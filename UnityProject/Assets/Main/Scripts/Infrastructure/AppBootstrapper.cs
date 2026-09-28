using System.Threading;
using Cysharp.Threading.Tasks;
using ProjectF.Infrastructure.Blockchain;
using ProjectF.Infrastructure.DataTables;
using ProjectF.Infrastructure.Network;
using ProjectF.Infrastructure.Scene;
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
    /// </summary>
    public sealed class AppBootstrapper : IAsyncStartable
    {
        private readonly UnityTableService _tables;
        private readonly ILibplanetClient _chain;
        private readonly StateWatcher _stateWatcher;
        private readonly IPresenceClient _presence;
        private readonly SceneRouter _sceneRouter;

        public AppBootstrapper(
            UnityTableService tables,
            ILibplanetClient chain,
            StateWatcher stateWatcher,
            IPresenceClient presence,
            SceneRouter sceneRouter)
        {
            _tables = tables;
            _chain = chain;
            _stateWatcher = stateWatcher;
            _presence = presence;
            _sceneRouter = sceneRouter;
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

            // 3. Start watching confirmed state (HUD binding source).
            _stateWatcher.Start();

            // 4. Presence: FIRE-AND-FORGET — a down hub must never block
            //    gameplay. PlayerHubClient degrades to Offline status;
            //    SceneRouter checks IsOnline per hop, so even a late
            //    recovery works. Forget() swallows connectivity errors.
            _presence
                .ConnectAsync("Player", (int)SceneId.Village, 0f, 0f, cancellation)
                .Forget(ex => Debug.LogWarning($"[boot] presence connect failed: {ex}"));

            // 5. Route to the first gameplay scene (additively on top of
            //    Persistent). SceneRouter switches the presence group only
            //    after Village is live — and skips it entirely while offline.
            await _sceneRouter.GoToInitialAsync(SceneId.Village);

            Debug.Log("[boot] Stage 7 client up.");
        }
    }
}
