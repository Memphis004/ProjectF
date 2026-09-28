using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using ProjectF.Infrastructure.Blockchain;
using ProjectF.Infrastructure.UI;
using UnityEngine;

// ReSharper disable CheckNamespace
namespace ProjectF.Presentation.Common
{
    /// <summary>
    /// Drives the <see cref="LoadingOverlay"/> during the INITIAL chain sync
    /// (spec 9.4): shows the overlay while Bootstrapping/Syncing with a
    /// "syncing block N" line, hides it on Synced/Offline.
    ///
    /// Polls (250ms, UniTask) instead of subscribing: ILibplanetClient.Status
    /// is a plain property on the embedded-node wrapper (no change event in
    /// the Stage 7 surface), and StateWatcher.TipChanged only fires after
    /// bootstrap — the overlay must ALSO cover the pre-tip phase.
    ///
    /// The progress line stays honest: Libplanet 5.5.3 exposes no target
    /// height (BlockSyncState.CurrentPhase only — reflection-verified), so the
    /// "N / M" shape renders only once a total is known (Stage 11's
    /// ChainConnectionMonitor supplies the peer-tip estimate;
    /// LoadingPresenter.SetProgress already accepts both shapes).
    /// </summary>
    public sealed class ChainSyncPresenter : IDisposable
    {
        private readonly ILibplanetClient client;
        private readonly LoadingPresenter loading;
        private readonly LocalizationService loc;
        private CancellationTokenSource? cts;

        public ChainSyncPresenter(
            ILibplanetClient client,
            LoadingPresenter loading,
            LocalizationService loc)
        {
            this.client = client;
            this.loading = loading;
            this.loc = loc;
        }

        /// <summary>Starts the status poll loop; renders the CURRENT status
        /// immediately (boot may already be past Bootstrapping when this
        /// binds). Call once from the UI bootstrap entry point.</summary>
        public void Start()
        {
            if (cts is { })
            {
                return; // already running
            }

            cts = new CancellationTokenSource();
            LoopAsync(cts.Token).Forget();
        }

        public void Dispose()
        {
            cts?.Cancel();
            cts?.Dispose();
            cts = null;
        }

        private async UniTaskVoid LoopAsync(CancellationToken ct)
        {
            ChainStatus last = (ChainStatus)(-1);
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await UniTask.Delay(250, cancellationToken: ct);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                ChainStatus status = client.Status;
                if (status == last)
                {
                    // Still syncing? Keep the block counter moving.
                    if (status is ChainStatus.Bootstrapping or ChainStatus.Syncing &&
                        client.TipIndex > 0)
                    {
                        loading.SetProgress(client.TipIndex, null);
                    }

                    continue;
                }

                last = status;
                RenderStatus(status);
            }
        }

        private void RenderStatus(ChainStatus status)
        {
            switch (status)
            {
                case ChainStatus.Bootstrapping:
                case ChainStatus.Syncing:
                    if (!loading.IsVisible)
                    {
                        loading.Show(loc.Get("UI_LOADING"));
                    }

                    if (client.TipIndex > 0)
                    {
                        loading.SetProgress(client.TipIndex, null);
                    }

                    break;

                case ChainStatus.Synced:
                case ChainStatus.Offline:
                    loading.Hide();
                    break;
            }
        }
    }
}
