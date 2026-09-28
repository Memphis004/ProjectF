using System;
using ProjectF.Infrastructure.Network;
using ProjectF.Infrastructure.Scene;
using ProjectF.Shared.Presence;
using UnityEngine;
// UnityEngine.AnimationState (legacy) collides with our presence enum —
// alias the presence one for this file.
using AnimationState = ProjectF.Shared.Presence.AnimationState;

// ReSharper disable CheckNamespace
namespace ProjectF.Presentation.Common
{
    /// <summary>
    /// Presence outbound path (spec section 7): throttles SendMove to 10 Hz.
    /// The 10 Hz cap is ALSO enforced inside <see cref="PlayerHubClient"/> —
    /// both layers enforce it; this one additionally skips state-unchanged
    /// frames entirely. Attached to the Player prefab; configured by
    /// <see cref="ScenePlayerService"/> on every scene entry.
    /// </summary>
    public sealed class PresenceBroadcaster : MonoBehaviour
    {
        private const float SendInterval = 1f / 10f; // 10 Hz, never per-frame

        private IPresenceClient? presence;
        private SceneId sceneId;
        private float accumulate;
        private Vector3 lastSentPosition;
        private Direction lastFacing = unchecked((Direction)(-1));
        private AnimationState lastAnimation = unchecked((AnimationState)(-1));

        [NonSerialized]
        public PlayerInputController? input;

        /// <summary>Re-configured on every scene entry (scene id changes).</summary>
        public void Configure(IPresenceClient presenceClient, SceneId currentScene)
        {
            presence = presenceClient;
            sceneId = currentScene;
        }

        private void Update()
        {
            if (presence is null || !presence.IsOnline || input is null)
            {
                return;
            }

            Vector3 p = transform.position;
            bool moved = (p - lastSentPosition).sqrMagnitude > 0.0001f;
            bool stateChanged =
                input.Facing != lastFacing || input.Animation != lastAnimation;
            bool positionChanged = moved || stateChanged;

            accumulate += Time.deltaTime;
            if (accumulate < SendInterval || !positionChanged)
            {
                return; // 10 Hz cap; nothing changed → nothing sent
            }

            accumulate = 0f;
            lastSentPosition = p;
            lastFacing = input.Facing;
            lastAnimation = input.Animation;

            presence.SendMove(
                (int)sceneId, p.x, p.y, input.Facing, input.Animation);
        }
    }
}
