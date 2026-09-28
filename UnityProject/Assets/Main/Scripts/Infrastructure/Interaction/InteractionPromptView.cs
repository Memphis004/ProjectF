using Cysharp.Threading.Tasks;
using ProjectF.Infrastructure.UI;
using ProjectF.Presentation.Common;
using UnityEngine;
using UnityEngine.UI;

// ReSharper disable CheckNamespace
namespace ProjectF.Infrastructure.Interaction
{
    /// <summary>
    /// Stage 10 prompt VIEW (spec 10.1): one floating "[E] {verb}" label above
    /// the current interactable. A single shared uGUI Text under the World
    /// layer, repositioned by <see cref="InteractionPromptDriver"/> each frame.
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

    /// <summary>
    /// Stage 10 [E] driver — a MonoBehaviour on the UIRoot (push-wired by
    /// UiBootstrapper exactly like UiInputDriver; constructor injection is
    /// impossible for generator-wired scene components). Every frame it reads
    /// the player's <see cref="InteractionDetector"/> (nearest interactable
    /// wins), shows/hides the shared prompt, and on E opens the matching
    /// window through IWindowService — gate/blocker semantics then match the
    /// I-key windows exactly.
    /// </summary>
    public sealed class InteractionPromptDriver : MonoBehaviour
    {
        [SerializeField]
        private InteractionPromptView view = default!;

        private IWindowService windows = default!;
        private LocalizationService loc = default!;
        private InteractionDetector? detector;

        /// <summary>Push-wired by UiBootstrapper after the UI services resolve.</summary>
        public void Configure(IWindowService windowService, LocalizationService localization)
        {
            windows = windowService;
            loc = localization;

            // A driver spawned by the bootstrapper fallback has no serialized
            // view — resolve the prefab's prompt (sibling) once here.
            view ??= GetComponentInChildren<InteractionPromptView>(true);
        }

        private void Update()
        {
            if (windows is null || loc is null || view is null)
            {
                return; // Configure not called yet / no prompt view on this GO
            }

            detector ??= FindObjectOfType<InteractionDetector>();
            IInteractable? current = detector is { } ? detector.Current : null;

            if (current is null || !current.CanInteract)
            {
                view.Hide();
                return;
            }

            view.Show(current, loc.Get("UI_INTERACT_KEY"), current.Prompt);

            if (Input.GetKeyDown(KeyCode.E) && !windows.IsOpen<ConfirmDialog>())
            {
                OpenAsync(current.Kind).Forget();
            }
        }

        private async UniTaskVoid OpenAsync(InteractionKind kind)
        {
            view.Hide();
            switch (kind)
            {
                case InteractionKind.Shopkeeper:
                    await windows.OpenAsync<Presentation.Shop.ShopWindow,
                        EmptyWindowParam, NoWindowResult>(
                        EmptyWindowParam.Instance, destroyCancellationToken);
                    break;

                case InteractionKind.TaskBoard:
                    await windows.OpenAsync<Presentation.Village.TaskBoardWindow,
                        EmptyWindowParam, NoWindowResult>(
                        EmptyWindowParam.Instance, destroyCancellationToken);
                    break;

                case InteractionKind.Kitchen:
                    await windows.OpenAsync<Presentation.AuntieHouse.CraftWindow,
                        EmptyWindowParam, NoWindowResult>(
                        EmptyWindowParam.Instance, destroyCancellationToken);
                    break;
            }
        }
    }
}
