using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Libplanet.Action;
using UnityEngine;

// ReSharper disable CheckNamespace
namespace ProjectF.Infrastructure.Blockchain
{
    /// <summary>Lifecycle of one queued action (Stage 11 spec):</summary>
    public enum PendingActionState
    {
        /// <summary>Accepted into the queue, waiting for the submit gate.</summary>
        Queued = 0,

        /// <summary>Signed + staged on the node, waiting for a block.</summary>
        Staged = 1,

        /// <summary>Executed in a block. Terminal.</summary>
        Confirmed = 2,

        /// <summary>The action threw on-chain (validation). Terminal — never retried.</summary>
        Failed = 3,

        /// <summary>No confirmation within the budget after all retries. Terminal.</summary>
        TimedOut = 4,

        /// <summary>The caller's token cancelled. Terminal.</summary>
        Cancelled = 5,
    }

    /// <summary>A single in-flight action tracked by the queue.</summary>
    public sealed class PendingAction
    {
        public PendingAction(IAction action, PendingActionState state)
        {
            Action = action;
            State = state;
        }

        public IAction Action { get; }

        /// <summary>Human-readable type id for the HUD ("fishing_v1" + params).</summary>
        public string Label { get; internal set; } = string.Empty;

        public PendingActionState State { get; internal set; }

        public long Attempt { get; internal set; }
    }

    /// <summary>
    /// Stage 7 spec: SubmitAsync(action, ct) → stage tx, raise OnStaged, await
    /// confirmation with a 30s timeout, raise OnConfirmed/OnFailed. NEVER
    /// throws into the caller — returns bool.
    ///
    /// Stage 11 upgrade:
    /// - Submissions are serialized PER AVATAR (one worker drains the queue in
    ///   FIFO order) so tx nonces are never created out of order.
    /// - Each item walks the Queued → Staged → Confirmed | Failed | TimedOut
    ///   state machine (see <see cref="Pending"/>).
    /// - Transport failures retry TWICE with 500ms/1s backoff; a validation
    ///   exception thrown by the action itself (ActionFailedException) NEVER
    ///   retries.
    /// - <see cref="Pending"/> + events drive the HUD indicator. Nothing is
    ///   persisted to disk — an app restart starts with an empty queue by
    ///   design (unconfirmed guesses die with the session; the chain is the
    ///   truth).
    /// - Optimistic hooks: <see cref="OptimisticGuess"/> builds the display
    ///   guess when the item is dequeued; the queue resolves it on
    ///   confirm/rollback so the UI moves on click, not on block.
    /// </summary>
    public sealed class ActionQueue : IDisposable
    {
        /// <summary>Spec: confirmation budget (sync API contract, not tunable yet).</summary>
        public const int DefaultTimeoutSeconds = 30;

        /// <summary>Transport retry policy: 2 retries (3 attempts total).
        /// Public: the tests assembly asserts the policy shape.</summary>
        public static readonly TimeSpan[] RetryBackoff =
        {
            TimeSpan.FromMilliseconds(500),
            TimeSpan.FromSeconds(1),
        };

        private readonly ILibplanetClient _client;
        private readonly List<PendingAction> _pending = new();
        private readonly Queue<QueueItem> _queue = new();

        private bool _draining;

        // Stage 11.5: in-flight drain task (if any) + shutdown token. The
        // drain loop checks the token between items, so DisposeAsync can stop
        // it deterministically instead of letting StageAndWaitAsync run its
        // full 30s budget into a disposed client.
        private UniTask? _drainTask;
        private CancellationTokenSource? _queueCts;

        private sealed class QueueItem
        {
            public QueueItem(PendingAction tracked, UniTaskCompletionSource<(bool, string)> tcs)
            {
                Tracked = tracked;
                Completion = tcs;
            }

            public PendingAction Tracked { get; }

            public UniTaskCompletionSource<(bool, string)> Completion { get; }

            public CancellationToken Cancellation;
        }

        public ActionQueue(ILibplanetClient client, OptimisticState? optimistic = null)
        {
            _client = client;
            Optimistic = optimistic;
        }

        /// <summary>Stage 11: when wired, every submission applies an
        /// optimistic display guess and resolves it on confirm/rollback.</summary>
        public OptimisticState? Optimistic { get; }

        /// <summary>Fire when the tx has been signed + staged and is waiting for a block.</summary>
        public event Action<IAction>? OnStaged;

        /// <summary>Fire with the tip index that confirmed the action.</summary>
        public event Action<IAction, long>? OnConfirmed;

        /// <summary>Fire on validation failure (the action threw on-chain),
        /// timeout, or transport failure. Reason is user-displayable via
        /// ErrorMapper (Stage 10); here it is the raw exception message.</summary>
        public event Action<IAction, string>? OnFailed;

        /// <summary>Fired whenever <see cref="Pending"/> changed (submit,
        /// retry, terminal state) — the HUD "…n" badge subscribes.</summary>
        public event Action? PendingChanged;

        /// <summary>Snapshot of all not-yet-confirmed actions, FIFO order.</summary>
        public IReadOnlyList<PendingAction> Pending => _pending;

        /// <summary>True while at least one action is Staged (spinner state).</summary>
        public bool HasStaged
        {
            get
            {
                foreach (PendingAction p in _pending)
                {
                    if (p.State == PendingActionState.Staged)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public void Dispose()
        {
            // Queue dies with the session by design (spec: persist nothing).
            // Stage 11.5: also cancel the SHUTDOWN token so an in-flight
            // StageAndWaitAsync/UniTask.Delay aborts instead of polling into
            // a disposed client (action confirmation polling was the second
            // live loop at teardown in the Stage 11.5 repro).
            _queueCts?.Cancel();

            foreach (QueueItem item in _queue)
            {
                item.Completion.TrySetResult((false, "cancelled"));
            }

            _queue.Clear();
            _pending.Clear();
            _queueCts?.Dispose();
            _queueCts = null;
            _drainTask = null;
        }

        /// <summary>Stage 11.5 (spec 2, applied to the queue): cancel the
        // shutdown token, resolve every queued item, THEN await the in-flight
        // drain task with a bounded timeout — the running item's
        // StageAndWaitAsync observes the token via its ct and aborts.</summary>
        public async Task DisposeAsync()
        {
            _queueCts?.Cancel();

            foreach (QueueItem item in _queue)
            {
                item.Completion.TrySetResult((false, "cancelled"));
            }

            _queue.Clear();

            UniTask? drain = _drainTask;
            if (drain is { } d)
            {
                // Bounded wait on the in-flight drain: the running item's
                // StageAndWaitAsync observes the queue CTS via its linked ct
                // and aborts; 3s is the "never block forever" ceiling.
                bool stopped = await BackgroundTaskRegistry
                    .WaitForExitAsync(d.AsTask(), TimeSpan.FromSeconds(3));
                if (!stopped)
                {
                    Debug.LogWarning(
                        "[actions] in-flight action drain did not stop within 3s — abandoned.");
                }
            }

            _pending.Clear();
            _queueCts?.Dispose();
            _queueCts = null;
            _drainTask = null;
        }

        private CancellationToken GetShutdownToken()
        {
            _queueCts ??= new CancellationTokenSource();
            return _queueCts.Token;
        }

        /// <summary>
        /// Signs + stages the action and awaits its confirmation. Never throws:
        /// transport/validation errors and timeouts all come back as
        /// (false, reason). Submissions enqueue in order and are drained one
        /// at a time so nonces stay monotonic per avatar.
        /// </summary>
        public async UniTask<bool> SubmitAsync(IAction action, CancellationToken ct = default)
        {
            (bool ok, _) = await SubmitWithReasonAsync(action, ct);
            return ok;
        }

        /// <summary>As <see cref="SubmitAsync"/> but returns the failure reason
        /// (raw exception message / exception type name) for the HUD/debug UI.</summary>
        public UniTask<(bool Ok, string Reason)> SubmitWithReasonAsync(
            IAction action, CancellationToken ct = default)
        {
            return SubmitWithGuessAsync(action, guess: null, ct);
        }

        /// <summary>
        /// Full Stage 11 entry point: like <see cref="SubmitWithReasonAsync"/>
        /// but ALSO applies <paramref name="guess"/> to the
        /// <see cref="OptimisticState"/> while the action is in flight
        /// (bait −1, stamina −5, gold −50 …). The guess rolls back on any
        /// non-confirmed outcome and is dropped on confirmation.
        /// </summary>
        public async UniTask<(bool Ok, string Reason)> SubmitWithGuessAsync(
            IAction action,
            Action<PendingMutation>? guess,
            CancellationToken ct = default)
        {
            OptimisticState.PendingHandle? handle = null;
            if (Optimistic is { } && guess is { })
            {
                handle = Optimistic.Apply(guess);
            }

            try
            {
                (bool ok, string reason) = await EnqueueAsync(action, ct);
                if (handle is { })
                {
                    if (ok)
                    {
                        handle.Confirm();
                    }
                    else
                    {
                        handle.RollBack(ClassifyRollback(reason), rawReason: reason);
                    }
                }

                return (ok, reason);
            }
            catch
            {
                handle?.RollBack(RollbackReason.Cancelled, "cancelled");
                throw;
            }
        }

        /// <summary>Maps a failure reason to the rollback cause for the toast.
        /// Public: the tests assembly asserts the mapping.</summary>
        public static RollbackReason ClassifyRollback(string reason)
        {
            if (string.IsNullOrEmpty(reason))
            {
                return RollbackReason.Failed;
            }

            if (reason.Contains("timeout", StringComparison.OrdinalIgnoreCase))
            {
                return RollbackReason.TimedOut;
            }

            if (reason.Contains("cancelled", StringComparison.OrdinalIgnoreCase))
            {
                return RollbackReason.Cancelled;
            }

            return RollbackReason.Failed;
        }

        // ------------------------------------------------------------------
        // Queue core: FIFO drain → nonce ordering is never violated.
        // ------------------------------------------------------------------

        private async UniTask<(bool Ok, string Reason)> EnqueueAsync(
            IAction action, CancellationToken ct)
        {
            var tracked = new PendingAction(action, PendingActionState.Queued)
            {
                Label = Describe(action),
            };
            _pending.Add(tracked);
            PendingChanged?.Invoke();

            var item = new QueueItem(
                tracked, new UniTaskCompletionSource<(bool, string)>())
            {
                Cancellation = ct,
            };
            _queue.Enqueue(item);

            UniTask drain = DrainAsync();
            _drainTask = drain;
            await drain;

            (bool ok, string reason) = await item.Completion.Task;
            _pending.Remove(tracked);
            PendingChanged?.Invoke();
            return (ok, reason);
        }

        /// <summary>Runs the queue drain; re-entrant calls while a drain is
        /// active simply return (their items are already in the queue).</summary>
        private async UniTask DrainAsync()
        {
            if (_draining)
            {
                return;
            }

            _draining = true;
            try
            {
                while (_queue.Count > 0)
                {
                    QueueItem item = _queue.Dequeue();
                    (bool ok, string reason) = await ExecuteAsync(item);
                    item.Completion.TrySetResult((ok, reason));
                }
            }
            finally
            {
                _draining = false;
            }
        }

        /// <summary>State machine + retry policy for ONE queue item.</summary>
        private async UniTask<(bool Ok, string Reason)> ExecuteAsync(QueueItem item)
        {
            PendingAction tracked = item.Tracked;
            CancellationToken ct = item.Cancellation;

            for (int attempt = 0; ; attempt++)
            {
                tracked.Attempt = attempt + 1;

                try
                {
                    // Shutdown check per item (spec 1: no unbounded work
                    // without a cancellation check).
                    if (_queueCts is { } qc && qc.IsCancellationRequested)
                    {
                        ct = CancellationTokenSource
                            .CreateLinkedTokenSource(ct, qc.Token).Token;
                    }

                    ct.ThrowIfCancellationRequested();

                    // PlainValue is typed IValue; the interface seam expects
                    // the Bencodex Dictionary the action actually encodes.
                    var plainValue = (Bencodex.Types.Dictionary)tracked.Action.PlainValue;
                    tracked.State = PendingActionState.Staged;
                    PendingChanged?.Invoke();
                    OnStaged?.Invoke(tracked.Action);

                    long tip = await _client.StageAndWaitAsync(
                        plainValue,
                        TimeSpan.FromSeconds(DefaultTimeoutSeconds),
                        ct);

                    tracked.State = PendingActionState.Confirmed;
                    PendingChanged?.Invoke();
                    OnConfirmed?.Invoke(tracked.Action, tip);
                    return (true, string.Empty);
                }
                catch (OperationCanceledException)
                {
                    tracked.State = PendingActionState.Cancelled;
                    PendingChanged?.Invoke();
                    OnFailed?.Invoke(tracked.Action, "cancelled");
                    return (false, "cancelled");
                }
                catch (LibplanetClient.ActionFailedException ex)
                {
                    // The action itself threw on-chain (validation) —
                    // NEVER retried: retrying would just fail again.
                    Debug.LogWarning($"[actions] failed on-chain: {ex.Message}");
                    tracked.State = PendingActionState.Failed;
                    PendingChanged?.Invoke();
                    OnFailed?.Invoke(tracked.Action, ex.Message);
                    return (false, ex.Message);
                }
                catch (TimeoutException)
                {
                    // The tx may still land later; guessing further is worse
                    // than showing truth. Terminal per spec (TimedOut).
                    Debug.LogWarning(
                        $"[actions] timed out after {DefaultTimeoutSeconds}s " +
                        $"(attempt {tracked.Attempt}).");
                    tracked.State = PendingActionState.TimedOut;
                    PendingChanged?.Invoke();
                    OnFailed?.Invoke(tracked.Action, "timeout");
                    return (false, "timeout");
                }
                catch (Exception ex) when (attempt < RetryBackoff.Length)
                {
                    // Transport failure — retry with backoff, staying in Staged.
                    Debug.LogWarning(
                        $"[actions] transport failure ({ex.Message}) — retry " +
                        $"{attempt + 1}/{RetryBackoff.Length} in " +
                        $"{RetryBackoff[attempt].TotalMilliseconds:0}ms.");
                    tracked.State = PendingActionState.Queued;
                    PendingChanged?.Invoke();

                    try
                    {
                        await UniTask.Delay(RetryBackoff[attempt], cancellationToken: ct);
                    }
                    catch (OperationCanceledException)
                    {
                        tracked.State = PendingActionState.Cancelled;
                        PendingChanged?.Invoke();
                        OnFailed?.Invoke(tracked.Action, "cancelled");
                        return (false, "cancelled");
                    }
                }
                catch (Exception ex)
                {
                    // Transport failure, retries exhausted.
                    Debug.LogWarning($"[actions] transport failure (retries exhausted): {ex.Message}");
                    tracked.State = PendingActionState.Failed;
                    PendingChanged?.Invoke();
                    OnFailed?.Invoke(tracked.Action, ex.Message);
                    return (false, ex.Message);
                }
            }
        }

        private static string Describe(IAction action) => action.GetType().Name;
    }
}
