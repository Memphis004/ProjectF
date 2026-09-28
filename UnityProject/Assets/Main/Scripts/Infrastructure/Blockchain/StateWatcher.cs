using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Bencodex.Types;
using Libplanet.Crypto;
using ProjectF.Lib;
using ProjectF.Lib.States;
using UnityEngine;

// ReSharper disable CheckNamespace
namespace ProjectF.Infrastructure.Blockchain
{
    /// <summary>
    /// Polls the player's avatar/inventory/pond state on new tips and raises
    /// typed events (spec section 7). Stage 11 adds reorg detection here;
    /// Stage 7 ships the polling loop + change detection.
    /// </summary>
    public sealed class StateWatcher : IDisposable
    {
        private const int PollIntervalMs = 500;

        private readonly ILibplanetClient _client;
        private readonly KeyStore _keyStore;
        private CancellationTokenSource? _cts;

        private long _lastTip = -1;
        private AvatarSnapshot? _last;

        /// <summary>Fired on every tip change (index, hex hash).</summary>
        public event Action<long, string>? TipChanged;

        /// <summary>Fired after each poll with the confirmed world view.</summary>
        public event Action<AvatarSnapshot>? AvatarUpdated;

        public AvatarSnapshot? Current => _last;

        public StateWatcher(ILibplanetClient client, KeyStore keyStore)
        {
            _client = client;
            _keyStore = keyStore;
        }

        public void Start()
        {
            if (_cts is { })
            {
                return; // already running
            }

            _cts = new CancellationTokenSource();
            RunLoopAsync(_cts.Token).Forget();
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }

        private async UniTaskVoid RunLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await UniTask.Delay(PollIntervalMs, cancellationToken: ct);
                    PollOnce();
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    // Never let the watch loop die — log and keep polling.
                    Debug.LogWarning($"[state] poll failed: {ex.Message}");
                }
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
            AvatarSnapshot snapshot = ReadSnapshot(tip);

            bool tipMoved = tip != _lastTip;
            bool stateMoved = _last is null
                || snapshot.Stamina != _last.Stamina
                || snapshot.Gold != _last.Gold
                || snapshot.FishingExp != _last.FishingExp
                || snapshot.CookingExp != _last.CookingExp
                || snapshot.KitchenUnlocked != _last.KitchenUnlocked
                || !InventoryEquals(_last.Inventory, snapshot.Inventory);

            _lastTip = tip;
            _last = snapshot;

            if (tipMoved)
            {
                TipChanged?.Invoke(tip, _client.TipHash);
            }

            if (stateMoved || tipMoved)
            {
                AvatarUpdated?.Invoke(snapshot);
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

            return AvatarSnapshot.FromStates(
                name, tip, stamina, maxStamina, gold,
                AvatarState.LevelFromExp(fishingExp), fishingExp,
                AvatarState.LevelFromExp(cookingExp), cookingExp,
                kitchen, inventory);
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
}
