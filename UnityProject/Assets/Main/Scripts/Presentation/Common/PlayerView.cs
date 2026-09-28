using ProjectF.Shared.Presence;
using UnityEngine;
// UnityEngine.AnimationState (legacy) collides with our presence enum —
// alias the presence one for this file.
using AnimationState = ProjectF.Shared.Presence.AnimationState;

// ReSharper disable CheckNamespace
namespace ProjectF.Presentation.Common
{
    /// <summary>
    /// The local player VIEW (MVP: no logic). State is written by
    /// <see cref="PlayerInputController"/> (movement) and read by
    /// <see cref="PresenceBroadcaster"/>; presenters only call the explicit
    /// output methods.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(BoxCollider2D))]
    [RequireComponent(typeof(Animator))]
    public sealed class PlayerView : MonoBehaviour
    {
        [SerializeField]
        private Animator animator = default!;

        [SerializeField]
        private SpriteRenderer spriteRenderer = default!;

        private static readonly int AnimStateId = Animator.StringToHash("State");
        private static readonly int AnimFacingId = Animator.StringToHash("Facing");

        public Vector2 Position => transform.position;

        public void Teleport(Vector3 position)
        {
            transform.position = position;
            if (TryGetComponent(out Rigidbody2D body))
            {
                // Zero residual velocity so teleports (scene spawns) are exact.
                // Unity 2022.3 API (linearVelocity arrived in Unity 6).
                body.velocity = Vector2.zero;
            }
        }

        public void SetFacing(Direction facing)
        {
            // Placeholder art: facing = horizontal flip + animator param.
            // Real pixel-art sheets (Stage 8 generator) swap sprites by
            // direction without code changes.
            if (spriteRenderer is { })
            {
                spriteRenderer.flipX = facing == Direction.Left;
            }

            if (animator is { })
            {
                animator.SetInteger(AnimFacingId, (int)facing);
            }
        }

        public void SetAnimation(AnimationState state)
        {
            if (animator is { })
            {
                animator.SetInteger(AnimStateId, (int)state);
            }
        }
    }
}
