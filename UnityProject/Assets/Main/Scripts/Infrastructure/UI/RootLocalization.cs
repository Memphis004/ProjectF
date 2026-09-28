using UnityEngine;

// ReSharper disable CheckNamespace
namespace ProjectF.Infrastructure.UI
{
    /// <summary>
    /// Tiny static bridge so zero-logic VIEWS (ConfirmDialog buttons) can
    /// localize their fixed labels without presenter involvement. The
    /// LocalizationService instance registers itself here on construction
    /// (RootLifetimeScope) — before that, Get returns the key (same
    /// missing-key contract as the real service, no crash during boot).
    /// </summary>
    public static class RootLocalization
    {
        private static LocalizationService? instance;

        public static LocalizationService? Instance => instance;

        internal static void Register(LocalizationService service) => instance = service;

        internal static void Unregister(LocalizationService service)
        {
            if (ReferenceEquals(instance, service))
            {
                instance = null;
            }
        }

        /// <summary>Convenience getter: key when no service is registered yet.</summary>
        public static string Get(string key) =>
            instance is { } ? instance.Get(key) : key;
    }
}
