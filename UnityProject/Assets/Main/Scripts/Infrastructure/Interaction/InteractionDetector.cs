using UnityEngine;

// ReSharper disable CheckNamespace
namespace ProjectF.Infrastructure.Interaction
{
    /// <summary>
    /// Stage 10 (spec 10.1): lives on THE player (Player prefab). Every frame
    /// it overlap-circles a small radius around the player and keeps the
    /// NEAREST enabled IInteractable — nearest wins (spec). The prompt view
    /// and the [E] handler read <see cref="Current"/>.
    ///
    /// Physics2D.OverlapCircleAll is non-alloc via the All variant; the
    /// colliders are triggers (NPC/taskboard/kitchen points carry trigger
    /// colliders added by the generator).
    /// </summary>
    public sealed class InteractionDetector : MonoBehaviour
    {
        [SerializeField]
        private float radius = 1.2f;

        /// <summary>Nearest interactable in range this frame (null when none).</summary>
        public IInteractable? Current { get; private set; }

        private void Update()
        {
            IInteractable? best = null;
            float bestSqr = float.MaxValue;
            Vector2 origin = transform.position;

            // Triggers are included; all stage-10 targets are trigger colliders.
            foreach (Collider2D hit in Physics2D.OverlapCircleAll(origin, radius))
            {
                if (!hit.TryGetComponent(out IInteractable candidate) || !candidate.CanInteract)
                {
                    continue;
                }

                float sqr = ((Vector2)hit.transform.position - origin).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = candidate;
                }
            }

            Current = best;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.4f, 0.9f, 0.5f, 0.5f);
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}
