using System;
using ProjectF.Infrastructure.Blockchain;
using ProjectF.Infrastructure.Network;
using ProjectF.Presentation.Common;
using UnityEngine;
using VContainer;
using VContainer.Unity;

// ReSharper disable CheckNamespace
namespace ProjectF.Presentation.Shop
{
    /// <summary>Scene 2 container. Same pattern as VillageLifetimeScope —
    /// see UnityProject/SETUP.md for the scene-side wiring. Stage 10: the
    /// shop window/presenter live at APP scope (ShopWindow.cs, registered on
    /// RootLifetimeScope — the pooled window outlives scenes); this scene
    /// opens it through the [E] interaction on the shopkeeper NPC.</summary>
    public sealed class ShopLifetimeScope : LifetimeScope
    {
        [SerializeField]
        private PlayerView playerPrefab = default!;

        [SerializeField]
        private RemotePlayerView remotePlayerPrefab = default!;

        [SerializeField]
        private HudView hudView = default!;

        [SerializeField]
        private Transform spawnPoint = default!;

        protected override void Configure(IContainerBuilder builder)
        {
            builder.RegisterInstance(playerPrefab);
            builder.RegisterInstance(remotePlayerPrefab);
            builder.RegisterInstance(hudView);
            builder.RegisterInstance(spawnPoint);

            builder.Register<HudPresenter>(Lifetime.Singleton);
            builder.Register<ShopScenePresenter>(Lifetime.Singleton);
            builder.RegisterEntryPoint<ShopSceneStartup>();
        }
    }

    public sealed class ShopSceneStartup : IInitializable, IDisposable
    {
        private readonly ScenePlayerService player;
        private readonly ShopScenePresenter presenter;
        private readonly RemotePlayerFactory remoteFactory;
        private readonly RemotePlayerRegistry remoteRegistry;

        public ShopSceneStartup(
            ScenePlayerService player,
            ShopScenePresenter presenter,
            RemotePlayerFactory remoteFactory,
            RemotePlayerRegistry remoteRegistry)
        {
            this.player = player;
            this.presenter = presenter;
            this.remoteFactory = remoteFactory;
            this.remoteRegistry = remoteRegistry;
        }

        public void Initialize()
        {
            remoteRegistry.Clear();
            remoteFactory.Configure(presenter.RemotePlayerPrefab);
            remoteFactory.Bind();

            player.BindPrefab(presenter.PlayerPrefab);
            player.EnsureSpawned(presenter.SpawnPoint.position);
            player.NotifySceneEntered(Infrastructure.Scene.SceneId.Shop);
            player.PushInputGate(presenter.InputGate);
            UiSceneStartup.AttachHud(presenter.HudView);

            presenter.Start();
        }

        public void Dispose() => remoteFactory.Unbind();
    }

    /// <summary>Scene-scene glue: HUD binding + player spawn. The Stage 10
    /// shop economy (TbShop ⋈ TbItem, BuyItemAction/SellItemAction) runs in
    /// the app-scope ShopPresenter.</summary>
    public sealed class ShopScenePresenter
    {
        public RemotePlayerView RemotePlayerPrefab { get; }
        public PlayerView PlayerPrefab { get; }
        public Transform SpawnPoint { get; }

        private readonly HudPresenter hud;
        private readonly StateWatcher stateWatcher;

        public PlayerInputGate InputGate { get; }
        public HudView HudView { get; }

        public ShopScenePresenter(
            HudView hudView, HudPresenter hudPresenter, StateWatcher stateWatcher,
            PlayerInputGate inputGate,
            RemotePlayerView remotePlayerPrefab, PlayerView playerPrefab, Transform spawnPoint)
        {
            HudView = hudView;
            hud = hudPresenter;
            this.stateWatcher = stateWatcher;
            InputGate = inputGate;
            RemotePlayerPrefab = remotePlayerPrefab;
            PlayerPrefab = playerPrefab;
            SpawnPoint = spawnPoint;
        }

        public void Start()
        {
            hud.Bind(stateWatcher);
            hud.SetSceneName("Shop");
        }
    }
}
