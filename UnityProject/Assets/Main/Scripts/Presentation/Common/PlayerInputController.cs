using ProjectF.Shared.Presence;
using UnityEngine;
// UnityEngine.AnimationState (legacy) collides with our presence enum —
// alias the presence one for this file.
using AnimationState = ProjectF.Shared.Presence.AnimationState;

// ReSharper disable CheckNamespace
namespace ProjectF.Presentation.Common
{
    /// <summary>
    /// 8-way local movement + facing/animation state machine (spec section 7).
    /// Tile-aligned collision and grid snapping land in Stage 12; Stage 7
    /// keeps free 2D movement so scene routing is testable.
    /// </summary>
    [RequireComponent(typeof(PlayerView))]
    public sealed class PlayerInputController : MonoBehaviour
    {
        [SerializeField]
        private float moveSpeed = 4f;

        private PlayerView view = default!;
        private Rigidbody2D body = default!;

        public Direction Facing { get; private set; } = Direction.Down;

        public AnimationState Animation { get; private set; } = AnimationState.Idle;

        /// <summary>Set false while a modal window owns input (Stage 9).</summary>
        public bool InputEnabled { get; set; } = true;

        private void Awake()
        {
            view = GetComponent<PlayerView>();
            body = GetComponent<Rigidbody2D>();

            // Let the PresenceBroadcaster (same GameObject) read our state
            // without a hard reference cycle in the scene setup.
            if (TryGetComponent(out PresenceBroadcaster broadcaster))
            {
                broadcaster.input = this;
            }
        }

        private void Update()
        {
            if (!InputEnabled)
            {
                return;
            }

            float x = Input.GetAxisRaw("Horizontal");
            float y = Input.GetAxisRaw("Vertical");
            Vector2 move = new Vector2(x, y);
            bool moving = move.sqrMagnitude > 0.01f;

            if (moving)
            {
                move.Normalize();
                Facing = FacingFromVector(move);
            }

            Animation = moving ? AnimationState.Walk : AnimationState.Idle;
        }

        private void FixedUpdate()
        {
            if (!InputEnabled)
            {
                body.velocity = Vector2.zero;
                return;
            }

            float x = Input.GetAxisRaw("Horizontal");
            float y = Input.GetAxisRaw("Vertical");
            var move = new Vector2(x, y);
            if (move.sqrMagnitude > 1f)
            {
                move.Normalize();
            }

            // Unity 2022.3 API (linearVelocity arrived in Unity 6).
            body.velocity = move * moveSpeed;
        }

        private void LateUpdate()
        {
            // Single write point for the view + presence per frame.
            view.SetFacing(Facing);
            view.SetAnimation(Animation);
        }

        private static Direction FacingFromVector(Vector2 move)
        {
            if (Mathf.Abs(move.x) >= Mathf.Abs(move.y))
            {
                return move.x >= 0f ? Direction.Right : Direction.Left;
            }

            return move.y >= 0f ? Direction.Up : Direction.Down;
        }
    }
}
