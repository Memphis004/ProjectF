using System;
using System.Collections.Generic;
using System.Linq;

// ReSharper disable CheckNamespace
namespace ProjectF.Infrastructure.Blockchain
{
    /// <summary>
    /// One local guess of "what the confirmed state WILL look like once my tx
    /// lands" (e.g. bait −1, stamina −5, gold −50). Numeric fields are DELTAS
    /// applied to the confirmed snapshot; flags are absolute (a mutation can
    /// only unlock the kitchen, never lock it); inventory counts are additive
    /// per item id.
    /// </summary>
    public sealed class PendingMutation
    {
        // Private setters: the fluent builder below is the only writer (init
        // accessors cannot be assigned from instance methods — CS8852).
        public long StaminaDelta { get; private set; }

        public long GoldDelta { get; private set; }

        public long FishingExpDelta { get; private set; }

        public long CookingExpDelta { get; private set; }

        public bool? KitchenUnlocked { get; private set; }

        /// <summary>Inventory deltas (itemId → +N / −N).</summary>
        public IReadOnlyDictionary<int, long> InventoryDelta { get; private set; }
            = new Dictionary<int, long>();

        public static PendingMutation Builder() => new();

        public PendingMutation Stamina(long delta)
        {
            StaminaDelta += delta;
            return this;
        }

        public PendingMutation Gold(long delta)
        {
            GoldDelta += delta;
            return this;
        }

        public PendingMutation FishingExp(long delta)
        {
            FishingExpDelta += delta;
            return this;
        }

        public PendingMutation CookingExp(long delta)
        {
            CookingExpDelta += delta;
            return this;
        }

        public PendingMutation SetKitchenUnlocked()
        {
            KitchenUnlocked = true;
            return this;
        }

        public PendingMutation Item(int itemId, long delta)
        {
            if (itemId <= 0 || delta == 0)
            {
                return this;
            }

            var dict = new Dictionary<int, long>(InventoryDelta);
            dict.TryGetValue(itemId, out long current);
            dict[itemId] = current + delta;
            InventoryDelta = dict;
            return this;
        }
    }

    /// <summary>Why a pending mutation was dropped and the display rolled back.</summary>
    public enum RollbackReason
    {
        /// <summary>The action threw on-chain (validation) — the state never changed.</summary>
        Failed = 0,

        /// <summary>No confirmation within the timeout budget.</summary>
        TimedOut = 1,

        /// <summary>The submission was cancelled before it confirmed.</summary>
        Cancelled = 2,

        /// <summary>Confirmed state disagreed with the optimistic guess beyond
        /// tolerance — the guess was wrong (regen while waiting, remote
        /// spends, …).</summary>
        Reconciled = 3,
    }

    /// <summary>Payload of <see cref="OptimisticState.OnRolledBack"/> — what the
    /// UI shows in the "ยกเลิกรายการ: …" toast and what it stops highlighting.</summary>
    public readonly struct Rollback
    {
        public Rollback(RollbackReason reason, PendingMutation? mutation, string rawReason = "")
        {
            Reason = reason;
            Mutation = mutation;
            RawReason = rawReason;
        }

        public RollbackReason Reason { get; }

        /// <summary>The mutation that was dropped (may be null when a reorg
        /// cleared everything).</summary>
        public PendingMutation? Mutation { get; }

        /// <summary>The raw failure text from the ActionQueue (exception type
        /// name recorded on-chain, "timeout", a transport message …) —
        /// mappable through ErrorMapper for the toast.</summary>
        public string RawReason { get; }
    }

    /// <summary>
    /// Stage 11 — optimistic display state. Holds the confirmed
    /// <see cref="AvatarSnapshot"/> (from StateWatcher) plus an ordered list
    /// of pending local mutations, and exposes a MERGED view that the UI
    /// binds to, so inventory/stamina/gold move the instant the player clicks.
    ///
    /// HARD CONTRACT (spec stage 11): this is DISPLAY ONLY. Nothing here may
    /// ever decide legality — the chain is the sole authority and throws on
    /// any violation. Optimistic numbers may drift (stamina regen while a tx
    /// waits, remote players buying the last stock); confirmation snaps the
    /// display back to truth, and disagreement beyond tolerance is logged as
    /// a reconciliation warning with both values.
    /// </summary>
    public sealed class OptimisticState : IDisposable
    {
        /// <summary>How far the confirmed value may differ from the merged
        /// guess on CONFIRM before we log a reconciliation warning. 0 means
        /// exact match expected. Stamina legitimately regenerates while a tx
        /// waits for a block, so it gets a regen-window allowance; everything
        /// else is exact.</summary>
        private const long StaminaTolerance = 10;
        private const long ExactTolerance = 0;

        private readonly StateWatcher _watcher;
        private readonly List<PendingMutation> _pending = new();
        private AvatarSnapshot? _confirmed;

        public OptimisticState(StateWatcher watcher)
        {
            _watcher = watcher;
            _watcher.AvatarUpdated += OnConfirmedUpdate;
            _confirmed = watcher.Current;
        }

        /// <summary>Fired whenever the merged view changed (pending applied,
        /// confirmed update, or rollback) — presenters re-render from this.</summary>
        public event Action<AvatarSnapshot>? MergedUpdated;

        /// <summary>Raised when a pending mutation was dropped and the display
        /// rolled back (failure, timeout, cancel, or reconciliation).</summary>
        public event Action<Rollback>? OnRolledBack;

        /// <summary>The merged display view — confirmed snapshot + every
        /// pending mutation applied in order. Bind UI to THIS, never to the
        /// raw watcher snapshot (and never gate legality on it).</summary>
        public AvatarSnapshot Current => Confirmed is { } confirmed
            ? Merge(confirmed, _pending)
            : new AvatarSnapshot();

        /// <summary>The confirmed snapshot untouched by any guess. Coalesced
        /// with the watcher's current snapshot: if the watcher polled before
        /// this overlay was constructed (or between polls), we still show the
        /// freshest confirmed world.</summary>
        public AvatarSnapshot? Confirmed => _confirmed ?? _watcher.Current;

        /// <summary>Number of mutations currently optimistic (HUD "…n" badge).</summary>
        public int PendingCount => _pending.Count;

        public void Dispose()
        {
            _watcher.AvatarUpdated -= OnConfirmedUpdate;
            _pending.Clear();
        }

        /// <summary>
        /// Applies a pending guess and returns a handle whose Dispose marks
        /// the mutation resolved. DISPOSING WITHOUT RESOLVING rolls back
        /// (failed/cancelled path). On success, confirm with
        /// <see cref="PendingHandle.Confirm()"/> — the entry is dropped and
        /// the confirmed snapshot (adopted via the next/last watcher update)
        /// takes over.
        /// </summary>
        public PendingHandle ApplyPending(PendingMutation mutation)
        {
            _pending.Add(mutation);
            MergedUpdated?.Invoke(Current);
            return new PendingHandle(this, mutation, _confirmed);
        }

        /// <summary>Convenience for the common UI flow: build → apply →
        /// resolve when the action settles. never blocks; the handle pattern
        /// exists for the two-step occupy→fish cast.</summary>
        public PendingHandle Apply(Action<PendingMutation>? build = null)
        {
            var mutation = new PendingMutation();
            build?.Invoke(mutation);
            return ApplyPending(mutation);
        }

        /// <summary>Drops ALL pending mutations (reorg recovery) and raises
        /// one rollback per entry so every window re-renders from truth.</summary>
        public void RollbackAll(RollbackReason reason)
        {
            if (_pending.Count == 0)
            {
                return;
            }

            List<PendingMutation> dropped = _pending.ToList();
            _pending.Clear();
            MergedUpdated?.Invoke(Current);
            foreach (PendingMutation mutation in dropped)
            {
                OnRolledBack?.Invoke(new Rollback(reason, mutation));
            }
        }

        private void Resolve(
            PendingMutation mutation, bool confirm, RollbackReason reason,
            string rawReason = "")
        {
            // Remove the FIRST equal mutation (FIFO — matches the queue order
            // the deltas were applied in).
            int index = _pending.FindIndex(m => ReferenceEquals(m, mutation));
            if (index < 0)
            {
                return; // already dropped (reorg / double resolve) — ignore.
            }

            _pending.RemoveAt(index);
            if (!confirm)
            {
                MergedUpdated?.Invoke(Current);
                OnRolledBack?.Invoke(new Rollback(reason, mutation, rawReason));
            }
            else
            {
                // Adopt the freshest confirmed snapshot now that the guess is
                // gone; reconciliation (warning + rollback if wildly off) runs
                // inside OnConfirmedUpdate, which re-fires for the same tip.
                MergedUpdated?.Invoke(Current);
            }
        }

        private void OnConfirmedUpdate(AvatarSnapshot snapshot)
        {
            _confirmed = snapshot;
            MergedUpdated?.Invoke(Current);
        }

        /// <summary>Spec: "On confirmation: drop the pending entry, adopt the
        /// confirmed snapshot, and if the two disagree beyond a tolerance, log
        /// a reconciliation warning with both values." Runs ONCE per entry at
        /// resolve time — comparing the PRE-GUESS baseline against the now-
        /// confirmed world — never per intermediate tip (a 2s chain would
        /// spam a warning for every block that did not yet include the tx).
        /// This also catches the reorg-erased-confirm case: a tx that
        /// "confirmed" on an orphaned branch leaves the confirmed world
        /// without its expected deltas → warned with both values.</summary>
        private void Reconcile(AvatarSnapshot baseline, PendingMutation guess)
        {
            AvatarSnapshot? confirmed = Confirmed;
            if (confirmed is null)
            {
                return;
            }

            CheckReconciliation("stamina", baseline.Stamina, confirmed.Stamina,
                guess.StaminaDelta, StaminaTolerance);
            CheckReconciliation("gold", baseline.Gold, confirmed.Gold,
                guess.GoldDelta, ExactTolerance);
            CheckReconciliation("fishingExp", baseline.FishingExp, confirmed.FishingExp,
                guess.FishingExpDelta, ExactTolerance);
            CheckReconciliation("cookingExp", baseline.CookingExp, confirmed.CookingExp,
                guess.CookingExpDelta, ExactTolerance);

            foreach (KeyValuePair<int, long> kv in guess.InventoryDelta)
            {
                long before = baseline.GetItemCount(kv.Key);
                long after = confirmed.GetItemCount(kv.Key);
                CheckReconciliation($"item {kv.Key}", before, after, kv.Value, ExactTolerance);
            }

            if (guess.KitchenUnlocked == true && !confirmed.KitchenUnlocked)
            {
                UnityEngine.Debug.LogWarning(
                    "[optimistic] kitchen unlock guess did not confirm — showing confirmed state.");
            }
        }

        private static void CheckReconciliation(
            string field, long before, long confirmedValue, long guessedDelta, long tolerance)
        {
            long actualDelta = confirmedValue - before;
            long diff = Math.Abs(actualDelta - guessedDelta);
            if (diff > tolerance)
            {
                UnityEngine.Debug.LogWarning(
                    $"[optimistic] reconciliation drift on {field}: the guess expected " +
                    $"{before} → {before + guessedDelta} but the confirmed block says " +
                    $"{confirmedValue} (expected Δ{guessedDelta}, got Δ{actualDelta}). " +
                    "Display follows the confirmed truth.");
            }
        }

        /// <summary>Fold every pending mutation onto a confirmed snapshot.
        /// Counts never go negative in the DISPLAY (the chain rejects
        /// over-spends — a negative here would mean the guess is already
        /// known-wrong, still clamped so the UI never shows "-3 bait").</summary>
        private static AvatarSnapshot Merge(
            AvatarSnapshot confirmed, IReadOnlyList<PendingMutation> pending)
        {
            long stamina = confirmed.Stamina;
            long gold = confirmed.Gold;
            long fishingExp = confirmed.FishingExp;
            long cookingExp = confirmed.CookingExp;
            bool kitchen = confirmed.KitchenUnlocked;
            var inventory = new Dictionary<int, long>(confirmed.Inventory);

            foreach (PendingMutation m in pending)
            {
                stamina += m.StaminaDelta;
                gold += m.GoldDelta;
                fishingExp += m.FishingExpDelta;
                cookingExp += m.CookingExpDelta;
                kitchen |= m.KitchenUnlocked == true;
                foreach (KeyValuePair<int, long> kv in m.InventoryDelta)
                {
                    inventory.TryGetValue(kv.Key, out long cur);
                    long next = cur + kv.Value;
                    inventory[kv.Key] = next > 0 ? next : 0;
                }
            }

            return AvatarSnapshot.FromStates(
                confirmed.Name,
                confirmed.BlockIndex,
                Math.Max(0, Math.Min(stamina, confirmed.MaxStamina)),
                confirmed.MaxStamina,
                Math.Max(0, gold),
                confirmed.FishingLevel,
                Math.Max(0, fishingExp),
                confirmed.CookingLevel,
                Math.Max(0, cookingExp),
                kitchen,
                inventory,
                confirmed.Tasks,
                confirmed.TasksLastRerolledAt);
        }

        /// <summary>Lifetime handle for one applied mutation. Dispose without
        /// Confirm() == rollback (failure/cancel path).</summary>
        public sealed class PendingHandle : IDisposable
        {
            private readonly OptimisticState _owner;
            private readonly PendingMutation _mutation;
            private readonly AvatarSnapshot? _baseline;
            private bool _resolved;

            internal PendingHandle(
                OptimisticState owner, PendingMutation mutation, AvatarSnapshot? baseline)
            {
                _owner = owner;
                _mutation = mutation;
                _baseline = baseline;
            }

            /// <summary>Drops the pending entry on success — the confirmed
            /// snapshot takes over the display — and runs the ONCE-per-entry
            /// reconciliation against the pre-guess baseline.</summary>
            public void Confirm()
            {
                if (_resolved)
                {
                    return;
                }

                _resolved = true;
                AvatarSnapshot? baseline = _baseline;
                _owner.Resolve(_mutation, confirm: true, RollbackReason.Reconciled);
                if (baseline is { })
                {
                    _owner.Reconcile(baseline, _mutation);
                }
            }

            /// <summary>Drops the pending entry and rolls the display back
            /// with the given reason (Failed / TimedOut / Cancelled) plus the
            /// raw failure text for ErrorMapper.</summary>
            public void RollBack(RollbackReason reason, string rawReason = "")
            {
                if (_resolved)
                {
                    return;
                }

                _resolved = true;
                _owner.Resolve(_mutation, confirm: false, reason, rawReason);
            }

            public void Dispose() => RollBack(RollbackReason.Cancelled);
        }
    }
}
