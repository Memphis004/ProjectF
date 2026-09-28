using ProjectF.Shared.Presence;
using UnityEngine;
// UnityEngine.AnimationState (legacy) collides with our presence enum —
// alias the presence one for this file.
using AnimationState = ProjectF.Shared.Presence.AnimationState;

// ReSharper disable CheckNamespace
namespace ProjectF.Presentation.Common
{
    /// <summary>
    /// Remote player VIEW. Position is interpolated toward a target (presence
    /// is 10 Hz — clients interpolate, knowledge.md style rules); snaps when
    /// the error exceeds 2 tiles. Stage 12 replaces the lerp with a 150 ms
    /// snapshot buffer — the SetTarget/Teleport surface stays.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class RemotePlayerView : MonoBehaviour
    {
        [SerializeField]
        private SpriteRenderer spriteRenderer = default!;

        [SerializeField]
        private NameTagView nameTag = default!;

        [SerializeField]
        private float tileSize = 1f;

        [SerializeField]
        private float lerpSpeed = 12f;

        private Vector2 target;
        private string playerName = string.Empty;

        public string PlayerName => playerName;

        public void Teleport(Vector2 position)
        {
            target = position;
            transform.position = position;
        }

        public void SetTarget(Vector2 position) => target = position;

        public void SetVisuals(string name, Direction facingDir, AnimationState animState)
        {
            playerName = name;

            if (spriteRenderer is { })
            {
                // Placeholder art: horizontal flip for left/right; Stage 12
                // drives the Animator from the received AnimationState.
                spriteRenderer.flipX = facingDir == Direction.Left;
            }

            if (nameTag is { })
            {
                nameTag.SetPlayerName(name);
            }
        }

        private void Update()
        {
            float distance = Vector2.Distance(transform.position, target);
            if (distance > tileSize * 2f)
            {
                // Teleport-grade desync (join, lag spike) — snap, don't glide.
                transform.position = target;
                return;
            }

            transform.position = Vector2.Lerp(
                transform.position, target, Time.deltaTime * lerpSpeed);
        }
    }
}
