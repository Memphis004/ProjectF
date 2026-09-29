using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using ProjectF.Infrastructure;
using ProjectF.Infrastructure.Blockchain;
using ProjectF.Infrastructure.Network;
using UnityEngine;

namespace ProjectF.Tests
{
    /// <summary>
    /// Stage 11.5 — the shutdown-hang regression tests (spec item 5).
    ///
    /// Repro of the original bug: start the client stack with NO server
    /// reachable (no seed, no hub), let the background loops spin for ~2s,
    /// then simulate exiting play mode (Dispose the whole stack) and assert
    /// shutdown completes within 5 seconds with NO dangling tracked task.
    ///
    /// The new-in-11.5 invariants are each covered explicitly:
    /// - every loop observes its cancellation token (a cancelled loop task
    ///   ends within its poll interval),
    /// - teardown awaits the tracked tasks instead of orphaning them,
    /// - dispose order: cancel → await → dispose (the registry is empty and
    ///   the client is dead only after the await phase),
    /// - connect attempts to a dead hub are bounded (~5s) — never unbounded.
    /// </summary>
    public sealed class Stage115ShutdownTests
    {
        private sealed class DeadClient : ILibplanetClient
        {
            public long TipIndex { get; set; }

            public string TipHash { get; set; } = string.Empty;

            public string TipPreviousHash { get; set; } = string.Empty;

            public ChainStatus Status { get; set; } = ChainStatus.Offline;

            public int PeerCount => 0;

            public string PlayerAddress => string.Empty;

            public SyncProgress SyncProgress => default;

            public Task<ChainStatus> BootstrapAsync(CancellationToken ct) =>
                Task.FromResult(ChainStatus.Offline);

            public Bencodex.Types.IValue? GetState(
                in Libplanet.Crypto.Address account, in Libplanet.Crypto.Address key) => null;

            public Task<long> StageAndWaitAsync(
                Bencodex.Types.Dictionary actionPlainValue, TimeSpan timeout,
                CancellationToken ct) => Task.FromResult(0L);
        }

        private static ProjectF.Infrastructure.UI.LocalizationService NewLoc() =>
            new(new ProjectF.Infrastructure.DataTables.UnityTableService());

        [Test]
        public void Shutdown_with_no_server_completes_within_5s_and_leaves_no_dangling_task()
        {
            var registry = new BackgroundTaskRegistry();
            DeadClient client = new DeadClient();
            KeyStore keys = new KeyStore(new NetworkSettings());

            using StateWatcher watcher = new StateWatcher(client, keys, registry);
            using var monitor = new ChainConnectionMonitor(client, NewLoc(), null, registry);
            using ActionQueue queue = new ActionQueue(client);
            using PlayerHubClient hub = new PlayerHubClient(
                new NetworkSettings(),
                new PresenceConnection(new NetworkSettings(), new PresenceReceiver(), registry),
                new PresenceReceiver());

            // "Play" for ~2s with no server: start every loop exactly like
            // AppBootstrapper does.
            watcher.Start();
            monitor.Start();

            var sw = Stopwatch.StartNew();
            Thread.Sleep(2000);

            // Simulate ExitingPlayMode → VContainer teardown (Dispose calls,
            // in the same order the scope unwinds: entry points' services
            // first, chain client last).
            hub.Dispose();
            queue.Dispose();
            monitor.Dispose();
            watcher.Dispose();

            // The whole teardown must fit the 5s budget (spec item 5).
            sw.Stop();
            Assert.Less(
                sw.ElapsedMilliseconds, 5000,
                $"shutdown took {sw.ElapsedMilliseconds}ms — the hang is back");

            // NO dangling tracked task: everything observed cancellation.
            (string Name, TimeSpan Age)[] alive = registry.AliveTasks();
            Assert.AreEqual(
                0, alive.Length,
                "dangling tasks after shutdown: " + string.Join(", ", Array.ConvertAll(alive, a => a.Name)));
        }

        [Test]
        public void Cancelled_loop_task_ends_within_its_poll_interval()
        {
            var registry = new BackgroundTaskRegistry();
            var cts = new BackedUpCts(registry);

            int ticks = 0;
            Task loop = cts.Run(
                "Test.Loop",
                TimeSpan.FromMilliseconds(50),
                () =>
                {
                    Interlocked.Increment(ref ticks);
                    return Task.CompletedTask;
                });

            Assert.IsFalse(loop.IsCompleted, "loop should be running");

            cts.Cancel();
            Task done = Task.WhenAny(loop, Task.Delay(1000)).GetAwaiter().GetResult();
            Assert.AreSame(loop, done, "cancelled loop did not observe its token within 1s");

            cts.AwaitOwnedAsync(TimeSpan.FromSeconds(1)).Wait();
            Assert.AreEqual(0, registry.AliveTasks().Length);
            cts.Dispose();
        }

        [Test]
        public void Registry_awaitAll_names_the_task_that_refused_to_stop()
        {
            var registry = new BackgroundTaskRegistry();

            // A task that ignores cancellation entirely — the "culprit"
            // shape. AwaitAll must return (bounded), not hang.
            Task stubborn = registry.Run(
                "Test.StubbornLoop",
                async () =>
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan);
                });

            DateTimeOffset started = DateTimeOffset.UtcNow;
            registry.AwaitAllAsync(TimeSpan.FromMilliseconds(300)).Wait();
            TimeSpan waited = DateTimeOffset.UtcNow - started;

            Assert.Less(waited.TotalSeconds, 3, "AwaitAllAsync must stay bounded");
            Assert.IsFalse(stubborn.IsCompleted, "sanity: the stubborn task is still alive");

            // Diagnostics must name it (what the editor guard prints).
            (string Name, TimeSpan Age)[] alive = registry.AliveTasks();
            Assert.AreEqual(1, alive.Length);
            Assert.AreEqual("Test.StubbornLoop", alive[0].Name);
            Assert.Greater(alive[0].Age.TotalMilliseconds, 0);
        }

        [Test]
        public void Dispose_order_is_cancel_then_await_then_dispose()
        {
            var registry = new BackgroundTaskRegistry();
            var cts = new BackedUpCts(registry);

            int observedCancel = 0;
            Task loop = cts.Run(
                "Test.OrderProbe",
                TimeSpan.FromMilliseconds(20),
                () =>
                {
                    Interlocked.Increment(ref observedCancel);
                    return Task.CompletedTask;
                });

            // Phase a: cancel. Phase b: await — after AwaitAll completes, the
            // loop task MUST be done (that is the "await, not orphan" rule
            // whose violation produced the BufferedFileLogStorage warnings).
            cts.Cancel();
            registry.AwaitAllAsync(TimeSpan.FromSeconds(2)).Wait();

            Assert.IsTrue(loop.IsCompleted, "tracked loop not awaited/completed after AwaitAll");
            int afterAwait = Interlocked.CompareExchange(ref observedCancel, 0, 0);
            Thread.Sleep(80);
            int afterSleep = Interlocked.CompareExchange(ref observedCancel, 0, 0);
            Assert.AreEqual(
                afterAwait, afterSleep,
                "loop kept running after teardown — cancellation not honoured");

            cts.Dispose();
        }

        [Test]
        public void Hub_connect_to_a_dead_address_is_bounded()
        {
            // Spec item 3: StreamingHubClient.ConnectAsync under a linked CTS
            // with a 5s cap. Against 127.0.0.1 on a closed port the failure
            // is immediate; the assertion is that it can NEVER exceed the
            // cap (a hung connect must be impossible now).
            //
            // IMPORTANT: run the connect on the THREADPOOL — a UniTask's
            // continuations resume on the Unity main thread, so blocking the
            // main thread with GetResult() here deadlocks forever (the very
            // hang Stage 11.5 exists to prevent; found by the stuck Test
            // Runner dialog). Task.Run keeps the main thread free.
            var settings = new NetworkSettings
            {
                HubHost = "127.0.0.1",
                HubPort = 1, // nothing listens here
            };
            PresenceConnection connection = new PresenceConnection(
                settings, new PresenceReceiver(), new BackgroundTaskRegistry());
            using PlayerHubClient hub = new PlayerHubClient(
                settings, connection, new PresenceReceiver());

            DateTimeOffset started = DateTimeOffset.UtcNow;
            bool completed = Task.Run(() =>
                    hub.ConnectAsync("test", 1, 0f, 0f, CancellationToken.None).AsTask())
                .Wait(TimeSpan.FromSeconds(8));
            TimeSpan elapsed = DateTimeOffset.UtcNow - started;

            Assert.IsTrue(completed, "connect against a dead hub did not return within 8s");
            Assert.IsFalse(hub.IsOnline, "dead hub must degrade to offline");
            Assert.Less(
                elapsed.TotalSeconds, 8,
                $"connect against a dead hub took {elapsed.TotalSeconds:0.0}s — unbounded wait is back");
        }
    }
}
