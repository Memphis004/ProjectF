using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Bencodex.Types;
using Libplanet.Crypto;
using ProjectF.Lib;
using ProjectF.Lib.States;
using UnityEngine;

using ProjectF.Infrastructure;

// ReSharper disable CheckNamespace
namespace ProjectF.Infrastructure.Blockchain
{
    /// <summary>
    /// Polls the player's avatar/inventory/pond state on new tips and raises
    /// typed events (spec section 7). Stage 7 shipped the polling loop +
    /// change detection.
    ///
    /// Stage 11 adds:
    /// - REORG DETECTION: the tip hash is remembered with its index. When the
    ///   index moves but the recorded parent hash is no longer in our recent
    ///   history, the new tip is not a descendant of the previous one → full
    ///   state re-read + <see cref="OnReorg"/> (depth = fork point distance).
    /// - PER-TIP DEBOUNCE: at most one full read per tip (AvatarUpdated is
    ///   raised once per tip unless something else changed it), so fast blocks
    ///   cannot flood the UI thread with rebuilds.
    /// </summary>
    public sealed class StateWatcher : IDisposable
    {
        private const int PollIntervalMs = 500;

        /// <summary>How many recent tip hashes are remembered for the
        /// descendant check. At 2s blocks this covers ~2 minutes of history —
        /// far beyond any realistic debounce window.</summary>
        private const int TipHistorySize = 64;

        private readonly ILibplanetClient _client;
        private readonly KeyStore _keyStore;
        private readonly BackgroundTaskRegistry _registry;
        private readonly BackedUpCts _cts;

        private long _lastTip = -1;
        private string _lastTipHash = string.Empty;
        private AvatarSnapshot? _last;

        /// <summary>Recent tip hashes (newest last) for reorg detection.</summary>
        private readonly List<string> _recentTipHashes = new(TipHistorySize);

        /// <summary>Hashes this tip index was already fully read at — the
        /// debounce set ("at most one full read per tip").</summary>
        private readonly HashSet<string> _readTips = new(StringComparer.Ordinal);

        /// <summary>Fired on every tip change (index, hex hash).</summary>
        public event Action<long, string>? TipChanged;

        /// <summary>Fired after each poll with the confirmed world view.</summary>
        public event Action<AvatarSnapshot>? AvatarUpdated;

        /// <summary>Stage 11: fired when the new tip is NOT a descendant of
        /// the previous tip — the depth is how many blocks were dropped from
        /// our previous view (1 = the previous tip itself was orphaned).</summary>
        public event Action<long>? OnReorg;

        public AvatarSnapshot? Current => _last;

        public StateWatcher(ILibplanetClient client, KeyStore keyStore)
            : this(client, keyStore, new BackgroundTaskRegistry())
        {
        }

        public StateWatcher(ILibplanetClient client, KeyStore keyStore, BackgroundTaskRegistry registry)
        {
            _client = client;
            _keyStore = keyStore;
            _registry = registry;
            _cts = new BackedUpCts(registry);
        }

        public void Start()
        {
            // PollOnce touches NO Unity main-thread-only API — it reads the
            // client and raises C# events. The threadpool loop is therefore
            // safe here, and it is cancellation-checked + tracked (Stage
            // 11.5): teardown cancels the token and awaits the task.
            _cts.Run(
                "StateWatcher.Poll",
                TimeSpan.FromMilliseconds(PollIntervalMs),
                () =>
                {
                    try
                    {
                        PollOnce();
                    }
                    catch (Exception ex)
                    {
                        // Never let the watch loop die — log and keep polling.
                        Debug.LogWarning($"[state] poll failed: {ex.Message}");
                    }

                    return Task.CompletedTask;
                });
        }

        /// <summary>Spec 2 order: cancel → await the poll task (bounded) →
        /// nothing else to dispose (the watcher owns no unmanaged resources).
        /// Awaits ONLY this watcher's task — never another service's loop.</summary>
        public async Task DisposeAsync()
        {
            _cts.Cancel();
            // ConfigureAwait(false): the sync Dispose bridge blocks the main
            // thread — a sync-context continuation would deadlock until the
            // timeout (the 14s EditMode repro). Finish on the threadpool.
            await _cts.AwaitOwnedAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
            _cts.Dispose();
        }

        public void Dispose()
        {
            // Sync bridge (tests / VContainer teardown): bounded — never
            // blocks forever (spec 2). Task.Run FIRST: awaiting on the main
            // thread captures the Unity sync context, which is blocked in
            // .Wait — deadlock until the timeout (the EditMode 14s repro).
            try
            {
                Task.Run(() => DisposeAsync()).Wait(TimeSpan.FromSeconds(4));
            }
            catch
            {
                // Shutdown must never throw into teardown.
            }
        }

        /// <summary>One read of the confirmed world at the current tip. Raises
        /// events only when something actually changed (tip moved or the
        /// avatar fields moved — stamina regen moves state without the tip).</summary>
        public void PollOnce()
        {
            if (_client.Status is ChainStatus.Bootstrapping)
            {
                return;
            }

            long tip = _client.TipIndex;
            string tipHash = _client.TipHash;

            bool tipMoved = tip != _lastTip || tipHash != _lastTipHash;

            // --- Stage 11: reorg detection -------------------------------
            // The tip moved: the new tip extends the old one IFF its PARENT is
            // the previously-seen tip (or already part of our recent history).
            // If the parent is neither, blocks we already read state from were
            // replaced → re-read everything from scratch and raise OnReorg.
            if (tipMoved && _lastTip >= 0 && tipHash != _lastTipHash)
            {
                string parentHash = _client.TipPreviousHash;
                bool descendant = string.IsNullOrEmpty(parentHash)
                    || parentHash == _lastTipHash
                    || _recentTipHashes.Contains(parentHash);
                if (!descendant)
                {
                    long depth = Math.Min(tip - _lastTip, Math.Max(1, _recentTipHashes.Count));
                    Debug.LogWarning(
                        $"[state] REORG detected at tip #{tip}: new tip's parent " +
                        $"{Short(parentHash)} is neither the previous tip " +
                        $"{Short(_lastTipHash)} nor in recent history — re-reading " +
                        "all watched state from scratch.");
                    OnReorg?.Invoke(depth);
                    _readTips.Clear();
                }
            }

            // --- Stage 11: per-tip debounce ------------------------------
            // At most one FULL read per (tip, hash): stamina regen is stamped
            // per block, so a moved tip may still change values, but polling
            // the SAME tip twice can only re-read identical state.
            bool alreadyRead = !tipMoved || _readTips.Contains(tipHash);

            if (tipMoved)
            {
                RememberTip(tip, tipHash);
                TipChanged?.Invoke(tip, tipHash);
            }

            AvatarSnapshot snapshot;
            if (alreadyRead && _last is { })
            {
                // Debounced: reuse the previous read for this exact tip. (The
                // events still fire below so the optimistic overlay stays live.)
                snapshot = _last;
            }
            else
            {
                snapshot = ReadSnapshot(tip);
                _readTips.Add(tipHash);
                while (_readTips.Count > TipHistorySize)
                {
                    _readTips.RemoveOldest();
                }
            }

            bool stateMoved = _last is null
                || snapshot.Stamina != _last.Stamina
                || snapshot.Gold != _last.Gold
                || snapshot.FishingExp != _last.FishingExp
                || snapshot.CookingExp != _last.CookingExp
                || snapshot.KitchenUnlocked != _last.KitchenUnlocked
                || !InventoryEquals(_last.Inventory, snapshot.Inventory)
                || !TasksEquals(_last.Tasks, snapshot.Tasks);

            _lastTip = tip;
            _lastTipHash = tipHash;
            _last = snapshot;

            if (stateMoved || tipMoved)
            {
                AvatarUpdated?.Invoke(snapshot);
            }
        }

        private void RememberTip(long tip, string tipHash)
        {
            if (_lastTipHash is { Length: > 0 } && _lastTip >= 0)
            {
                _recentTipHashes.Add(_lastTipHash);
                while (_recentTipHashes.Count > TipHistorySize)
                {
                    _recentTipHashes.RemoveAt(0);
                }
            }
        }

        private AvatarSnapshot ReadSnapshot(long tip)
        {
            Address avatarAddress;
            try
            {
                avatarAddress = _keyStore.LoadOrCreatePlayerKey().Address;
            }
            catch (Exception)
            {
                // No key yet (pre-bootstrap UI) — report an empty world.
                return new AvatarSnapshot { BlockIndex = tip };
            }

            IValue? avatarValue = _client.GetState(Addresses.Avatar, avatarAddress);
            IValue? invValue = _client.GetState(Addresses.Inventory, avatarAddress);
            IValue? taskBoardValue = _client.GetState(Addresses.TaskBoard, avatarAddress);

            long stamina = 0, maxStamina = 0, gold = 0, fishingExp = 0, cookingExp = 0;
            string name = string.Empty;
            bool kitchen = false;

            if (avatarValue is Dictionary dict)
            {
                var avatar = new AvatarState(dict);
                name = avatar.Name;
                // Mirror the on-chain lazy regen for DISPLAY purposes: the
                // chain's authoritative value is what the last block stamped;
                // we show what the current block WOULD stamp (same formula as
                // AvatarState.SyncStamina, knowledge.md rule 3).
                long elapsed = Math.Max(0, tip - avatar.StaminaUpdatedAt);
                stamina = Math.Min(avatar.MaxStamina, avatar.Stamina + elapsed);
                maxStamina = avatar.MaxStamina;
                gold = avatar.Gold;
                fishingExp = avatar.FishingExp;
                cookingExp = avatar.CookingExp;
                kitchen = avatar.KitchenUnlocked;
            }

            var inventory = new Dictionary<int, long>();
            if (invValue is Dictionary invDict)
            {
                var inv = new Inventory(invDict);
                foreach (KeyValuePair<int, long> kv in inv.All)
                {
                    inventory[kv.Key] = kv.Value;
                }
            }

            // Stage 10: taskboard (never exists before create_avatar — empty).
            var tasks = new Dictionary<int, bool>();
            long tasksLastRerolledAt = 0;
            if (taskBoardValue is Dictionary taskDict)
            {
                var board = new TaskBoardState(taskDict);
                foreach (KeyValuePair<int, bool> kv in board.Tasks)
                {
                    tasks[kv.Key] = kv.Value;
                }

                tasksLastRerolledAt = board.LastRerolledAt;
            }

            return AvatarSnapshot.FromStates(
                name, tip, stamina, maxStamina, gold,
                AvatarState.LevelFromExp(fishingExp), fishingExp,
                AvatarState.LevelFromExp(cookingExp), cookingExp,
                kitchen, inventory, tasks, tasksLastRerolledAt);
        }

        private static string Short(string hash) =>
            hash.Length <= 10 ? hash : hash[..10];

        private static bool TasksEquals(
            IReadOnlyDictionary<int, bool> a, IReadOnlyDictionary<int, bool> b)
        {
            if (a.Count != b.Count)
            {
                return false;
            }

            foreach (KeyValuePair<int, bool> kv in a)
            {
                if (!b.TryGetValue(kv.Key, out bool completed) || completed != kv.Value)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool InventoryEquals(
            IReadOnlyDictionary<int, long> a, IReadOnlyDictionary<int, long> b)
        {
            if (a.Count != b.Count)
            {
                return false;
            }

            foreach (KeyValuePair<int, long> kv in a)
            {
                if (!b.TryGetValue(kv.Key, out long count) || count != kv.Value)
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>Tiny HashSet<T> extension: remove the "oldest" (first) entry —
    /// HashSet iteration order is insertion order until removals happen; for
    /// our bounded window that is exactly the FIFO behaviour we want.</summary>
    internal static class StateWatcherExtensions
    {
        public static void RemoveOldest(this HashSet<string> set)
        {
            foreach (string item in set)
            {
                set.Remove(item);
                return;
            }
        }
    }
}
