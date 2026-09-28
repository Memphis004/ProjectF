using Cysharp.Threading.Tasks;
using ProjectF.Infrastructure;
using ProjectF.Infrastructure.UI;
using UnityEngine;

// ReSharper disable CheckNamespace
namespace ProjectF.Presentation.Common
{
    /// <summary>
    /// Self-registering binding helper for the LoadingOverlay (the overlay
    /// lives on the UIRoot prefab; the LoadingPresenter is a plain-C#
    /// singleton that cannot find it alone). On enable the view hands itself
    /// to the presenter resolved from the ROOT scope; on disable it unbinds.
    /// This keeps the presenter constructor-injectable while the view stays
    /// zero-logic (push-only API surface).
    /// </summary>
    [RequireComponent(typeof(LoadingOverlay))]
    public sealed class LoadingOverlayBinder : MonoBehaviour
    {
        private LoadingPresenter? presenter;

        private void OnEnable()
        {
            // Late resolve: the overlay may activate before the root container
            // finished building (scene order); retry across a few frames.
            TryBindAsync().Forget();
        }

        private void OnDisable()
        {
            if (presenter is { })
            {
                presenter.Unbind(GetComponent<LoadingOverlay>());
                presenter = null;
            }
        }

        private async UniTaskVoid TryBindAsync()
        {
            for (int attempt = 0; attempt < 30 && presenter is null; attempt++)
            {
                RootLifetimeScope? root = FindObjectOfType<RootLifetimeScope>();
                if (root is { } && root.Container is { } &&
                    root.Container.TryResolve(typeof(LoadingPresenter), out object resolved))
                {
                    presenter = (LoadingPresenter)resolved;
                    presenter.Bind(GetComponent<LoadingOverlay>());
                    return;
                }

                await UniTask.Yield(PlayerLoopTiming.Update);
            }

            if (presenter is null)
            {
                Debug.LogWarning("[ui] LoadingOverlay could not bind a LoadingPresenter.");
            }
        }
    }
}
