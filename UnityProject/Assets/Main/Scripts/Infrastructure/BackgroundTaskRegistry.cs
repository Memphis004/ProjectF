using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ProjectF.Infrastructure.Blockchain;
using ProjectF.Infrastructure.Network;

// ReSharper disable CheckNamespace
namespace ProjectF.Infrastructure
{
    /// <summary>
    /// Stage 11.5 — registry of every long-running background Task the app
    /// starts (swarm loop, peer-tip sampler, state/monitor pollers, presence
    /// disconnect watcher). Shutdown order (spec 2):
    /// a. cancel the shared CancellationTokenSource,
    /// b. AWAIT every registered task with a per-task timeout (3s default),
    ///    logging a loud warning naming any task that refused to stop,
    /// c. only then dispose the store / logger / gRPC channel.
    ///
    /// The registry is intentionally dumb: it tracks (name, task, startedAt)
    /// and never starts anything itself. Callers keep their own fire-and-forget
    /// ergonomics; disposal becomes observable instead of a silent hang.
    /// </summary>
    public sealed class BackgroundTaskRegistry
    {
        private readonly object _gate = new();
        private readonly List<Tracked> _tasks = new();

        private sealed class Tracked
        {
            public Tracked(string name, Task task, DateTimeOffset startedAt)
            {
                Name = name;
                Task = task;
                StartedAt = startedAt;
            }

            public string Name { get; }
            public Task Task { get; }
            public DateTimeOffset StartedAt { get; }
        }

        /// <summary>Fire-and-forget exception observer for background tasks —
        /// prevents UnobservedTaskException noise when teardown abandons one.
        /// Returns the continuation so callers CAN await it; typical use is
        /// discarding the result.</summary>
        public static Task Observe(Task task)
        {
            return task.ContinueWith(
                t => _ = t.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        /// <summary>Bounded wait for a task. Task.WaitAsync is .NET 6+ and
        /// does NOT exist on Unity 2022.3 Mono — WhenAny with a timer delay
        /// is the portable equivalent. Returns true when the task completed
        /// (any terminal state), false on timeout. Never throws. Always
        /// continues OFF the sync context (shutdown bridges block the main
        /// thread — a sync-context continuation deadlocks until timeout).</summary>
        public static async Task<bool> WaitForExitAsync(Task task, TimeSpan timeout)
        {
            if (task.IsCompleted)
            {
                return true;
            }

            Task done = await Task.WhenAny(task, Task.Delay(timeout)).ConfigureAwait(false);
            if (!ReferenceEquals(done, task))
            {
                // The timer won — observe the task's eventual exception so it
                // cannot surface as UnobservedTaskException later.
                _ = Observe(task);
                return false;
            }

            // Consume fault/cancel so the await never throws into shutdown.
            try
            {
                await task.ConfigureAwait(false);
            }
            catch
            {
                // Terminal state reached — that is all shutdown needs.
            }

            return true;
        }

        public void Add(string name, Task task, DateTimeOffset startedAt)
        {
            lock (_gate)
            {
                _tasks.Add(new Tracked(name, task, startedAt));
            }
        }

        /// <summary>Starts a tracked background task: Task.Run + observed
        /// exceptions + registry entry. The returned Task is the one to await
        /// during teardown (and the one the registry awaits).</summary>
        public Task Run(string name, Func<Task> body)
        {
            DateTimeOffset startedAt = DateTimeOffset.UtcNow;
            Task tracked = Task.Run(body);
            _ = Observe(tracked);
            Add(name, tracked, startedAt);
            return tracked;
        }

        /// <summary>Runs a loop body once per interval until cancelled.
        /// Returns promptly on cancellation (no OperationCanceledException
        /// escapes). This is THE loop primitive: every while(!ct…) loop in the
        /// app is replaced by this so the cancellation check is structural
        /// (spec 1: no infinite loop without a cancellation check).</summary>
        public async Task Loop(string name, TimeSpan interval, CancellationToken ct, Func<Task> body)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await body().ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception)
                {
                    // Loops are best-effort by design; the per-body catches
                    // usually log first. Swallow and keep the cadence.
                }

                try
                {
                    await Task.Delay(interval, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        /// <summary>Removes finished entries — called before diagnostics so
        /// the report only lists tasks that are actually still alive.</summary>
        public void Prune()
        {
            lock (_gate)
            {
                _tasks.RemoveAll(t => t.Task.IsCompleted);
            }
        }

        /// <summary>Diagnostics for the Editor shutdown guard: every task
        /// that has not completed, with its name and running time.</summary>
        public (string Name, TimeSpan Age)[] AliveTasks()
        {
            Prune();
            lock (_gate)
            {
                DateTimeOffset now = DateTimeOffset.UtcNow;
                var alive = new (string, TimeSpan)[_tasks.Count];
                for (int i = 0; i < _tasks.Count; i++)
                {
                    alive[i] = (_tasks[i].Name, now - _tasks[i].StartedAt);
                }

                return alive;
            }
        }

        /// <summary>Spec 2b: awaits every registered task with a per-task
        /// timeout. A task that does not finish in time produces a warning
        /// naming it and how long it had been running, then shutdown moves
        /// on — never block forever. Completed entries are removed; tasks
        /// that refused to stop STAY listed so the Editor guard can keep
        /// naming them.</summary>
        public async Task AwaitAllAsync(TimeSpan perTaskTimeout)
        {
            Tracked[] snapshot;
            lock (_gate)
            {
                snapshot = _tasks.ToArray();
            }

            foreach (Tracked tracked in snapshot)
            {
                bool exited;
                try
                {
                    exited = await WaitForExitAsync(tracked.Task, perTaskTimeout)
                        .ConfigureAwait(false);
                }
                catch
                {
                    exited = false;
                }

                if (exited)
                {
                    lock (_gate)
                    {
                        _tasks.Remove(tracked);
                    }
                }
                else
                {
                    UnityEngine.Debug.LogWarning(
                        "[shutdown] background task '" + tracked.Name + "' did NOT stop within " +
                        perTaskTimeout.TotalSeconds.ToString("0") + "s (was running " +
                        (DateTimeOffset.UtcNow - tracked.StartedAt) + ") — abandoning it.");
                    // Stays listed: the Editor shutdown guard keeps naming it.
                }
            }
        }

        public void Clear()
        {
            lock (_gate)
            {
                _tasks.Clear();
            }
        }
    }

    /// <summary>
    /// A CancellationTokenSource that OWNS the tasks started through it:
    /// teardown cancels and awaits ONLY its own tasks (bounded) — a service
    /// disposing early must never block on another service's still-running
    /// loop. The shared registry keeps everything for diagnostics (the Editor
    /// shutdown guard reports survivors by name).
    /// </summary>
    public sealed class BackedUpCts
    {
        private readonly CancellationTokenSource _cts = new();
        private readonly BackgroundTaskRegistry _registry;
        private readonly List<Task> _mine = new();
        private readonly object _gate = new();

        public BackedUpCts(BackgroundTaskRegistry registry)
        {
            _registry = registry;
        }

        public CancellationToken Token => _cts.Token;

        /// <summary>Run a tracked loop bound to this source's token.</summary>
        public Task Run(string name, TimeSpan interval, Func<Task> body)
        {
            Task task = _registry.Run(
                name,
                () => _registry.Loop(name, interval, _cts.Token, body));
            Track(task);
            return task;
        }

        /// <summary>Track any task (not just loops) against this source.</summary>
        public Task Run(string name, Func<Task> body)
        {
            Task task = _registry.Run(name, body);
            Track(task);
            return task;
        }

        private void Track(Task task)
        {
            lock (_gate)
            {
                _mine.Add(task);
            }
        }

        /// <summary>Spec 2a: cancel. Idempotent and dispose-safe — a second
        /// teardown path (Editor guard after container teardown) must never
        /// throw ObjectDisposedException into Unity's exit-play-mode flow
        /// (that throw itself hangs the reload — Stage 11.5 repro).</summary>
        public void Cancel()
        {
            try
            {
                _cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Already torn down by another path — nothing to cancel.
            }
        }

        /// <summary>Spec 2b (ownership-scoped): await THIS source's tasks
        /// with a per-task timeout — never another service's live loop.
        /// Continues off the sync context (shutdown bridges block the main
        /// thread; a sync-context continuation deadlocks until timeout).</summary>
        public async Task AwaitOwnedAsync(TimeSpan perTaskTimeout)
        {
            Task[] snapshot;
            lock (_gate)
            {
                snapshot = _mine.ToArray();
            }

            if (snapshot.Length == 0)
            {
                return;
            }

            foreach (Task task in snapshot)
            {
                bool exited = await BackgroundTaskRegistry
                    .WaitForExitAsync(task, perTaskTimeout).ConfigureAwait(false);
                if (!exited)
                {
                    UnityEngine.Debug.LogWarning(
                        "[shutdown] owned task did not stop within " +
                        perTaskTimeout.TotalSeconds.ToString("0") + "s — abandoning.");
                }
            }
        }

        public void Dispose() => _cts.Dispose();
    }

    /// <summary>
    /// Stage 11.5 — runtime side of the Editor shutdown-guard bridge.
    /// RootLifetimeScope fills these at container build; the Editor-only
    /// PlayModeShutdownGuard reads them on ExitingPlayMode and
    /// beforeAssemblyReload. (The runtime asmdef cannot reference the editor
    /// asmdef — the bridge flows editor←runtime, never the reverse.)
    /// </summary>
    public static class EditorShutdownHook
    {
        public static ILibplanetClient? Chain;
        public static IPresenceClient? Presence;
        public static StateWatcher? StateWatcher;
        public static ChainConnectionMonitor? Monitor;
        public static ActionQueue? ActionQueue;
        public static BackgroundTaskRegistry? Registry;

        public static void Clear()
        {
            Chain = null;
            Presence = null;
            StateWatcher = null;
            Monitor = null;
            ActionQueue = null;
            Registry = null;
        }
    }
}
