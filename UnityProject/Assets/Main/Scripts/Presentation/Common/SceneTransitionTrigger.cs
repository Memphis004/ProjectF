using ProjectF.Infrastructure.Scene;
using UnityEngine;

// ReSharper disable CheckNamespace
namespace ProjectF.Presentation.Common
{
    /// <summary>
    /// Door/exit volume: on player entry routes through <see cref="SceneRouter"/>
    /// (which guarantees the presence switch happens only after the target
    /// scene is live). The Stage 8 scene generator places these with a target
    /// SceneId + spawn offset; the router receives the spawn coordinates.
    /// </summary>
    [RequireComponent(typeof(BoxCollider2D))]
    public sealed class SceneTransitionTrigger : MonoBehaviour
    {
        [SerializeField]
        private SceneId targetScene = SceneId.Village;

        [SerializeField]
        private Vector2 spawnAt = Vector2.zero;

        private SceneRouter router = default!;

        public SceneId TargetScene => targetScene;

        public Vector2 SpawnAt => spawnAt;

        /// <summary>Wired by the scene LifetimeScope (router is a root singleton).</summary>
        public void Configure(SceneRouter sceneRouter) => router = sceneRouter;

        private void Reset()
        {
            GetComponent<BoxCollider2D>().isTrigger = true;
        }

        private async void OnTriggerEnter2D(Collider2D other)
        {
            if (router is null || !other.TryGetComponent<PlayerView>(out _))
            {
                return;
            }

            // Debounce: transition in flight (the trigger fires once per entry,
            // but the player can linger in the volume during the load).
            if (busy)
            {
                return;
            }

            busy = true;
            try
            {
                await router.GoToAsync(targetScene, spawnAt.x, spawnAt.y);
            }
            finally
            {
                busy = false;
            }
        }

        private bool busy;
    }
}
