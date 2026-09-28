using Cysharp.Threading.Tasks;
using ProjectF.Infrastructure.UI;
using ProjectF.Presentation.Common;
using UnityEngine;

// ReSharper disable CheckNamespace
namespace ProjectF.Infrastructure.Interaction
{
    /// <summary>
    /// Stage 10 [E] driver — a MonoBehaviour on the UIRoot prefab (push-wired
    /// by UiBootstrapper exactly like UiInputDriver). Every frame it reads the
    /// player's <see cref="InteractionDetector"/> (nearest interactable wins),
    /// shows/hides the shared prompt, and on E opens the matching window
    /// through IWindowService — gate/blocker semantics then match the I-key
    /// windows exactly.
    ///
    /// Own file (MonoScript rule): the UIRoot prefab serializes this
    /// component, so class name must equal file name — a driver living inside
    /// InteractionPromptView.cs silently failed to serialize and never
    /// reached the scene (found by the E2E interaction test).
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

            // A driver spawned without a serialized view resolves the prefab's
            // prompt (sibling) once here.
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
