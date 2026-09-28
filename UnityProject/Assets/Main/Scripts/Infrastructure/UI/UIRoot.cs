using UnityEngine;

// ReSharper disable CheckNamespace
namespace ProjectF.Infrastructure.UI
{
    /// <summary>
    /// Stage 9 UI root (spec 9.1): a Screen Space - Camera canvas (320x180
    /// reference resolution, Pixel Perfect) with five named child canvases —
    /// World / HUD / Window / Modal / Toast — each with its own sorting order.
    /// Lives in the PERSISTENT scene (instantiated by the UIRoot prefab), so
    /// it survives scene switches and gameplay scenes never build canvases
    /// of their own (Stage 8 lesson: scene files cannot cross-reference).
    ///
    /// Zero logic beyond layer lookup; services (WindowService /
    /// ToastService / LoadingOverlay) parent their pooled instances under
    /// <see cref="GetLayer"/> results.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UIRoot : MonoBehaviour
    {
        [SerializeField]
        private RectTransform worldLayer = default!;

        [SerializeField]
        private RectTransform hudLayer = default!;

        [SerializeField]
        private RectTransform windowLayer = default!;

        [SerializeField]
        private RectTransform modalLayer = default!;

        [SerializeField]
        private RectTransform toastLayer = default!;

        [SerializeField]
        private Camera uiCamera = default!;

        /// <summary>The canvas the pixel-perfect rendering is driven from.</summary>
        public Canvas RootCanvas { get; private set; } = default!;

        public RectTransform WorldLayer => worldLayer;
        public RectTransform HudLayer => hudLayer;
        public RectTransform WindowLayer => windowLayer;
        public RectTransform ModalLayer => modalLayer;
        public RectTransform ToastLayer => toastLayer;

        public Camera UiCamera => uiCamera;

        private void Awake()
        {
            RootCanvas = GetComponent<Canvas>();
        }

        /// <summary>Layers container for the named layer (never null — a
        /// mis-wired prefab fails loudly here rather than silently swallowing
        /// windows into the void).</summary>
        public Transform GetLayer(UILayer layer) => layer switch
        {
            UILayer.World => worldLayer is { } ? worldLayer
                : throw new System.InvalidOperationException(
                    "UIRoot prefab: World layer is not assigned."),
            UILayer.Hud => hudLayer is { } ? hudLayer
                : throw new System.InvalidOperationException(
                    "UIRoot prefab: Hud layer is not assigned."),
            UILayer.Window => windowLayer is { } ? windowLayer
                : throw new System.InvalidOperationException(
                    "UIRoot prefab: Window layer is not assigned."),
            UILayer.Modal => modalLayer is { } ? modalLayer
                : throw new System.InvalidOperationException(
                    "UIRoot prefab: Modal layer is not assigned."),
            UILayer.Toast => toastLayer is { } ? toastLayer
                : throw new System.InvalidOperationException(
                    "UIRoot prefab: Toast layer is not assigned."),
            _ => throw new System.ArgumentOutOfRangeException(
                nameof(layer), layer, "Unknown UILayer."),
        };
    }
}
