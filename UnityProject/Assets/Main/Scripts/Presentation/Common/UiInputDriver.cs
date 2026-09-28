using Cysharp.Threading.Tasks;
using ProjectF.Infrastructure.UI;
using UnityEngine;

// ReSharper disable CheckNamespace
namespace ProjectF.Presentation.Common
{
    /// <summary>
    /// Stage 9 UI input driver — a MonoBehaviour placed on the UIRoot
    /// GameObject (Persistent scene, never unloaded) that owns the keyboard
    /// UI affordances:
    /// - <c>Escape</c> → IWindowService.CloseTopAsync (spec 9.2).
    /// - <c>I</c> → toggle the InventoryWindow (spec 9.4).
    /// - Click-outside: while a regular window is open (no modal above it),
    ///   the first click anywhere closes the top window.
    /// - PlayerInputGate levels (WindowOpen / ModalOpen) are owned ENTIRELY by
    ///   WindowService — this driver does NOT mirror the window stack (a
    ///   0→1 transition can be a MODAL, and mirroring would push WindowOpen
    ///   on top of ModalOpen; found by the stage-9 UI smoke).
    /// </summary>
    [RequireComponent(typeof(UIRoot))]
    public sealed class UiInputDriver : MonoBehaviour
    {
        private IWindowService windows = default!;
        private PlayerInputGate inputGate = default!;

        /// <summary>Called by UiBootstrapper after the UI services resolve
        /// (constructor injection is impossible for scene components wired by
        /// the generator — this is the one deliberate push-wiring, matching
        /// how the per-scene HudView gets its data pushed).</summary>
        public void Configure(
            IWindowService windowService,
            PlayerInputGate playerInputGate)
        {
            windows = windowService;
            inputGate = playerInputGate;
        }

        private void Update()
        {
            if (windows is null)
            {
                return; // Configure not called yet (first frames / no DI)
            }

            // 1. Escape closes the top window (spec 9.2). WindowService owns
            //    the PlayerInputGate levels (WindowOpen / ModalOpen) — see its
            //    class doc; mirroring here would push WindowOpen ABOVE a
            //    modal that opened first.
            if (Input.GetKeyDown(KeyCode.Escape) && windows.Count > 0)
            {
                windows.CloseTopAsync().Forget();
                return;
            }

            // 2. I toggles the inventory (spec 9.4). Suppressed while a MODAL
            //    owns input — Escape is the way out of modals.
            if (Input.GetKeyDown(KeyCode.I) && inputGate.Current != WorldInputState.ModalOpen)
            {
                ToggleInventory();
                return;
            }

            // 3. Click-outside closes a regular window (the same click must
            //    not also fall through to the world — the gate stays "Window"
            //    for this frame, and PlayerInputController only READS it).
            if (inputGate.WindowDismissOnClick && Input.GetMouseButtonDown(0))
            {
                windows.CloseTopAsync().Forget();
            }
        }

        private void ToggleInventory()
        {
            if (windows.IsOpen<InventoryWindow>())
            {
                windows.CloseAsync<InventoryWindow>().Forget();
            }
            else
            {
                windows.OpenAsync<InventoryWindow, EmptyWindowParam, NoWindowResult>(
                    EmptyWindowParam.Instance, destroyCancellationToken).Forget();
            }
        }
    }
}
