using UnityEngine;

// ReSharper disable CheckNamespace
namespace ProjectF.Presentation.Common
{
    /// <summary>
    /// Floating name label above a remote player. TextMesh (no uGUI canvas per
    /// player — cheap and pixel-scaled). Stage 12 adds off-screen clamping and
    /// the shortened address line.
    /// </summary>
    public sealed class NameTagView : MonoBehaviour
    {
        [SerializeField]
        private TextMesh label = default!;

        [SerializeField]
        private Transform? follow;

        [SerializeField]
        private Vector3 offset = new(0f, 0.9f, 0f);

        private void Awake()
        {
            // NOTE: NEVER use `is {{ }}` / `is null` on UnityEngine.Object
            // fields — an unassigned serialized reference is a FAKE null (a
            // managed wrapper around a missing object), so pattern matching
            // sees it as non-null and the next use throws
            // UnassignedReferenceException (found via the presence broadcast
            // spam). The overloaded == / != operators handle it correctly.
            if (label == null)
            {
                label = GetComponent<TextMesh>();
            }

            if (follow == null)
            {
                follow = transform.parent;
            }
        }

        private void LateUpdate()
        {
            // Stay upright + above the player regardless of parent flip/scale.
            // (`!= null` — see the Awake comment about Unity's fake nulls.)
            Vector3 basePosition = follow != null ? follow.position : transform.position;
            transform.SetPositionAndRotation(
                basePosition + offset, Quaternion.identity);
        }

        public void SetPlayerName(string playerName)
        {
            if (label != null)
            {
                label.text = playerName;
            }
        }
    }
}
