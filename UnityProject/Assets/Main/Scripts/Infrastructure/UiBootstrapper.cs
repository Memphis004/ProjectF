using System;
using Cysharp.Threading.Tasks;
using ProjectF.Infrastructure.UI;
using ProjectF.Presentation.Common;
using UnityEngine;
using VContainer.Unity;

// ReSharper disable CheckNamespace
namespace ProjectF.Infrastructure
{
    /// <summary>
    /// Stage 9 UI startup (runs BEFORE AppBootstrapper's await points — the
    /// loading overlay must be live while tables + chain sync run):
    /// 1. Load the localization table (Thai default, spec 9.5).
    /// 2. Wire the UIRoot's UiInputDriver (Escape / I / click-outside).
    /// 3. Start the ChainSyncPresenter overlay loop.
    /// Registered on the ROOT scope as a VContainer entry point, ordered
    /// before AppBootstrapper.
    /// </summary>
    public sealed class UiBootstrapper : IInitializable, IDisposable
    {
        private readonly LocalizationService localization;
        private readonly IWindowService windows;
        private readonly PlayerInputGate inputGate;
        private readonly ChainSyncPresenter chainSync;
        private readonly RootLifetimeScope scope;

        public UiBootstrapper(
            LocalizationService localization,
            IWindowService windows,
            PlayerInputGate inputGate,
            ChainSyncPresenter chainSync,
            RootLifetimeScope scope)
        {
            this.localization = localization;
            this.windows = windows;
            this.inputGate = inputGate;
            this.chainSync = chainSync;
            this.scope = scope;
        }

        public void Initialize()
        {
            // 1. Localization (spec 9.5: th first; missing keys degrade to
            //    the key itself — never fatal).
            try
            {
                localization.Load(Language.Th);
                UI.RootLocalization.Register(localization);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[ui] localization load failed — keys will show raw. {ex.Message}");
            }

            // 2. UiInputDriver + Stage 10 InteractionPromptDriver (MonoBehaviours
            //    on the UIRoot prefab).
            if (scope.Root is { })
            {
                var driver = scope.Root.GetComponent<UiInputDriver>()
                    ?? scope.Root.gameObject.AddComponent<UiInputDriver>();
                driver.Configure(windows, inputGate);

                // Only the prefab instance carries a wired view — never add a
                // bare driver to the Root GO (its Update would NRE on the
                // missing view reference).
                var promptDriver =
                    scope.Root.GetComponentInChildren<Interaction.InteractionPromptDriver>(true);
                if (promptDriver is { })
                {
                    promptDriver.Configure(windows, localization);
                }
            }

            // 3. Loading overlay during initial chain sync.
            chainSync.Start();
        }

        public void Dispose()
        {
            UI.RootLocalization.Unregister(localization);
        }
    }
}
