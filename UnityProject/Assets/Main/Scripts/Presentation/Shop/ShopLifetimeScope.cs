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
    /// see UnityProject/SETUP.md for the scene-side wiring.</summary>
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
            builder.Register<ShopPresenter>(Lifetime.Singleton);
            builder.RegisterEntryPoint<ShopSceneStartup>();
        }
    }

    public sealed class ShopSceneStartup : IInitializable, IDisposable
    {
        private readonly ScenePlayerService player;
        private readonly ShopPresenter presenter;
        private readonly RemotePlayerFactory remoteFactory;
        private readonly RemotePlayerRegistry remoteRegistry;

        public ShopSceneStartup(
            ScenePlayerService player,
            ShopPresenter presenter,
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

            presenter.Start();
        }

        public void Dispose() => remoteFactory.Unbind();
    }

    /// <summary>Shop scene: HUD + exits + buy window placeholder (Stage 10
    /// wires TbShop rows + BuyItemAction through ActionQueue).</summary>
    public sealed class ShopPresenter
    {
        public RemotePlayerView RemotePlayerPrefab { get; }
        public PlayerView PlayerPrefab { get; }
        public Transform SpawnPoint { get; }

        private readonly HudPresenter hud;
        private readonly StateWatcher stateWatcher;

        public ShopPresenter(
            HudView hudView, HudPresenter hudPresenter, StateWatcher stateWatcher,
            RemotePlayerView remotePlayerPrefab, PlayerView playerPrefab, Transform spawnPoint)
        {
            hud = hudPresenter;
            this.stateWatcher = stateWatcher;
            RemotePlayerPrefab = remotePlayerPrefab;
            PlayerPrefab = playerPrefab;
            SpawnPoint = spawnPoint;
        }

        public void Start()
        {
            hud.Bind(stateWatcher);
            hud.SetSceneName("Shop");
            // Stage 10: ShopPresenter + ShopWindow (TbShop joined with TbItem,
            // BuyItemAction via ActionQueue with pending toast + error mapping).
        }
    }
}
