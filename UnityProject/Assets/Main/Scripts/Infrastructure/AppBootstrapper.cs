using System.Threading;
using Cysharp.Threading.Tasks;
using ProjectF.Infrastructure.Blockchain;
using ProjectF.Infrastructure.DataTables;
using ProjectF.Infrastructure.Network;
using ProjectF.Infrastructure.Scene;
using ProjectF.Infrastructure.UI;
using UnityEngine;
using VContainer.Unity;

// ReSharper disable CheckNamespace
namespace ProjectF.Infrastructure
{
    /// <summary>
    /// Startup sequence (spec section 7), in order:
    /// load tables → load/create key → start embedded node & sync → connect
    /// presence (fire-and-forget, MUST NOT block gameplay if the hub is down)
    /// → route to the Village scene.
    /// Stage 11: also starts the connection monitor (Stalled detection + HUD
    /// dot) and routes OptimisticState rollbacks into the toast queue
    /// ("ยกเลิกรายการ: <reason>").
    /// </summary>
    public sealed class AppBootstrapper : IAsyncStartable
    {
        private readonly UnityTableService _tables;
        private readonly ILibplanetClient _chain;
        private readonly StateWatcher _stateWatcher;
        private readonly OptimisticState _optimistic;
        private readonly ChainConnectionMonitor _connectionMonitor;
        private readonly IToastService _toasts;
        private readonly IPresenceClient _presence;
        private readonly SceneRouter _sceneRouter;
        private readonly NetworkSettings _settings;

        public AppBootstrapper(
            UnityTableService tables,
            ILibplanetClient chain,
            StateWatcher stateWatcher,
            OptimisticState optimistic,
            ChainConnectionMonitor connectionMonitor,
            IToastService toasts,
            IPresenceClient presence,
            SceneRouter sceneRouter,
            NetworkSettings settings)
        {
            _tables = tables;
            _chain = chain;
            _stateWatcher = stateWatcher;
            _optimistic = optimistic;
            _connectionMonitor = connectionMonitor;
            _toasts = toasts;
            _presence = presence;
            _sceneRouter = sceneRouter;
            _settings = settings;
        }

        public async UniTask StartAsync(CancellationToken cancellation)
        {
            // 1. Tables (client-side presentation data; chain tables are
            //    embedded in ProjectF.Lib).
            await _tables.LoadAsync();

            // 2. Chain bootstrap (key load happens inside). No seed reachable
            //    → offline read-only mode, never an exception.
            ChainStatus status = await _chain.BootstrapAsync(cancellation);
            Debug.Log($"[boot] chain status: {status} (tip #{_chain.TipIndex})");

            // 3. Start watching confirmed state (HUD binding source) + the
            //    Stage 11 display overlay + connection monitor.
            _stateWatcher.Start();
            _connectionMonitor.Start();
            _optimistic.OnRolledBack += OnOptimisticRolledBack;

            // 4. Presence: FIRE-AND-FORGET — a down hub must never block
            //    gameplay. PlayerHubClient degrades to Offline status;
            //    SceneRouter checks IsOnline per hop, so even a late
            //    recovery works. Forget() swallows connectivity errors.
            // Stage 16: the configured display name (possibly --player-name
            // overridden) instead of the old hardcoded "Player" — two
            // instances must be distinguishable on the roster.
            _presence
                .ConnectAsync(_settings.PlayerName, (int)SceneId.Village, 0f, 0f, cancellation)
                .Forget(ex => Debug.LogWarning($"[boot] presence connect failed: {ex}"));

            // 5. Route to the first gameplay scene (additively on top of
            //    Persistent). SceneRouter switches the presence group only
            //    after Village is live — and skips it entirely while offline.
            await _sceneRouter.GoToInitialAsync(SceneId.Village);

            Debug.Log("[boot] Stage 7 client up (Stage 11 UX chain online).");
        }

        /// <summary>UX contract (spec stage 11): a rolled-back optimistic
        /// mutation toasts "ยกเลิกรายการ: <reason>" — the display restored
        /// itself when the pending entry was dropped. Reconciled rollbacks
        /// (drift beyond tolerance) log only — the confirmed numbers stood.</summary>
        private void OnOptimisticRolledBack(Rollback rollback)
        {
            if (rollback.Reason == RollbackReason.Reconciled)
            {
                return; // silent: chain truth won, nothing was cancelled.
            }

            // Prefer the mapped on-chain validation reason (e.g. "not enough
            // gold") over the generic failed/cancelled/timeout line.
            string mapped = ErrorMapper.Classify(rollback.RawReason) is { } key
                ? ErrorMapper.Localize(rollback.RawReason)
                : rollback.Reason switch
                {
                    RollbackReason.TimedOut => ErrorMapper.Localize("timeout"),
                    RollbackReason.Cancelled => ErrorMapper.Localize("cancelled"),
                    _ => ErrorMapper.Localize("failed"),
                };

            _toasts.Warning(
                RootLocalization.Get("ROLLBACK_TOAST").Replace("{0}", mapped));
        }
    }
}
