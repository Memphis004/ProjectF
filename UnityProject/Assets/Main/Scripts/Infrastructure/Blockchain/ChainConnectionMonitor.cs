using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using ProjectF.Infrastructure.UI;
using UnityEngine;

// ReSharper disable CheckNamespace
namespace ProjectF.Infrastructure.Blockchain
{
    /// <summary>
    /// Stage 11 — watches tip movement and drives the HUD chain dot through
    /// five states:
    /// - Bootstrapping: verbatim from the client during node startup.
    /// - Syncing: peers present; while a peer-tip target is known the progress
    ///   "N / M" renders on the HUD sync line (cosmetic — spec 9.4).
    /// - Synced: tip is advancing at the target interval.
    /// - Stalled: NO new tip for 3× the target block interval (2s → 6s grace
    ///   around expected block production). Submissions are BLOCKED with a
    ///   clear message instead of letting actions silently time out.
    /// - Offline: no peers / bootstrap failed (verbatim from the client).
    ///
    /// The monitor NEVER gates reads or legality — only submissions (via
    /// <see cref="CanSubmit"/> which presenters check before enqueueing) and
    /// the HUD dot. Pure polling display: ILibplanetClient exposes no change
    /// event, exactly like ChainSyncPresenter.
    /// </summary>
    public sealed class ChainConnectionMonitor : IDisposable
    {
        private const int PollIntervalMs = 250;

        /// <summary>Stall threshold: 3× the target block interval.</summary>
        private readonly TimeSpan _stallAfter;

        private readonly ILibplanetClient _client;
        private readonly IToastService? _toasts;
        private readonly LocalizationService _loc;
        private CancellationTokenSource? _cts;

        private ChainStatus _reported = (ChainStatus)(-1);
        private long _lastTip;
        private DateTimeOffset _lastTipAt = DateTimeOffset.UtcNow;
        private bool _stalledToastShown;
        private bool _offlineToastShown;

        public ChainConnectionMonitor(
            ILibplanetClient client,
            LocalizationService loc,
            IToastService? toasts = null)
        {
            _client = client;
            _loc = loc;
            _toasts = toasts;
            // Mirror of ProjectF.Lib BlockPolicySource.TargetBlockIntervalMs —
            // the assembly reference goes the wrong way for a constant here,
            // and 2s is the seed node's contract (appsettings.json).
            _stallAfter = TimeSpan.FromMilliseconds(3 * 2_000);
        }

        /// <summary>The CURRENT monitored status (Stalled computed live between
        /// polls) — the HUD dot and CanSubmit read this.</summary>
        public ChainStatus Status { get; private set; } = ChainStatus.Bootstrapping;

        /// <summary>False exactly when Status is Stalled or Offline —
        /// presenters must refuse to enqueue actions in that case (UX
        /// contract: a clear refusal beats a silent 30s timeout).</summary>
        public bool CanSubmit => Status is not (ChainStatus.Stalled or ChainStatus.Offline);

        /// <summary>Fired whenever the reported status changed.</summary>
        public event Action<ChainStatus>? StatusChanged;

        public void Start()
        {
            if (_cts is { })
            {
                return;
            }

            _cts = new CancellationTokenSource();
            LoopAsync(_cts.Token).Forget();
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }

        private async UniTaskVoid LoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await UniTask.Delay(PollIntervalMs, cancellationToken: ct);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                Tick();
            }
        }

        /// <summary>One monitor tick — computes the status and toasts on
        /// degradation. Public so tests can drive it deterministically.</summary>
        public void Tick()
        {
            ChainStatus computed = Compute();
            if (computed != _reported)
            {
                _reported = computed;
                Status = computed;
                StatusChanged?.Invoke(computed);
            }
            else
            {
                Status = computed;
            }

            // One-shot degradation toasts (never spam while the condition holds).
            if (Status == ChainStatus.Stalled && !_stalledToastShown)
            {
                _stalledToastShown = true;
                _toasts?.Warning(_loc.Get("TOAST_CHAIN_STALLED"));
            }
            else if (Status != ChainStatus.Stalled)
            {
                _stalledToastShown = false;
            }

            if (Status == ChainStatus.Offline && !_offlineToastShown)
            {
                _offlineToastShown = true;
                _toasts?.Warning(_loc.Get("TOAST_CHAIN_OFFLINE"));
            }
            else if (Status != ChainStatus.Offline)
            {
                _offlineToastShown = false;
            }
        }

        private ChainStatus Compute()
        {
            switch (_client.Status)
            {
                case ChainStatus.Bootstrapping:
                    return ChainStatus.Bootstrapping;

                case ChainStatus.Offline:
                    return ChainStatus.Offline;
            }

            // Bootstrapped (Syncing or Synced per the client). Watch the tip.
            long tip = _client.TipIndex;
            DateTimeOffset now = DateTimeOffset.UtcNow;
            if (tip != _lastTip)
            {
                _lastTip = tip;
                _lastTipAt = now;
            }

            bool stalled = now - _lastTipAt > _stallAfter;
            if (stalled)
            {
                return ChainStatus.Stalled;
            }

            SyncProgress progress = _client.SyncProgress;
            if (progress.HasTarget)
            {
                // A peer is ahead of us — we are catching up.
                return ChainStatus.Syncing;
            }

            // No known target and the tip advanced within the last target
            // block interval (stallAfter / 3): we are keeping pace.
            if (tip > 0 && now - _lastTipAt <= TimeSpan.FromMilliseconds(2_000))
            {
                return ChainStatus.Synced;
            }

            // Inside the grace window with a quiet tip: mirror the client
            // (Syncing stays yellow until the stall threshold flips us red).
            return _client.Status;
        }

        /// <summary>Standard refusal reason for the presenters' toasts.</summary>
        public string BlockedReason() => _loc.Get(
            Status == ChainStatus.Offline ? "TOAST_CHAIN_OFFLINE" : "TOAST_CHAIN_STALLED");
    }
}
