using Cysharp.Threading.Tasks;
using ProjectF.Infrastructure.Network;
using UnityEngine;
using UnityEngine.SceneManagement;

// ReSharper disable CheckNamespace
namespace ProjectF.Infrastructure.Scene
{
    /// <summary>
    /// Drives BOTH layers on scene change — the Unity scene first, the
    /// presence group second. Exact order (Stage 7 spec):
    /// <list type="number">
    /// <item><see cref="SceneManager.LoadSceneAsync(string, LoadSceneMode)"/> ADDITIVE —
    /// the new scene becomes live while the old one still exists.</item>
    /// <item><see cref="SceneManager.UnloadSceneAsync(string)"/> of the PREVIOUS scene —
    /// only after the new one is loaded, so Persistent (index 0) is never the
    /// only scene alive.</item>
    /// <item><see cref="SceneManager.SetActiveScene(Scene)"/> — the new scene wins
    /// the "which scene do new GameObjects / lighting belong to" vote.</item>
    /// <item><see cref="IPresenceClient.ChangeSceneAsync"/> — the presence group
    /// changes only after the Unity scene is live, otherwise remote players
    /// would spawn into a scene whose <see cref="RemotePlayerRegistry"/> has not
    /// been rebuilt yet.</item>
    /// </list>
    /// If the presence client is offline, GoToAsync still completes normally —
    /// local scene loading NEVER depends on the hub (UX contract: offline hub =
    /// single-player visual mode).
    /// </summary>
    public sealed class SceneRouter
    {
        private readonly IPresenceClient _presenceClient;
        private readonly ISceneEvents _sceneEvents;

        /// <summary>SceneId of the currently live additive scene; 0 before the first GoToAsync.</summary>
        public SceneId Current { get; private set; }

        public SceneRouter(IPresenceClient presenceClient, ISceneEvents sceneEvents)
        {
            _presenceClient = presenceClient;
            _sceneEvents = sceneEvents;
        }

        public SceneRouter(IPresenceClient presenceClient)
            : this(presenceClient, new NullSceneEvents())
        {
        }

        public bool IsLoaded(SceneId sceneId) =>
            SceneManager.GetSceneByName(sceneId.ToSceneName()).isLoaded;

        /// <summary>Additively loads the target gameplay scene, unloads the previous
        /// gameplay scene only after the load succeeds, activates the new scene, and
        /// THEN informs the presence layer. Never throws on hub failure.</summary>
        public async UniTask GoToAsync(SceneId target, float spawnX = 0f, float spawnY = 0f)
        {
            string sceneName = target.ToSceneName();

            // Idempotence guard: the FarmPlot exit trigger and the pond-side
            // trigger back both target Village; rapid re-entry must not
            // double-load (or unload the scene we just loaded).
            if (Current == target && IsLoaded(target))
            {
                return;
            }

            SceneId previous = Current;

            // 1. ADDITIVE load first — the previous scene stays alive so the
            //    screen never shows the Persistent-only void.
            await SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);

            // 2. Unload the PREVIOUS gameplay scene — only after the new one is
            //    loaded. Persistent is never touched and never left alone.
            if (previous != 0 && previous != target)
            {
                AsyncOperation unload = SceneManager.UnloadSceneAsync(previous.ToSceneName());
                if (unload is { })
                {
                    await unload;
                }
            }

            // 3. Make the new scene active before anything else reads
            //    SceneManager.activeScene (spawn resolution, lighting).
            SceneManager.SetActiveScene(SceneManager.GetSceneByName(sceneName));

            Current = target;

            // New scene is LIVE — now let its scope hook the player into its
            // fresh RemotePlayerRegistry (fires before the presence switch).
            _sceneEvents.NotifySceneLoaded(target);

            // 4. LAST: switch the presence group. The old scene's registry was
            //    destroyed with its scene; the new one is rebuilt and listening
            //    before any remote snapshot can arrive. IsOnline is false
            //    whenever the hub is down — the hop is skipped entirely.
            if (_presenceClient.IsOnline)
            {
                await _presenceClient.ChangeSceneAsync((int)target, spawnX, spawnY);
            }
        }

        /// <summary>First-run entry point: boot lands on Village with no previous
        /// scene to unload. Same ordering guarantees as GoToAsync.</summary>
        public async UniTask GoToInitialAsync(SceneId target, float spawnX = 0f, float spawnY = 0f)
        {
            if (Current == target && IsLoaded(target))
            {
                return;
            }

            string sceneName = target.ToSceneName();

            await SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
            SceneManager.SetActiveScene(SceneManager.GetSceneByName(sceneName));

            Current = target;
            _sceneEvents.NotifySceneLoaded(target);

            if (_presenceClient.IsOnline)
            {
                await _presenceClient.ChangeSceneAsync((int)target, spawnX, spawnY);
            }
        }
    }
}
