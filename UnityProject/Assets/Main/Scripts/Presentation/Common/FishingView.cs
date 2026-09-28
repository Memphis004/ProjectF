using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using ProjectF.Infrastructure.Blockchain;
using ProjectF.Lib.Actions;
using UnityEngine;
using UnityEngine.UI;

// ReSharper disable CheckNamespace
namespace ProjectF.Presentation.Common
{
    /// <summary>Fishing VIEW — state flags + reveal panel, zero logic.</summary>
    public sealed class FishingView : MonoBehaviour
    {
        [SerializeField]
        private Text statusLabel = default!;

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

        private CancellationTokenSource? cts;

        public FishingPresenter(ActionQueue actionQueue, StateWatcher stateWatcher, FishingView view)
        {
            actions = actionQueue;
            state = stateWatcher;
            this.view = view;
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

        /// <summary>Full cast: occupy → fish. Each step cancels the next on failure.</summary>
        public async UniTask CastAsync(int pondId, int baitItemId, CancellationToken ct)
        {
            cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            CancellationToken token = cts.Token;

            view.SetCasting(true);
            view.ShowStatus("Finding a slot at the pond…");
            try
            {
                if (!await actions.SubmitAsync(new OccupyPondAction(pondId), token))
                {
                    view.ShowStatus("Could not take a slot at the pond.");
                    return;
                }

                view.ShowStatus("Casting…");
                AvatarSnapshot? before = state.Current;
                long baitBefore = before?.GetItemCount(baitItemId) ?? 0L;

                if (!await actions.SubmitAsync(new FishingAction(pondId, baitItemId), token))
                {
                    view.ShowStatus("The fish got away (action failed).");
                    return;
                }

                // Inventory diff before/after decides the catch animation —
                // NEVER a client-side roll (knowledge.md rule 1).
                AvatarSnapshot? after = state.Current;
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

        public void Cancel()
        {
            cts?.Cancel();
        }
    }
}
