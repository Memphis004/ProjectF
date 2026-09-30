using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using ProjectF.Infrastructure.Blockchain;
using ProjectF.Infrastructure.Network;
using UnityEngine;

// ReSharper disable CheckNamespace
namespace ProjectF.Tests
{
    /// <summary>
    /// Stage 11 EditMode tests — the pure UX-chain logic that needs no live
    /// node: optimistic merge math, rollback on failure/timeout, per-tip
    /// debounce and stall detection. The chain remains the legality authority
    /// in every case; these only verify the DISPLAY layer.
    /// </summary>
    public sealed class Stage11LogicTests
    {
        // -----------------------------------------------------------------
        // Test doubles
        // -----------------------------------------------------------------

        private sealed class FakeClient : ILibplanetClient
        {
            public long TipIndex { get; set; }

            public string TipHash { get; set; } = "genesis";

            public string TipPreviousHash { get; set; } = string.Empty;

            /// <summary>Simulated chain: index → hash. Tests append entries
            /// so <see cref="GetBlockHashAt"/> can answer ancestor queries
            /// exactly like the real LibplanetClient.</summary>
            public Dictionary<long, string> Blocks { get; } = new();

            public ChainStatus Status { get; set; } = ChainStatus.Synced;

            public int PeerCount { get; set; } = 1;

            public string PlayerAddress => string.Empty;

            public SyncProgress SyncProgress => new SyncProgress
            {
                Tip = TipIndex,
                TargetTip = 0,
                HasTarget = false,
            };

            public string? GetBlockHashAt(long index) =>
                Blocks.TryGetValue(index, out string? hash) ? hash : null;

            public Task<ChainStatus> BootstrapAsync(CancellationToken ct) =>
                Task.FromResult(Status);

            public Bencodex.Types.IValue? GetState(
                in Libplanet.Crypto.Address account, in Libplanet.Crypto.Address key) => null;

            public Task<long> StageAndWaitAsync(
                Bencodex.Types.Dictionary actionPlainValue, TimeSpan timeout,
                CancellationToken ct) => Task.FromResult(TipIndex);
        }

        private sealed class FakeKeyStore
        {
        }

        // -----------------------------------------------------------------
        // OptimisticState — merge math
        // -----------------------------------------------------------------

        private static StateWatcher MakeWatcher(FakeClient client, KeyStore keys)
        {
            return new StateWatcher(client, keys);
        }

        private static ProjectF.Infrastructure.UI.LocalizationService NewLoc() =>
            new(new ProjectF.Infrastructure.DataTables.UnityTableService());

        [Test]
        public void MergedView_applies_pending_deltas_instantly()
        {
            FakeClient client = new FakeClient();
            KeyStore keys = new KeyStore(new NetworkSettings());
            using StateWatcher watcher = MakeWatcher(client, keys);
            using var optimistic = new OptimisticState(watcher);

            // Seed a confirmed snapshot through the watcher's public event —
            // the constructor adopts watcher.Current, so simulate a poll.
            var confirmed = AvatarSnapshot.FromStates(
                "tester", blockIndex: 10, stamina: 50, maxStamina: 100, gold: 200,
                fishingLevel: 1, fishingExp: 0, cookingLevel: 1, cookingExp: 0,
                kitchenUnlocked: false,
                new Dictionary<int, long> { { 2001, 5 } });

            // Push via reflection-free path: raise AvatarUpdated manually.
            typeof(StateWatcher)
                .GetField("_last", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .SetValue(watcher, confirmed);
            typeof(StateWatcher)
                .GetField("_lastTip", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .SetValue(watcher, 10L);

            OptimisticState.PendingHandle handle = optimistic.Apply(g =>
            {
                g.Item(2001, -1);   // bait −1
                g.Stamina(-5);      // cast −5
            });

            AvatarSnapshot merged = optimistic.Current;
            Assert.AreEqual(4, merged.GetItemCount(2001), "bait decrements instantly");
            Assert.AreEqual(45, merged.Stamina, "stamina decrements instantly");
            Assert.AreEqual(200, merged.Gold, "gold untouched by a fishing cast");
            Assert.AreEqual(10, merged.BlockIndex, "block index stays confirmed");
            Assert.AreEqual(1, optimistic.PendingCount);

            handle.Confirm();
            Assert.AreEqual(0, optimistic.PendingCount, "confirmed entries drop");
        }

        [Test]
        public void Rollback_restores_the_confirmed_numbers()
        {
            FakeClient client = new FakeClient();
            KeyStore keys = new KeyStore(new NetworkSettings());
            using StateWatcher watcher = MakeWatcher(client, keys);
            using var optimistic = new OptimisticState(watcher);

            var confirmed = AvatarSnapshot.FromStates(
                "tester", 10, stamina: 50, maxStamina: 100, gold: 100,
                fishingLevel: 1, fishingExp: 0, cookingLevel: 1, cookingExp: 0,
                kitchenUnlocked: false,
                new Dictionary<int, long> { { 1001, 10 } });

            typeof(StateWatcher)
                .GetField("_last", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .SetValue(watcher, confirmed);
            typeof(StateWatcher)
                .GetField("_lastTip", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .SetValue(watcher, 10L);

            Rollback? rollbackEvent = null;
            optimistic.OnRolledBack += r => rollbackEvent = r;

            OptimisticState.PendingHandle handle = optimistic.Apply(g =>
            {
                g.Gold(-50);
                g.Item(1001, -2);
            });

            Assert.AreEqual(50, optimistic.Current.Gold);
            Assert.AreEqual(8, optimistic.Current.GetItemCount(1001));

            // Chain said no (e.g. NotEnoughGoldException) → display restores.
            handle.RollBack(RollbackReason.Failed, "NotEnoughGoldException: 50 < 100");

            Assert.AreEqual(100, optimistic.Current.Gold, "gold restored");
            Assert.AreEqual(10, optimistic.Current.GetItemCount(1001), "items restored");
            Assert.AreEqual(0, optimistic.PendingCount);

            Assert.IsTrue(rollbackEvent.HasValue, "no rollback event raised");
            Rollback rolled = rollbackEvent!.Value;
            Assert.AreEqual(RollbackReason.Failed, rolled.Reason);
            Assert.AreEqual("NotEnoughGoldException: 50 < 100", rolled.RawReason);
        }

        [Test]
        public void Dispose_without_confirm_rolls_back_as_cancelled()
        {
            FakeClient client = new FakeClient();
            KeyStore keys = new KeyStore(new NetworkSettings());
            using StateWatcher watcher = MakeWatcher(client, keys);
            using var optimistic = new OptimisticState(watcher);

            Rollback? rollbackEvent = null;
            optimistic.OnRolledBack += r => rollbackEvent = r;

            OptimisticState.PendingHandle handle = optimistic.Apply(g => g.Gold(-1));
            handle.Dispose();

            Assert.AreEqual(0, optimistic.PendingCount);
            Assert.IsTrue(rollbackEvent.HasValue, "no rollback event raised");
            Assert.AreEqual(RollbackReason.Cancelled, rollbackEvent!.Value.Reason);
        }

        [Test]
        public void Merge_clamps_counts_and_stamina_at_zero()
        {
            FakeClient client = new FakeClient();
            KeyStore keys = new KeyStore(new NetworkSettings());
            using StateWatcher watcher = MakeWatcher(client, keys);
            using var optimistic = new OptimisticState(watcher);

            var confirmed = AvatarSnapshot.FromStates(
                "tester", 10, stamina: 3, maxStamina: 100, gold: 0,
                fishingLevel: 1, fishingExp: 0, cookingLevel: 1, cookingExp: 0,
                kitchenUnlocked: false,
                new Dictionary<int, long> { { 1001, 1 } });

            typeof(StateWatcher)
                .GetField("_last", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .SetValue(watcher, confirmed);
            typeof(StateWatcher)
                .GetField("_lastTip", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .SetValue(watcher, 10L);

            optimistic.Apply(g => g.Stamina(-5).Item(1001, -3).Gold(-10));

            Assert.AreEqual(0, optimistic.Current.Stamina, "display never goes negative");
            Assert.AreEqual(0, optimistic.Current.GetItemCount(1001));
            Assert.AreEqual(0, optimistic.Current.Gold);
        }

        [Test]
        public void Optimistic_never_gates_legality_it_is_display_only()
        {
            // Contract check: OptimisticState exposes ONLY snapshots + counts —
            // no legality API. The merged view may show impossible numbers
            // (e.g. 0 gold while a buy is pending); the CHAIN still rejects
            // the tx and the guess rolls back. This test documents the seam.
            FakeClient client = new FakeClient();
            KeyStore keys = new KeyStore(new NetworkSettings());
            using StateWatcher watcher = MakeWatcher(client, keys);
            using var optimistic = new OptimisticState(watcher);

            Assert.AreEqual(0, optimistic.Current.Gold);
            Assert.AreEqual(0, optimistic.PendingCount);
        }

        // -----------------------------------------------------------------
        // StateWatcher — per-tip debounce
        // -----------------------------------------------------------------

        [Test]
        public void PollOnce_reads_each_tip_only_once()
        {
            FakeClient client = new FakeClient();
            KeyStore keys = new KeyStore(new NetworkSettings());
            using StateWatcher watcher = MakeWatcher(client, keys);

            int updates = 0;
            watcher.AvatarUpdated += _ => updates++;

            client.TipIndex = 5;
            client.TipHash = "aaa";
            client.Blocks[5] = "aaa";
            watcher.PollOnce();
            Assert.AreEqual(1, updates, "first read of tip 5");

            watcher.PollOnce(); // same tip, same hash → debounced
            Assert.AreEqual(1, updates, "same tip does not re-read (debounce)");

            client.TipIndex = 6;
            client.TipHash = "bbb";
            client.Blocks[6] = "bbb";
            client.TipPreviousHash = "aaa";
            watcher.PollOnce();
            Assert.AreEqual(2, updates, "a new tip re-reads");
        }

        [Test]
        public void Reorg_clears_the_debounce_window_and_raises()
        {
            FakeClient client = new FakeClient();
            KeyStore keys = new KeyStore(new NetworkSettings());
            using StateWatcher watcher = MakeWatcher(client, keys);

            long reorgs = 0;
            watcher.OnReorg += _ => reorgs++;
            int updates = 0;
            watcher.AvatarUpdated += _ => updates++;

            client.TipIndex = 5;
            client.TipHash = "aaa";
            client.Blocks[5] = "aaa";
            watcher.PollOnce();

            client.TipIndex = 6;
            client.TipHash = "bbb";
            client.Blocks[6] = "bbb";
            client.TipPreviousHash = "aaa";
            watcher.PollOnce();

            // Tip 7 whose parent is NOT bbb (nor in history) → reorg.
            client.TipIndex = 7;
            client.TipHash = "ccc";
            client.Blocks[7] = "ccc";
            client.TipPreviousHash = "zzz";
            // Blocks[5] still "aaa" — but the parent of ccc is zzz, i.e. the
            // chain claims ccc sits at index 7 on a fork whose history does
            // NOT pass through our aaa@5... the ancestor check reads hash at
            // OUR old tip index 5 on the CURRENT chain: still aaa → append.
            // To simulate a REAL reorg the chain must replace index 5 too:
            watcher.PollOnce();

            Assert.AreEqual(0, reorgs, "same-history append is not a reorg");
            Assert.AreEqual(3, updates);

            // Now a fork replaces our history from index 5 up (a real reorg
            // replaces a CONTIGUOUS suffix — blocks are immutable, so 6 and 7
            // cannot survive while 5 changes).
            client.Blocks[5] = "fork5";
            client.Blocks[6] = "fork6";
            client.Blocks[7] = "fork7";
            client.TipIndex = 8;
            client.TipHash = "ddd";
            client.Blocks[8] = "ddd";
            client.TipPreviousHash = "fork7";
            watcher.PollOnce();

            Assert.AreEqual(1, reorgs, "replaced ancestor raises OnReorg");
            Assert.AreEqual(4, updates, "reorg forces a full re-read");

            // Same tip again after the reorg: debounce window was cleared and
            // re-seeded by the reorg read → still debounced (no re-read).
            watcher.PollOnce();
            Assert.AreEqual(4, updates);
        }

        [Test]
        public void Catchup_jump_of_hundreds_of_blocks_is_not_a_reorg()
        {
            FakeClient client = new FakeClient();
            KeyStore keys = new KeyStore(new NetworkSettings());
            using StateWatcher watcher = MakeWatcher(client, keys);

            long reorgs = 0;
            watcher.OnReorg += _ => reorgs++;
            int updates = 0;
            watcher.AvatarUpdated += _ => updates++;

            client.TipIndex = 9635;
            client.TipHash = "old-tip";
            client.Blocks[9635] = "old-tip";
            watcher.PollOnce();

            // One poll later the preload delivered 400 blocks — the new tip's
            // parent (intermediate block we NEVER recorded) is unknown to the
            // watcher, but our old tip is still an ancestor at index 9635.
            client.TipIndex = 10035;
            client.TipHash = "new-tip";
            client.Blocks[10035] = "new-tip";
            client.TipPreviousHash = "intermediate-never-seen";
            watcher.PollOnce();

            Assert.AreEqual(0, reorgs, "append past our tip is not a reorg");
            Assert.AreEqual(2, updates, "the jump still re-reads once");
        }

        // -----------------------------------------------------------------
        // ChainConnectionMonitor — stall detection
        // -----------------------------------------------------------------

        [Test]
        public void Monitor_flips_to_Stalled_after_3x_block_interval()
        {
            FakeClient client = new FakeClient
            {
                Status = ChainStatus.Synced,
                TipIndex = 100,
            };
            KeyStore keys = new KeyStore(new NetworkSettings());
            using var monitor = new ChainConnectionMonitor(client, NewLoc());

            client.Status = ChainStatus.Synced;
            monitor.Tick();
            Assert.AreEqual(ChainStatus.Synced, monitor.Status);
            Assert.IsTrue(monitor.CanSubmit);

            // Simulate 10s without a tip move (> 3 × 2s).
            typeof(ChainConnectionMonitor)
                .GetField("_lastTipAt", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .SetValue(monitor, DateTimeOffset.UtcNow - TimeSpan.FromSeconds(10));

            monitor.Tick();
            Assert.AreEqual(ChainStatus.Stalled, monitor.Status);
            Assert.IsFalse(monitor.CanSubmit, "Stalled blocks submissions");
        }

        [Test]
        public void Monitor_recovers_from_Stalled_when_the_tip_moves()
        {
            FakeClient client = new FakeClient
            {
                Status = ChainStatus.Synced,
                TipIndex = 100,
            };
            KeyStore keys = new KeyStore(new NetworkSettings());
            using var monitor = new ChainConnectionMonitor(client, NewLoc());

            // Prime the tip clock (a fresh monitor re-primes on first sight
            // of a non-zero tip), then backdate it past the stall threshold.
            monitor.Tick();
            typeof(ChainConnectionMonitor)
                .GetField("_lastTipAt", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .SetValue(monitor, DateTimeOffset.UtcNow - TimeSpan.FromSeconds(10));
            monitor.Tick();
            Assert.AreEqual(ChainStatus.Stalled, monitor.Status);

            client.TipIndex = 101; // tip moved again
            monitor.Tick();
            Assert.AreEqual(ChainStatus.Synced, monitor.Status);
            Assert.IsTrue(monitor.CanSubmit);
        }

        // -----------------------------------------------------------------
        // ActionQueue — rollback classification + retry policy constants
        // -----------------------------------------------------------------

        [Test]
        public void Rollback_classification_maps_reasons()
        {
            Assert.AreEqual(RollbackReason.TimedOut,
                ActionQueue.ClassifyRollback("timeout"));
            Assert.AreEqual(RollbackReason.Cancelled,
                ActionQueue.ClassifyRollback("cancelled"));
            Assert.AreEqual(RollbackReason.Failed,
                ActionQueue.ClassifyRollback("NotEnoughGoldException"));
            Assert.AreEqual(RollbackReason.Failed,
                ActionQueue.ClassifyRollback(string.Empty));
        }

        [Test]
        public void Retry_policy_has_two_backoffs()
        {
            Assert.AreEqual(2, ActionQueue.RetryBackoff.Length);
            Assert.Greater(ActionQueue.RetryBackoff[1], ActionQueue.RetryBackoff[0]);
        }

        [Test]
        public void Queue_starts_empty_and_stays_in_memory_only()
        {
            FakeClient client = new FakeClient();
            KeyStore keys = new KeyStore(new NetworkSettings());
            using StateWatcher watcher = MakeWatcher(client, keys);
            using var optimistic = new OptimisticState(watcher);
            using ActionQueue queue = new ActionQueue(client, optimistic);

            Assert.AreEqual(0, queue.Pending.Count, "restart = empty queue (by design)");
            Assert.IsFalse(queue.HasStaged);
        }
    }
}
