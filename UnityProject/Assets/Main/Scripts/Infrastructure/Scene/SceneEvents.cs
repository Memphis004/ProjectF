using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

// ReSharper disable CheckNamespace
namespace ProjectF.Infrastructure.Scene
{
    /// <summary>Optional notifications around <see cref="SceneRouter"/> transitions.
    /// Presenters subscribe to rebuild scene-local state (player spawn, remote
    /// registry wiring) BEFORE the presence group switches.</summary>
    public interface ISceneEvents
    {
        /// <summary>Raised after the additive scene is loaded and active, before
        /// IPresenceClient.ChangeSceneAsync is awaited.</summary>
        void NotifySceneLoaded(SceneId sceneId);
    }

    public sealed class NullSceneEvents : ISceneEvents
    {
        public event Action<SceneId>? SceneLoaded;

        public void NotifySceneLoaded(SceneId sceneId)
        {
        }
    }

    /// <summary>Minimal implementation: subscribe via <see cref="SceneLoaded"/>.</summary>
    public sealed class SceneEvents : ISceneEvents
    {
        public event Action<SceneId>? SceneLoaded;

        public void NotifySceneLoaded(SceneId sceneId) => SceneLoaded?.Invoke(sceneId);
    }
}

// Separate file would be nicer, but VContainer wiring is at the end of this
// chain — keep the spawn-point helper with the scene infrastructure.
namespace ProjectF.Infrastructure.Scene
{
    using UnityEngine.SceneManagement;

    /// <summary>Resolves where the player appears in a freshly loaded scene.
    /// Stage 14's full door-matching resolver replaces this; for now every
    /// scene exposes one "Spawn" GameObject (see UnityProject/SETUP.md).</summary>
    public sealed class SceneSpawnResolver
    {
        public bool TryResolve(SceneId sceneId, out Vector3 position)
        {
            Scene scene = SceneManager.GetSceneByName(sceneId.ToSceneName());
            if (!scene.IsValid() || !scene.isLoaded)
            {
                position = Vector3.zero;
                return false;
            }

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                Transform spawn = root.transform.Find("Spawn");
                if (spawn is { })
                {
                    position = spawn.position;
                    return true;
                }
            }

            position = Vector3.zero;
            return false;
        }
    }
}
