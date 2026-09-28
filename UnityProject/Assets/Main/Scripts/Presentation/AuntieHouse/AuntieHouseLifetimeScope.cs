using System;
using ProjectF.Infrastructure.Blockchain;
using ProjectF.Infrastructure.Network;
using ProjectF.Presentation.Common;
using UnityEngine;
using VContainer;
using VContainer.Unity;

// ReSharper disable CheckNamespace
namespace ProjectF.Presentation.AuntieHouse
{
    /// <summary>Scene 3 container. Same pattern as VillageLifetimeScope.</summary>
    public sealed class AuntieHouseLifetimeScope : LifetimeScope
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
            builder.Register<AuntieHousePresenter>(Lifetime.Singleton);
            builder.RegisterEntryPoint<AuntieHouseSceneStartup>();
        }
    }

    public sealed class AuntieHouseSceneStartup : IInitializable, IDisposable
    {
        private readonly ScenePlayerService player;
        private readonly AuntieHousePresenter presenter;
        private readonly RemotePlayerFactory remoteFactory;
        private readonly RemotePlayerRegistry remoteRegistry;

        public AuntieHouseSceneStartup(
            ScenePlayerService player,
            AuntieHousePresenter presenter,
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
            player.NotifySceneEntered(Infrastructure.Scene.SceneId.AuntieHouse);

            presenter.Start();
        }

        public void Dispose() => remoteFactory.Unbind();
    }

    /// <summary>Auntie's house: HUD + dialogue anchor (Stage 14 quests) + kitchen
    /// interactable (Stage 10 CraftFoodAction via ActionQueue).</summary>
    public sealed class AuntieHousePresenter
    {
        public RemotePlayerView RemotePlayerPrefab { get; }
        public PlayerView PlayerPrefab { get; }
        public Transform SpawnPoint { get; }

        private readonly HudPresenter hud;
        private readonly StateWatcher stateWatcher;

        public AuntieHousePresenter(
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
            hud.SetSceneName("AuntieHouse");
            // Stage 10: KitchenPresenter + CraftWindow (TbRecipe, portions,
            // KitchenUnlocked gate). Stage 14: DialogueSystem + quest chain.
        }
    }
}
