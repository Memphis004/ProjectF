using Cysharp.Threading.Tasks;
using UnityEngine;
using TMPro;
using TMPro;

using UnityEngine.UI;

// ReSharper disable CheckNamespace
namespace ProjectF.Infrastructure.UI
{
    /// <summary>
    /// Full-screen loading overlay (spec 9.4): shown during scene transitions
    /// and the initial chain sync. Progress line reads e.g.
    /// "syncing block 1234 / 1717" — the total is OPTIONAL (Libplanet 5.5.3
    /// reports no target height; when unknown we print the current block only).
    /// Zero logic: everything is pushed by <see cref="LoadingPresenter"/>.
    /// </summary>
    public sealed class LoadingOverlay : MonoBehaviour
    {
        [SerializeField]
        private Image dim = default!;

        [SerializeField]
        private TMP_Text titleLabel = default!;

        [SerializeField]
        private TMP_Text progressLabel = default!;

        public Image Dim => dim;
        public TMP_Text TitleLabel => titleLabel;
        public TMP_Text ProgressLabel => progressLabel;

        private void Awake()
        {
            gameObject.SetActive(false);
        }
    }

    /// <summary>Presenter for the overlay (plain C#). Registered Singleton on
    /// the root scope; every LoadingOverlay INSTANCE in a scene binds here —
    /// exactly one exists at a time (the UIRoot prefab carries it).</summary>
    public sealed class LoadingPresenter
    {
        private readonly LocalizationService loc;

        private LoadingOverlay? view;

        public LoadingPresenter(LocalizationService loc)
        {
            this.loc = loc;
        }

        public bool IsVisible => view is { } && view.gameObject.activeSelf;

        /// <summary>Binds a freshly resolved overlay instance (called by
        /// LoadingOverlay.OnEnable's registration helper).</summary>
        public void Bind(LoadingOverlay overlay)
        {
            view = overlay;
        }

        public void Unbind(LoadingOverlay overlay)
        {
            if (ReferenceEquals(view, overlay))
            {
                view = null;
            }
        }

        public void Show(string title)
        {
            if (view is null)
            {
                return; // no overlay in this scene configuration — skip silently
            }

            view.gameObject.SetActive(true);
            if (view.TitleLabel is { })
            {
                view.TitleLabel.text = title;
            }

            SetProgress(null, null);
        }

        public void Hide()
        {
            if (view is { })
            {
                view.gameObject.SetActive(false);
            }
        }

        /// <summary>Sets the progress line. total == null renders
        /// "syncing block N" (spec's honest fallback); both values render
        /// "syncing block N / M" (the "1234 / 1717" shape).</summary>
        public void SetProgress(long? current, long? total)
        {
            TMP_Text? label = view?.ProgressLabel;
            if (label is null)
            {
                return;
            }

            label.text = current is { } c
                ? total is { } t
                    ? loc.Get("UI_LOADING_SYNCING_BLOCK_TOTAL")
                        .Replace("{0}", c.ToString())
                        .Replace("{1}", t.ToString())
                    : loc.Get("UI_LOADING_SYNCING_BLOCK").Replace("{0}", c.ToString())
                : loc.Get("UI_LOADING");
        }
    }
}
