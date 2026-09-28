using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Libplanet.Action;
using UnityEngine;

// ReSharper disable CheckNamespace
namespace ProjectF.Infrastructure.Blockchain
{
    /// <summary>
    /// Stage 7 spec: SubmitAsync(action, ct) → stage tx, raise OnStaged, await
    /// confirmation with a 30s timeout, raise OnConfirmed/OnFailed. NEVER
    /// throws into the caller — returns bool.
    /// Stage 11 upgrades this with optimistic mutations + reconciliation; the
    /// public surface here is kept stable for that.
    /// </summary>
    public sealed class ActionQueue
    {
        /// <summary>Spec: confirmation budget (sync API contract, not tunable yet).</summary>
        public const int DefaultTimeoutSeconds = 30;

        private readonly ILibplanetClient _client;

        public ActionQueue(ILibplanetClient client)
        {
            _client = client;
        }

        /// <summary>Fired when the tx has been signed + staged and is waiting for a block.</summary>
        public event Action<IAction>? OnStaged;

        /// <summary>Fired with the tip index that confirmed the action.</summary>
        public event Action<IAction, long>? OnConfirmed;

        /// <summary>Fired on validation failure (the action threw on-chain),
        /// timeout, or transport failure. Reason is user-displayable via
        /// ErrorMapper (Stage 10); here it is the raw exception message.</summary>
        public event Action<IAction, string>? OnFailed;

        /// <summary>
        /// Signs + stages the action and awaits its confirmation. Never throws:
        /// transport/validation errors and timeouts all come back as
        /// (false, reason).
        /// </summary>
        public async UniTask<bool> SubmitAsync(IAction action, CancellationToken ct = default)
        {
            (bool ok, _) = await SubmitWithReasonAsync(action, ct);
            return ok;
        }

        /// <summary>As <see cref="SubmitAsync"/> but returns the failure reason
        /// (raw exception message / exception type name) for the HUD/debug UI.</summary>
        public async UniTask<(bool Ok, string Reason)> SubmitWithReasonAsync(
            IAction action, CancellationToken ct = default)
        {
            // PlainValue is typed IValue; the interface seam expects the
            // Bencodex Dictionary the action actually encodes.
            var plainValue = (Bencodex.Types.Dictionary)action.PlainValue;
            OnStaged?.Invoke(action);

            try
            {
                long tip = await _client.StageAndWaitAsync(
                    plainValue,
                    TimeSpan.FromSeconds(DefaultTimeoutSeconds),
                    ct);
                OnConfirmed?.Invoke(action, tip);
                return (true, string.Empty);
            }
            catch (OperationCanceledException)
            {
                OnFailed?.Invoke(action, "cancelled");
                return (false, "cancelled");
            }
            catch (LibplanetClient.ActionFailedException ex)
            {
                // The action itself threw on-chain (validation) — not retried.
                Debug.LogWarning($"[actions] failed on-chain: {ex.Message}");
                OnFailed?.Invoke(action, ex.Message);
                return (false, ex.Message);
            }
            catch (TimeoutException)
            {
                Debug.LogWarning($"[actions] timed out after {DefaultTimeoutSeconds}s.");
                OnFailed?.Invoke(action, "timeout");
                return (false, "timeout");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[actions] transport failure: {ex.Message}");
                OnFailed?.Invoke(action, ex.Message);
                return (false, ex.Message);
            }
        }
    }
}
