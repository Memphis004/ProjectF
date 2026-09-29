using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using ProjectF.Infrastructure;
using ProjectF.Infrastructure.Blockchain;
using ProjectF.Infrastructure.UI;
using ProjectF.Lib.Actions;
using UnityEngine;
using TMPro;
using TMPro;


// ReSharper disable CheckNamespace
namespace ProjectF.Presentation.Common
{
    /// <summary>Fishing VIEW — state flags + reveal panel, zero logic.</summary>
    public sealed class FishingView : MonoBehaviour
    {
        [SerializeField]
        private TMP_Text statusLabel = default!;

        [SerializeField]
        private GameObject castButton = default!;

        [SerializeField]
        private GameObject revealPanel = default!;

        public void ShowStatus(string message)
        {
            if (statusLabel is { })
            {
                statusLabel.text = message;
            }
        }

        public void SetCasting(bool casting)
        {
            if (castButton is { })
            {
                castButton.SetActive(!casting);
            }
        }

        public void ShowReveal(bool visible)
        {
            if (revealPanel is { })
            {
                revealPanel.SetActive(visible);
            }
        }
    }

    /// <summary>
    /// Fishing PRESENTER — orchestrates the spec section 7 loop:
    /// <see cref="OccupyPondAction"/> → (client mini-game — cosmetic only,
    /// Stage 13) → <see cref="FishingAction"/> → diff inventory before/after
    /// to decide which catch animation to play.
    /// On-chain rules (hit chance, fish roll, stamina) live in ProjectF.Lib —
    /// the client NEVER rolls game outcomes.
    /// </summary>
    public sealed class FishingPresenter : IDisposable
    {
        private readonly ActionQueue actions;
        private readonly StateWatcher state;
        private readonly FishingView view;
        private readonly OptimisticState optimistic;
        private readonly ChainConnectionMonitor connection;
        private readonly IToastService toasts;

        private CancellationTokenSource? cts;

        public FishingPresenter(
            ActionQueue actionQueue,
            StateWatcher stateWatcher,
            FishingView view,
            OptimisticState optimistic,
            ChainConnectionMonitor connection,
            IToastService toasts)
        {
            actions = actionQueue;
            state = stateWatcher;
            this.view = view;
            this.optimistic = optimistic;
            this.connection = connection;
            this.toasts = toasts;
        }

        public void Bind()
        {
            state.AvatarUpdated += OnStateChanged;
        }

        public void Dispose()
        {
            state.AvatarUpdated -= OnStateChanged;
            cts?.Cancel();
            cts?.Dispose();
            cts = null;
        }

        private void OnStateChanged(AvatarSnapshot snapshot)
        {
            // Occupancy feedback is cosmetic-only; the chain owns the truth
            // (slot ownership + expiry). A free-looking pond can still throw
            // PondFullException — the failed SubmitAsync surfaces it (Stage 10
            // ErrorMapper → toast).
            view.ShowStatus($"Inventory items: {snapshot.Inventory.Count}");
        }

        /// <summary>Full cast: occupy → fish. Each step cancels the next on
        /// failure. Stage 11: every step moves the HUD numbers the instant it
        /// is submitted (optimistic guess), rolls back + toasts on failure.</summary>
        public async UniTask CastAsync(int pondId, int baitItemId, CancellationToken ct)
        {
            // Stage 11 gate: never enqueue into a Stalled/Offline chain — a
            // clear refusal beats a silent 30s timeout.
            if (!connection.CanSubmit)
            {
                view.ShowStatus(connection.BlockedReason());
                toasts.Warning(connection.BlockedReason());
                return;
            }

            cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            CancellationToken token = cts.Token;

            view.SetCasting(true);
            view.ShowStatus("Finding a slot at the pond…");
            try
            {
                // Occupy: no resource cost on-chain (renewing a lease) — no
                // optimistic guess beyond the pending-action badge.
                (bool occupied, string occupyReason) = await actions.SubmitWithGuessAsync(
                    new OccupyPondAction(pondId), guess: null, token);
                if (!occupied)
                {
                    view.ShowStatus("Could not take a slot at the pond.");
                    toasts.Error(ErrorMapper.Localize(occupyReason));
                    return;
                }

                view.ShowStatus("Casting…");

                // Fish: the optimistic guess moves bait −1 / stamina −(rod
                // discount at the fishing level) the moment we click. The
                // catch itself (item + exp) is the CHAIN's roll — it appears
                // when the block confirms.
                (bool caught, string fishReason) = await actions.SubmitWithGuessAsync(
                    new FishingAction(pondId, baitItemId),
                    guess: guess => guess.Item(baitItemId, -1).Stamina(-FishingStaminaCost()),
                    token);

                if (!caught)
                {
                    // SubmitWithGuessAsync already rolled the guess back and
                    // raised OptimisticState.OnRolledBack → the "ยกเลิกรายการ"
                    // toast. Here we only set the status line.
                    view.ShowStatus("The fish got away (action failed).");
                    toasts.Error(ErrorMapper.Localize(fishReason));
                    return;
                }

                // Inventory diff before/after decides the catch animation —
                // NEVER a client-side roll (knowledge.md rule 1).
                AvatarSnapshot? before = state.Current;
                long baitBefore = before?.GetItemCount(baitItemId) ?? 0L;
                AvatarSnapshot? after = optimistic.Current;
                long baitAfter = after?.GetItemCount(baitItemId) ?? baitBefore;
                bool consumedBait = baitAfter < baitBefore;
                view.ShowStatus(consumedBait ? "Caught something!" : "It slipped the hook…");
                view.ShowReveal(true);
            }
            finally
            {
                view.SetCasting(false);
            }
        }

        /// <summary>Display-only mirror of the chain's fishing stamina cost
        /// max(1, 5 − rod.StaminaDiscount): 5 − level*0.2 floor… kept honest
        /// by the reconciliation warning — the chain remains the authority.</summary>
        private long FishingStaminaCost()
        {
            // Phase-1 rod table has no client copy here; 5 is the undiscounted
            // cost. Drift beyond tolerance is logged by OptimisticState.
            return 5;
        }

        public void Cancel()
        {
            cts?.Cancel();
        }
    }
}
