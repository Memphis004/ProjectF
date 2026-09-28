using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

// ReSharper disable CheckNamespace
namespace ProjectF.Infrastructure.UI
{
    /// <summary>Optional parameter payload for a window's
    /// <see cref="UIWindow.OnOpenAsync"/> (spec: OpenAsync carries TParam).
    /// The default when a window needs nothing.</summary>
    public sealed class EmptyWindowParam
    {
        public static readonly EmptyWindowParam Instance = new();
    }

    /// <summary>Default "no result" payload for windows opened for effect
    /// only (InventoryWindow) — keeps the generic OpenAsync signature uniform.
    /// TResult itself stays UNCONSTRAINED so ConfirmDialog can be
    /// UIWindow&lt;ConfirmDialogParam, bool&gt; verbatim.</summary>
    public sealed class NoWindowResult
    {
        public static readonly NoWindowResult Instance = new();
    }

    /// <summary>
    /// Pooled-window stack (spec 9.2). One instance per window TYPE is kept
    /// alive after first open (hidden, not destroyed); OpenAsync reuses it.
    /// Escape closes the top window; opening a Modal raises a raycast blocker
    /// that gates input to everything below (see PlayerInputGate).
    ///
    /// TResult is UNCONSTRAINED so ConfirmDialog can be
    /// UIWindow&lt;ConfirmDialogParam, bool&gt; — "confirm/cancel returns bool"
    /// verbatim.
    /// </summary>
    public interface IWindowService
    {
        /// <summary>Opens (or focuses the already-open) TWindow under the
        /// Window/Modal layer, passing <paramref name="param"/> to
        /// OnOpenAsync. The task completes when the window CLOSES; TResult is
        /// whatever the window supplied via SetResult (default(TResult) when
        /// it never did). Opening an already-open window just re-runs its
        /// param pass — the stack position is unchanged, and all awaiters
        /// complete on the eventual close.</summary>
        UniTask<TResult> OpenAsync<TWindow, TParam, TResult>(
            TParam param, CancellationToken ct)
            where TWindow : UIWindow<TParam, TResult>
            where TParam : class;

        /// <summary>Closes TWindow (result = default) if open. Safe when it is
        /// not open.</summary>
        UniTask CloseAsync<TWindow>()
            where TWindow : UIWindow;

        /// <summary>Whether TWindow currently sits anywhere in the stack.</summary>
        bool IsOpen<TWindow>()
            where TWindow : UIWindow;

        /// <summary>Closes every open window, top to bottom (scene exits).</summary>
        UniTask CloseAllAsync();

        /// <summary>Escape behaviour: closes the top window; no-op when the
        /// stack is empty (spec 9.2).</summary>
        UniTask CloseTopAsync();

        /// <summary>Number of windows currently in the stack (diagnostics).</summary>
        int Count { get; }
    }

    /// <summary>Type-erased base for pooling + stack bookkeeping.</summary>
    public abstract class UIWindow : MonoBehaviour
    {
        /// <summary>Modal windows raise the full-screen raycast blocker for as
        /// long as they are open (spec 9.2); regular windows do not. Read off
        /// the PREFAB by WindowService, so it must not depend on runtime state.</summary>
        public abstract bool IsModal { get; }

        /// <summary>Called by WindowService AFTER the pooled instance was
        /// activated and parented. Show content here. Cancellation fires when
        /// a close was requested while this open pass was still running.</summary>
        public abstract UniTask OnOpenAsync(CancellationToken ct);

        /// <summary>Called when a close of THIS window was requested. Hide
        /// content here; the pooled GameObject stays alive underneath.</summary>
        public abstract UniTask OnCloseAsync();

        /// <summary>True while the window is visible (pool bookkeeping).</summary>
        public bool IsOpen { get; internal set; }

        /// <summary>Wired + invoked by WindowService only. Fires once per
        /// close request; the service ignores requests for already-closed
        /// windows (idempotent).</summary>
        internal event Action<UIWindow>? Closing;

        /// <summary>Ask the service to close this window (result = whatever
        /// SetResult last stored, default when none).</summary>
        protected void RequestClose() => Closing?.Invoke(this);

        internal void RaiseClosingRequested() => Closing?.Invoke(this);
    }

    /// <summary>Typed window: carries its param/result pair so WindowService
    /// funnels both through the generic OpenAsync without reflection.</summary>
    public abstract class UIWindow<TParam, TResult> : UIWindow
        where TParam : class
    {
        /// <summary>The payload this open pass was given (EmptyWindowParam.Instance
        /// when the caller passed null). Valid from OnOpenAsync to the next open.</summary>
        public TParam Param { get; internal set; } = default!;

        /// <summary>The value returned to the OpenAsync awaiter (set via
        /// SetResult). Unset = default(TResult) on close.</summary>
        internal TResult? Result;

        /// <summary>Store the value the OpenAsync awaiter receives on close.
        /// Call BEFORE RequestClose.</summary>
        protected void SetResult(TResult value) => Result = value;
    }
}
