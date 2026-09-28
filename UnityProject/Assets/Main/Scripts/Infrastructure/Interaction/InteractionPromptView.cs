using UnityEngine;
using UnityEngine.UI;

// ReSharper disable CheckNamespace
namespace ProjectF.Infrastructure.Interaction
{
    /// <summary>
    /// Stage 10 prompt VIEW (spec 10.1): one floating "[E] {verb}" label above
    /// the current interactable. A single shared uGUI Text under the World
    /// layer, repositioned by <see cref="InteractionPromptDriver"/> each frame.
    ///
    /// Own file (MonoScript rule): Unity serializes MonoBehaviour references
    /// by class-name-in-file-name — anything the UIRoot prefab must carry
    /// cannot share a file with another MonoBehaviour.
    /// </summary>
    public sealed class InteractionPromptView : MonoBehaviour
    {
        [SerializeField]
        private RectTransform root = default!;

        [SerializeField]
        private Text label = default!;

        public RectTransform Root => root;
        public Text Label => label;

        public void Show(IInteractable target, string ePrefix, string verb)
        {
            if (root is null || label is null)
            {
                return; // generator wiring broken — fail quiet, never spam NREs
            }

            if (!root.gameObject.activeSelf)
            {
                root.gameObject.SetActive(true);
            }

            label.text = $"{ePrefix} {verb}";
            root.position = target.Anchor.position + new Vector3(0f, 0.9f, 0f);
        }

        public void Hide()
        {
            if (root is { } && root.gameObject.activeSelf)
            {
                root.gameObject.SetActive(false);
            }
        }
    }
}
