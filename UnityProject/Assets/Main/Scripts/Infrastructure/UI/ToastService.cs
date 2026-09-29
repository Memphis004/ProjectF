using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using TMPro;
using TMPro;

using UnityEngine.UI;

// ReSharper disable CheckNamespace
namespace ProjectF.Infrastructure.UI
{
    /// <summary>
    /// Toast queue implementation (spec 9.3): queue with max 3 visible,
    /// auto-dismiss 3s (errors 6s), pending spinner toasts stay until their
    /// handle is disposed. Instances are POOLED ToastViews instantiated from
    /// the Toast prefab (U * N times: one prefab, N concurrent toasts).
    /// Plain C# — registered on the ROOT scope as IToastService.
    ///
    /// The ToastView MonoBehaviour lives in ToastView.cs (MonoScript rule:
    /// class name must equal file name for prefab serialization).
    /// </summary>
    public sealed class ToastService : IToastService, IDisposable
    {
        private const int MaxVisible = 3;
        private const float AutoDismissSeconds = 3f;
        private const float ErrorDismissSeconds = 6f;

        private readonly UIRoot uiRoot;
        private readonly RectTransform prefab;

        // Newest first for layout; each entry carries its own cts.
        private readonly List<ToastEntry> active = new();
        private readonly Stack<ToastView> pool = new();

        private sealed class ToastEntry
        {
            public ToastView View = default!;
            public ToastKind Kind;
            public string Message = string.Empty;
            public CancellationTokenSource? AutoDismissCts;
            public bool Pending;
        }

        private sealed class PendingHandle : IDisposable
        {
            private ToastService? owner;
            private readonly ToastEntry entry;

            public PendingHandle(ToastService owner, ToastEntry entry)
            {
                this.owner = owner;
                this.entry = entry;
            }

            public void Dispose()
            {
                // Double-dispose is a no-op (ActionQueue finally-blocks call
                // this unconditionally); never throws.
                owner?.Remove(entry);
                owner = null;
            }
        }

        public ToastService(UIRoot uiRoot, UiPrefabSet prefabs)
        {
            this.uiRoot = uiRoot;
            this.prefab = prefabs.Toast;
        }

        public void Info(string message) => Show(ToastKind.Info, message, autoDismiss: true);

        public void Success(string message) => Show(ToastKind.Success, message, autoDismiss: true);

        public void Warning(string message) => Show(ToastKind.Warning, message, autoDismiss: true);

        public void Error(string message) =>
            Show(ToastKind.Error, message, autoDismiss: true, errorTiming: true);

        public IDisposable ShowPending(string message)
        {
            ToastEntry entry = Show(ToastKind.Info, message, autoDismiss: false);
            entry.Pending = true;
            return new PendingHandle(this, entry);
        }

        private ToastEntry Show(
            ToastKind kind, string message, bool autoDismiss, bool errorTiming = false)
        {
            // Evict the OLDEST entry (end of the list) when at capacity.
            while (active.Count >= MaxVisible)
            {
                Remove(active[^1]);
            }

            var entry = new ToastEntry
            {
                View = AcquireView(),
                Kind = kind,
                Message = message,
            };

            active.Insert(0, entry); // newest first
            Apply(entry);

            if (autoDismiss)
            {
                entry.AutoDismissCts = new CancellationTokenSource();
                DismissAfterAsync(
                    entry,
                    errorTiming ? ErrorDismissSeconds : AutoDismissSeconds,
                    entry.AutoDismissCts.Token).Forget();
            }

            return entry;
        }

        private void Apply(ToastEntry entry)
        {
            ToastView view = entry.View;
            view.gameObject.SetActive(true);
            view.transform.SetAsLastSibling();
            view.MessageLabel.text = entry.Message;

            (Color color, string glyph) = entry.Kind switch
            {
                ToastKind.Success => (new Color(0.36f, 0.78f, 0.35f), "OK"),
                ToastKind.Warning => (new Color(0.95f, 0.80f, 0.25f), "!"),
                ToastKind.Error => (new Color(0.85f, 0.30f, 0.25f), "X"),
                _ => (new Color(0.45f, 0.65f, 0.90f), "i"),
            };

            view.Icon.color = color;
            // Text glyph placeholder for the Stage 13 icon pass — the icon is
            // a small Image with a letter, cheap and readable at 320x180.
            TMP_Text? glyphLabel = view.Icon.GetComponent<TMP_Text>();
            if (glyphLabel is { })
            {
                glyphLabel.text = glyph;
            }

            bool pending = entry.Pending;
            view.Spinner.gameObject.SetActive(pending);
        }

        private async UniTaskVoid DismissAfterAsync(
            ToastEntry entry, float seconds, CancellationToken ct)
        {
            try
            {
                await UniTask.Delay(
                    TimeSpan.FromSeconds(seconds),
                    DelayType.UnscaledDeltaTime,
                    cancellationToken: ct);
            }
            catch (OperationCanceledException)
            {
                return; // superseded by a pending→dismissed transition
            }

            Remove(entry);
        }

        private void Remove(ToastEntry entry)
        {
            if (!active.Remove(entry))
            {
                return; // already removed (double dispose / evicted)
            }

            entry.AutoDismissCts?.Cancel();
            entry.AutoDismissCts?.Dispose();
            entry.AutoDismissCts = null;
            entry.View.gameObject.SetActive(false);
            pool.Push(entry.View);
            entry.View = null!;
        }

        private ToastView AcquireView()
        {
            if (pool.TryPop(out ToastView? reused))
            {
                return reused;
            }

            ToastView view = UnityEngine.Object.Instantiate(prefab, uiRoot.GetLayer(UILayer.Toast))
                .GetComponent<ToastView>()
                ?? throw new InvalidOperationException(
                    "Toast prefab has no ToastView component.");
            return view;
        }

        public void Dispose()
        {
            for (int i = active.Count - 1; i >= 0; i--)
            {
                Remove(active[i]);
            }

            active.Clear();
        }
    }
}
