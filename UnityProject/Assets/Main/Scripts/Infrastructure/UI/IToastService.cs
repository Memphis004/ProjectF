using System;

// ReSharper disable CheckNamespace
namespace ProjectF.Infrastructure.UI
{
    /// <summary>Visual weight of a toast (drives the icon + colour).</summary>
    public enum ToastKind
    {
        Info = 0,
        Success = 1,
        Warning = 2,
        Error = 3,
    }

    /// <summary>
    /// Toast queue (spec 9.3): max 3 visible, auto-dismiss after 3s
    /// (errors 6s), oldest evicted. <see cref="ShowPending"/> shows a
    /// spinner toast that stays until the returned handle is disposed —
    /// the "confirming" indicator of the UX contract (knowledge.md).
    /// </summary>
    public interface IToastService
    {
        void Info(string message);

        void Success(string message);

        void Warning(string message);

        /// <summary>Errors stay 6 seconds instead of 3 (spec 9.3).</summary>
        void Error(string message);

        /// <summary>Shows a spinner toast with NO auto-dismiss; disposing the
        /// handle removes it. The handle is safe to dispose twice and its
        /// Dispose never throws (action confirmations call it in finally).</summary>
        IDisposable ShowPending(string message);
    }
}
