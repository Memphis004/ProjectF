using UnityEngine;
using UnityEngine.UI;

// ReSharper disable CheckNamespace
namespace ProjectF.Infrastructure.UI
{
    /// <summary>One toast row. The VIEW holds references only; ToastService
    /// (plain C#) drives text/visibility/timing. Parented under the Toast
    /// layer by ToastService on spawn.
    ///
    /// Own file because of the MonoScript rule: Unity serializes MonoBehaviour
    /// references by class-name-in-file-name, so a MonoBehaviour living inside
    /// ToastService.cs serializes as a script-less reference in generated
    /// prefabs (found by the stage-9 UI smoke — Toast.prefab shipped with
    /// m_Script: {fileID: 0}).</summary>
    public sealed class ToastView : MonoBehaviour
    {
        [SerializeField]
        private Text messageLabel = default!;

        [SerializeField]
        private Image icon = default!;

        [SerializeField]
        private Image spinner = default!;

        public Text MessageLabel => messageLabel;
        public Image Icon => icon;
        public Image Spinner => spinner;

        /// <summary>Spinner rotate driver — cosmetic, view-local, zero logic
        /// leak (a rotating Image is presentation, not a decision).</summary>
        private void Update()
        {
            if (spinner is { } && spinner.gameObject.activeSelf)
            {
                spinner.rectTransform.Rotate(0f, 0f, -180f * Time.deltaTime);
            }
        }
    }
}
