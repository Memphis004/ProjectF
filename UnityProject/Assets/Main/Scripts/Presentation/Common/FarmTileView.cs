using System;
using Cysharp.Threading.Tasks;
using ProjectF.Infrastructure.Blockchain;
using ProjectF.Lib.Actions;
using UnityEngine;

// ReSharper disable CheckNamespace
namespace ProjectF.Presentation.Common
{
    /// <summary>One farm plot VIEW (plotIndex + placeholder state tint). The
    /// Stage 8 generator swaps sprites in; the surface stays.</summary>
    public sealed class FarmTileView : MonoBehaviour
    {
        [SerializeField]
        private SpriteRenderer spriteRenderer = default!;

        [SerializeField]
        private int plotIndex;

        public int PlotIndex => plotIndex;

        public void SetPlotIndex(int index) => plotIndex = index;

        public void SetState(FarmTileState state)
        {
            if (spriteRenderer is null)
            {
                return;
            }

            // Placeholder colors: empty=brown, planted=dark, growing=green, ready=gold.
            spriteRenderer.color = state switch
            {
                FarmTileState.Empty => new Color(0.55f, 0.40f, 0.28f),
                FarmTileState.Planted => new Color(0.35f, 0.28f, 0.18f),
                FarmTileState.Growing => new Color(0.30f, 0.60f, 0.25f),
                FarmTileState.Ready => new Color(0.95f, 0.80f, 0.20f),
                _ => Color.magenta,
            };
        }
    }

    public enum FarmTileState
    {
        Empty = 0,
        Planted = 1,
        Growing = 2,
        Ready = 3,
    }

    /// <summary>Farm PRESENTER — reads FarmPlotState from the chain (via the
    /// client's state reads) and submits plant/harvest actions. Growth is
    /// block-index based (knowledge.md rule 3): the view shows "ready" when
    /// the current tip passed PlantedAt + GrowBlocks.</summary>
    public sealed class FarmPresenter : IDisposable
    {
        private readonly ActionQueue actions;
        private readonly FarmTileView[] tiles;
        private System.Threading.CancellationTokenSource? cts;

        public FarmPresenter(ActionQueue actionQueue, FarmTileView[] tiles)
        {
            actions = actionQueue;
            this.tiles = tiles;
        }

        public void Bind() { /* Stage 8: subscribe to plot state reads. */ }

        public void Dispose() => cts?.Cancel();

        public async UniTask PlantAsync(int plotIndex, int seedItemId,
            System.Threading.CancellationToken ct)
        {
            await actions.SubmitAsync(new PlantSeedAction(plotIndex, seedItemId), ct);
        }

        public async UniTask HarvestAsync(int plotIndex,
            System.Threading.CancellationToken ct)
        {
            await actions.SubmitAsync(new HarvestAction(plotIndex), ct);
        }
    }
}
