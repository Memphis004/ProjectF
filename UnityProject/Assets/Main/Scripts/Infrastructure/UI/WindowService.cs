using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using ProjectF.Presentation.Common;
using UnityEngine;
using Object = UnityEngine.Object;
using IObjectResolver = VContainer.IObjectResolver;

// ReSharper disable CheckNamespace
namespace ProjectF.Infrastructure.UI
{
    /// <summary>
    /// Pool-backed window stack (spec 9.2):
    /// - One pooled instance per window TYPE, parented under the Window layer
    ///   (modals under Modal), deactivated — NEVER destroyed — after close.
    /// - OpenAsync resolves the prefab through VContainer (window types are
    ///   registered via WindowPrefab&lt;T&gt; instances on RootLifetimeScope), so
    ///   window presenters stay constructor-injected.
    /// - Escape closes the top window (UiInputDriver → CloseTopAsync).
    /// - Opening a Modal raises the raycast blocker (uGUI input below dies)
    ///   AND pushes PlayerInputGate.ModalOpen so the walking stops.
    /// - SOLE owner of the PlayerInputGate levels: ModalOpen while any modal
    ///   is open, WindowOpen while a regular window is the top concern. Both
    ///   live here so a modal opening as the FIRST window can never be
    ///   overwritten by a WindowOpen pushed on top of it.
    /// </summary>
    public sealed class WindowService : IWindowService, IDisposable
    {
        private readonly RootLifetimeScope scope;
        private readonly UIRoot uiRoot;
        private readonly PlayerInputGate inputGate;

        // type → pooled instance (its IsModal decides the layer)
        private readonly Dictionary<Type, UIWindow> pool = new();
        private readonly List<UIWindow> stack = new();

        private GameObject? blocker;

        /// <summary>The resolver is taken from the ROOT scope lazily (windows
        /// resolve presenters/prefabs on FIRST OPEN, after the container is
        /// fully built — a direct IObjectResolver constructor dependency would
        /// create a build-time cycle: WindowService itself is being built when
        /// its constructor runs).</summary>
        private IObjectResolver Resolver =>
            scope.Container
            ?? throw new InvalidOperationException(
                "Root container not built yet — UI services resolve after Awake.");

        public WindowService(RootLifetimeScope scope, UIRoot uiRoot, PlayerInputGate inputGate)
        {
            this.scope = scope;
            this.uiRoot = uiRoot;
            this.inputGate = inputGate;
        }

        public int Count => stack.Count;

        /// <summary>Topmost open window or null (UiInputDriver/debug).</summary>
        public UIWindow? Top => stack.Count > 0 ? stack[^1] : null;

        public bool IsOpen<TWindow>() where TWindow : UIWindow =>
            stack.Exists(w => w is TWindow);

        public async UniTask<TResult> OpenAsync<TWindow, TParam, TResult>(
            TParam param, CancellationToken ct)
            where TWindow : UIWindow<TParam, TResult>
            where TParam : class
        {
            bool stackWasEmpty = stack.Count == 0;

            TWindow window = await AcquireAsync<TWindow, TParam, TResult>();

            // Re-focus: an already-open window re-runs its param pass but keeps
            // its stack position (no second entry). Its existing OpenAsync
            // awaiter still completes at the eventual close.
            if (!window.IsOpen)
            {
                stack.Add(window);
                window.IsOpen = true;
            }

            window.Param = param ?? (TParam)(object)EmptyWindowParam.Instance;
            window.Result = default;
            window.gameObject.SetActive(true);

            if (window.IsModal)
            {
                EnsureBlocker();
            }
            else if (stackWasEmpty)
            {
                // First (bottom-most) regular window claims the "window open"
                // input level. WindowService owns BOTH gate levels — an
                // outside mirror (UiInputDriver) cannot tell that a 0→1
                // transition is a modal and would stack WindowOpen ABOVE
                // ModalOpen (stage-9 UI smoke finding).
                inputGate.Push(WorldInputState.WindowOpen);
            }

            await window.OnOpenAsync(ct);

            // Await the close — signalled via Closing (RequestClose /
            // SetResult+RequestClose from the view) or CloseAsync<TWindow>().
            while (window.IsOpen && !ct.IsCancellationRequested)
            {
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }

            return window.Result!;
        }

        public async UniTask CloseAsync<TWindow>() where TWindow : UIWindow
        {
            UIWindow? window = stack.Find(w => w is TWindow);
            if (window is { })
            {
                await CloseInternalAsync(window);
            }
        }

        public async UniTask CloseAllAsync()
        {
            while (stack.Count > 0)
            {
                await CloseInternalAsync(stack[^1]);
            }
        }

        public UniTask CloseTopAsync()
        {
            if (stack.Count > 0)
            {
                return CloseInternalAsync(stack[^1]);
            }

            return UniTask.CompletedTask;
        }

        // ------------------------------------------------------------------
        // Internals
        // ------------------------------------------------------------------

        private async UniTask CloseInternalAsync(UIWindow window)
        {
            if (!window.IsOpen)
            {
                return; // idempotent (double-close via Escape + code)
            }

            window.IsOpen = false;
            stack.Remove(window);
            try
            {
                await window.OnCloseAsync();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ui] window {window.GetType().Name} close threw: {ex.Message}");
            }

            window.gameObject.SetActive(false);

            // Drop the blocker only when the LAST modal left the stack.
            if (blocker is { } && !stack.Exists(w => w.IsModal))
            {
                Object.Destroy(blocker);
                blocker = null;
                inputGate.Pop(WorldInputState.ModalOpen);
            }

            // Mirror the plain window level around the stack: empty again →
            // release it; a regular window REMAINS after the last modal left →
            // reclaim the level (the modal sat above still-open windows).
            if (stack.Count == 0)
            {
                inputGate.Pop(WorldInputState.WindowOpen);
            }
            else if (!stack.Exists(w => w.IsModal) &&
                     !inputGate.Contains(WorldInputState.WindowOpen))
            {
                inputGate.Push(WorldInputState.WindowOpen);
            }
        }

        private async UniTask<TWindow> AcquireAsync<TWindow, TParam, TResult>()
            where TWindow : UIWindow<TParam, TResult>
            where TParam : class
        {
            if (pool.TryGetValue(typeof(TWindow), out UIWindow? pooled))
            {
                return (TWindow)pooled;
            }

            WindowPrefab<TWindow> registration =
                (WindowPrefab<TWindow>)Resolver.Resolve(typeof(WindowPrefab<TWindow>));
            UIWindow? probe = registration.Prefab.GetComponent<UIWindow>();
            Transform parent = probe is { IsModal: true }
                ? uiRoot.GetLayer(UILayer.Modal)
                : uiRoot.GetLayer(UILayer.Window);

            TWindow window = Object.Instantiate(registration.Prefab, parent)
                .GetComponent<TWindow>()
                ?? throw new InvalidOperationException(
                    $"Window prefab '{registration.Prefab.name}' has no " +
                    $"{typeof(TWindow).Name} component.");

            // Window-initiated close: the view calls RequestClose() (protected)
            // — the service funnels it into the same idempotent close path.
            window.Closing += w => CloseInternalAsync(w).Forget();

            // Presenter attach hook: the pooled window is NOT constructor
            // injected, so its presenter binds here on first acquisition.
            if (window is InventoryWindow inventoryWindow)
            {
                var inv = (InventoryPresenter)Resolver.Resolve(typeof(InventoryPresenter));
                inv.Attach(inventoryWindow);
            }

            pool[typeof(TWindow)] = window;
            return window;
        }

        private void EnsureBlocker()
        {
            if (blocker is { })
            {
                return;
            }

            blocker = new GameObject("ModalBlocker",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image));
            blocker.transform.SetParent(uiRoot.GetLayer(UILayer.Modal), false);
            var rect = (RectTransform)blocker.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            // Visible dim + raycast sink: every click BELOW the modal dies here
            // (spec: "blocks input below it with a raycast blocker").
            var image = blocker.GetComponent<UnityEngine.UI.Image>();
            image.sprite = uiRoot.GetComponentInChildren<SpriteRegistrySource>()?.WhiteSquare;
            image.color = new Color(0f, 0f, 0f, 0.55f);
            image.raycastTarget = true;

            // Latest sibling = renders above windows already in the layer.
            blocker.transform.SetAsLastSibling();

            inputGate.Push(WorldInputState.ModalOpen);
        }

        public void Dispose()
        {
            foreach (UIWindow window in pool.Values)
            {
                if (window is { })
                {
                    Object.Destroy(window.gameObject);
                }
            }

            pool.Clear();
            stack.Clear();
            if (blocker is { })
            {
                Object.Destroy(blocker);
                blocker = null;
            }
        }
    }
}
