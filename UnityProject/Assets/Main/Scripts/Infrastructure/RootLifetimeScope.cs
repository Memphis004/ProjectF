using ProjectF.Infrastructure.Blockchain;
using ProjectF.Infrastructure.DataTables;
using ProjectF.Infrastructure.Network;
using ProjectF.Infrastructure.Scene;
using ProjectF.Infrastructure.UI;
using ProjectF.Presentation.Common;
using UnityEngine;
using VContainer;
using VContainer.Unity;

// ReSharper disable CheckNamespace
namespace ProjectF.Infrastructure
{
    /// <summary>
    /// App-lifetime singletons (spec section 7). Lives in the Persistent
    /// scene (UnityProject/SETUP.md checklist) and is NEVER unloaded. Gameplay
    /// scenes add their own child LifetimeScope on top of this container.
    ///
    /// Stage 9: the UI services are APP-lifetime too (windows/toasts survive
    /// scene switches under the UIRoot in Persistent) — registered here, with
    /// the UI prefabs serialized on the scope by the generators.
    /// </summary>
    public sealed class RootLifetimeScope : LifetimeScope
    {
        [SerializeField]
        private NetworkSettings networkSettings = default!;

        [Header("Stage 9 UI")]
        [SerializeField]
        private UIRoot uiRoot = default!;

        [SerializeField]
        private RectTransform toastPrefab = default!;

        [SerializeField]
        private RectTransform inventoryWindowPrefab = default!;

        [SerializeField]
        private RectTransform confirmDialogPrefab = default!;

        [Header("Stage 10 windows")]
        [SerializeField]
        private RectTransform shopWindowPrefab = default!;

        [SerializeField]
        private RectTransform taskBoardWindowPrefab = default!;

        [SerializeField]
        private RectTransform craftWindowPrefab = default!;

        [SerializeField]
        private SpriteRegistryAsset spriteRegistry = default!;

        public NetworkSettings Settings => networkSettings;

        public UIRoot Root => uiRoot;

        protected override void Configure(IContainerBuilder builder)
        {
            // Config + identity
            builder.RegisterInstance(networkSettings);

            // Chain layer (consensus — owns ALL ownership/economy truth)
            builder.Register<KeyStore>(Lifetime.Singleton);
            builder.Register<ILibplanetClient, LibplanetClient>(Lifetime.Singleton);
            builder.Register<StateWatcher>(Lifetime.Singleton);
            // Stage 11: optimistic display overlay + connection monitor.
            // OptimisticState is DISPLAY ONLY — nothing may gate legality on it.
            builder.Register<OptimisticState>(Lifetime.Singleton);
            builder.Register<ChainConnectionMonitor>(Lifetime.Singleton);
            builder.Register<ActionQueue>(Lifetime.Singleton);

            // Presence layer (cosmetic — degrades to Offline status when the
            // hub is down; SceneRouter checks IsOnline per hop).
            builder.Register<PresenceReceiver>(Lifetime.Singleton);
            builder.Register<PresenceConnection>(Lifetime.Singleton);
            builder.Register<PlayerHubClient>(Lifetime.Singleton);
            builder.Register<IPresenceClient>(c => c.Resolve<PlayerHubClient>(), Lifetime.Singleton);

            // Scene routing (depends on IPresenceClient — the Stage 7 rule:
            // presence changes only after the Unity scene is live). Stage 9:
            // routes through the LoadingOverlay.
            builder.Register<SceneEvents>(Lifetime.Singleton).As<ISceneEvents>();
            builder.Register<SceneRouter>(c =>
                new SceneRouter(
                    c.Resolve<IPresenceClient>(),
                    c.Resolve<ISceneEvents>(),
                    c.Resolve<LoadingPresenter>()),
                Lifetime.Singleton);

            // Remote player roster (presence events → scene-local views).
            builder.Register<RemotePlayerRegistry>(Lifetime.Singleton);
            builder.Register<RemotePlayerFactory>(Lifetime.Singleton);

            // THE local player (app lifetime — spawned once, kept alive
            // across scene switches, repositioned per scene).
            builder.Register<ScenePlayerService>(Lifetime.Singleton);

            // Presentation data (Luban binaries from StreamingAssets).
            builder.Register<UnityTableService>(Lifetime.Singleton);

            // -----------------------------------------------------------------
            // Stage 9 — UI layer (spec 9: all windows registered on the root)
            // -----------------------------------------------------------------
            builder.RegisterInstance(uiRoot);
            builder.RegisterInstance(spriteRegistry);
            builder.Register<SpriteRegistry>(Lifetime.Singleton);
            builder.Register<LocalizationService>(Lifetime.Singleton);
            builder.Register<LoadingPresenter>(Lifetime.Singleton);
            builder.Register<ToastService>(Lifetime.Singleton).As<IToastService>();
            builder.Register<PlayerInputGate>(Lifetime.Singleton);

            // NOTE: the three prefabs ship as ONE UiPrefabSet registration —
            // three raw RegisterInstance(RectTransform) calls collide in
            // VContainer (implementation type = RectTransform for all).
            builder.RegisterInstance(new UiPrefabSet
            {
                Toast = toastPrefab,
                InventoryWindow = inventoryWindowPrefab,
                ConfirmDialog = confirmDialogPrefab,
                ShopWindow = shopWindowPrefab,
                TaskBoardWindow = taskBoardWindowPrefab,
                CraftWindow = craftWindowPrefab,
            });

            // Window prefabs (lazy-resolved by WindowService on first open).
            builder.RegisterInstance(new WindowPrefab<InventoryWindow>(inventoryWindowPrefab));
            builder.RegisterInstance(new WindowPrefab<ConfirmDialog>(confirmDialogPrefab));
            builder.RegisterInstance(new WindowPrefab<Presentation.Shop.ShopWindow>(shopWindowPrefab));
            builder.RegisterInstance(new WindowPrefab<Presentation.Village.TaskBoardWindow>(taskBoardWindowPrefab));
            builder.RegisterInstance(new WindowPrefab<Presentation.AuntieHouse.CraftWindow>(craftWindowPrefab));

            // WindowService resolves further services lazily from the ROOT
            // scope (first window open happens after the container is built).
            builder.Register<IWindowService>(c =>
                new WindowService(this, uiRoot, c.Resolve<PlayerInputGate>()),
                Lifetime.Singleton);

            // Presenters that live at app scope (windows + loading + chain UI).
            builder.Register<InventoryPresenter>(Lifetime.Singleton);
            builder.Register<ChainSyncPresenter>(Lifetime.Singleton);

            // Stage 10 presenters: app-lifetime (windows are pooled under the
            // persistent UIRoot; the presenters outlive gameplay scenes).
            builder.Register<Presentation.Shop.ShopPresenter>(Lifetime.Singleton);
            builder.Register<Presentation.Village.TaskBoardPresenter>(Lifetime.Singleton);
            builder.Register<Presentation.AuntieHouse.KitchenPresenter>(Lifetime.Singleton);

            // NOTE: HudPresenter is registered per SCENE (it needs that scene's
            // HudView instance) — see each XxxLifetimeScope.Configure.
            // AppBootstrapper (Stage 7) stays the LAST entry point so the UI
            // (loading overlay, toasts) is live before tables/chain sync run.

            builder.RegisterEntryPoint<UiBootstrapper>();
            builder.RegisterEntryPoint<AppBootstrapper>();
        }
    }
}
