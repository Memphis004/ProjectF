using System.Threading;
using Cysharp.Threading.Tasks;
using ProjectF.Infrastructure.UI;
using UnityEngine;
using UnityEngine.UI;

// ReSharper disable CheckNamespace
namespace ProjectF.Presentation.Common
{
    /// <summary>Open parameter for <see cref="ConfirmDialog"/>.</summary>
    public sealed class ConfirmDialogParam
    {
        public string Title = string.Empty;
        public string Body = string.Empty;

        public ConfirmDialogParam(string title, string body)
        {
            Title = title;
            Body = body;
        }
    }

    /// <summary>
    /// Modal confirm dialog (spec 9.4): title, body, confirm/cancel; the
    /// OpenAsync awaiter receives a bool. Modal → WindowService raises the
    /// raycast blocker + stops world input for as long as it is open.
    /// Zero logic: buttons call straight into the typed UIWindow surface.
    /// </summary>
    public sealed class ConfirmDialog : UIWindow<ConfirmDialogParam, bool>
    {
        [SerializeField]
        private Text titleLabel = default!;

        [SerializeField]
        private Text bodyLabel = default!;

        [SerializeField]
        private Button confirmButton = default!;

        [SerializeField]
        private Button cancelButton = default!;

        [SerializeField]
        private Text confirmLabel = default!;

        [SerializeField]
        private Text cancelLabel = default!;

        /// <summary>Escape = cancel (UIWindow semantics via RequestClose with
        /// the already-default result).</summary>
        public override bool IsModal => true;

        public override UniTask OnOpenAsync(CancellationToken ct)
        {
            titleLabel.text = Param.Title;
            bodyLabel.text = Param.Body;
            confirmLabel.text = StaticLoc("UI_CONFIRM");
            cancelLabel.text = StaticLoc("UI_CANCEL");

            confirmButton.onClick.RemoveAllListeners();
            cancelButton.onClick.RemoveAllListeners();
            confirmButton.onClick.AddListener(OnConfirm);
            cancelButton.onClick.AddListener(OnCancel);
            return UniTask.CompletedTask;
        }

        public override UniTask OnCloseAsync() => UniTask.CompletedTask;

        private void OnConfirm()
        {
            SetResult(true);
            RequestClose();
        }

        private void OnCancel()
        {
            SetResult(false);
            RequestClose();
        }

        /// <summary>Buttons are view-local; the labels come from the shared
        /// static localization accessor (kept tiny — the full service is
        /// presenter-side; see RootLocalization).</summary>
        private static string StaticLoc(string key) => RootLocalization.Get(key);
    }
}
