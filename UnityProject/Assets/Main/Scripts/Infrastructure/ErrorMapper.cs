using ProjectF.Infrastructure.UI;
using ProjectF.Lib.Exceptions;
using UnityEngine;

// ReSharper disable CheckNamespace
namespace ProjectF.Infrastructure
{
    /// <summary>
    /// Stage 10 (spec 10.5): maps every custom exception from ProjectF.Lib to
    /// a localized, player-facing message. Unknown exceptions fall back to a
    /// generic message; the RAW text goes to the debug log only — never into
    /// the UI (players should not read stack traces).
    /// </summary>
    public static class ErrorMapper
    {
        /// <summary>Localized message for a (possibly unknown) failure reason.
        /// <paramref name="rawReason"/> is whatever ActionQueue surfaced (an
        /// exception type name recorded on-chain, or a transport message).</summary>
        public static string Localize(string rawReason)
        {
            string message = Classify(rawReason);
            if (message is not null)
            {
                return RootLocalization.Get(message);
            }

            Debug.LogWarning($"[errors] unmapped failure reason: {rawReason}");
            return RootLocalization.Get("ERR_UNKNOWN");
        }

        /// <summary>Key that best matches the reason, or null when unknown.
        /// Public so tests can assert the mapping without a loc file.</summary>
        public static string? Classify(string? rawReason)
        {
            if (string.IsNullOrWhiteSpace(rawReason))
            {
                return null;
            }

            // The chain records EXCEPTION TYPE NAMES in the execution summary
            // (ActionQueue → ActionFailedException(exceptionNames.First())).
            // Match on those names first, then on embedded message fragments
            // for defensive depth.
            if (rawReason.Contains(nameof(NotEnoughStaminaException)))
            {
                return "ERR_NOT_ENOUGH_STAMINA";
            }

            if (rawReason.Contains(nameof(NotEnoughGoldException)))
            {
                return "ERR_NOT_ENOUGH_GOLD";
            }

            if (rawReason.Contains(nameof(ItemNotFoundException)))
            {
                return "ERR_ITEM_NOT_FOUND";
            }

            if (rawReason.Contains(nameof(PondFullException)))
            {
                return "ERR_POND_FULL";
            }

            if (rawReason.Contains(nameof(PermissionDeniedException)))
            {
                return "ERR_PERMISSION_DENIED";
            }

            if (rawReason.Contains(nameof(FailedLoadStateException)))
            {
                return "ERR_STATE_CORRUPT";
            }

            if (rawReason.Contains("timeout"))
            {
                return "ERR_TIMEOUT";
            }

            if (rawReason.Contains("cancelled"))
            {
                return "ERR_CANCELLED";
            }

            return null;
        }
    }
}
