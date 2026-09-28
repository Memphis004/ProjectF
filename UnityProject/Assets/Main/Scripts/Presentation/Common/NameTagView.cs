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
            if (label is null)
            {
                label = GetComponent<TextMesh>();
            }

            if (follow is null)
            {
                follow = transform.parent;
            }
        }

        private void LateUpdate()
        {
            // Stay upright + above the player regardless of parent flip/scale.
            Vector3 basePosition = follow is { } ? follow.position : transform.position;
            transform.SetPositionAndRotation(
                basePosition + offset, Quaternion.identity);
        }

        public void SetPlayerName(string playerName)
        {
            if (label is { })
            {
                label.text = playerName;
            }
        }
    }
}
