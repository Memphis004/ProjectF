using UnityEngine;

// ReSharper disable CheckNamespace
namespace ProjectF.Infrastructure.UI
{
    /// <summary>
    /// One registered window prefab: TWindow → RectTransform. RootLifetimeScope
    /// registers one instance per window type (RegisterWindowPrefab), and
    /// WindowService resolves it lazily on first open — the prefab lives on
    /// disk, the pooled instance materializes under the UIRoot layer on
    /// demand and is NEVER destroyed afterwards (spec 9.2).
    /// </summary>
    public sealed class WindowPrefab<TWindow>
        where TWindow : UIWindow
    {
        public RectTransform Prefab { get; }

        public WindowPrefab(RectTransform prefab)
        {
            Prefab = prefab;
        }
    }
}
