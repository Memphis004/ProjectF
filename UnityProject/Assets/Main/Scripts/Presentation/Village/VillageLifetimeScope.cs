using System;
using ProjectF.Infrastructure.Blockchain;
using ProjectF.Infrastructure.Network;
using ProjectF.Infrastructure.Scene;
using ProjectF.Presentation.Common;
using UnityEngine;
using VContainer;
using VContainer.Unity;

// ReSharper disable CheckNamespace
namespace ProjectF.Presentation.Village
{
    /// <summary>
    /// Per-scene container (one LifetimeScope per gameplay scene — knowledge.md
    /// style conventions). Parent is RootLifetimeScope (parentReference — see
    /// UnityProject/SETUP.md), so every gameplay scene resolves the same singletons
    /// while adding scene-local registrations (the presenter + startup).
    /// ShopLifetimeScope / AuntieHouseLifetimeScope / FarmPlotLifetimeScope
    /// follow this exact pattern.
    /// </summary>
    public sealed class VillageLifetimeScope : LifetimeScope
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

            // HUD presenter is scene-local: it binds THIS scene's HudView to
            // the app-lifetime StateWatcher + presence client.
            builder.Register<HudPresenter>(Lifetime.Singleton);
            builder.Register<VillagePresenter>(Lifetime.Singleton);
            builder.RegisterEntryPoint<VillageSceneStartup>();
        }
    }

    /// <summary>Runs when the scene container is built. Ordering matters: the
    /// player + remote registry must be ready before the presence group
    /// switches to this scene — which holds because SceneRouter fires
    /// NotifySceneLoaded (registry rebuild) and only THEN awaits
    /// ChangeSceneAsync (Stage 7 ordering rule).</summary>
    public sealed class VillageSceneStartup : IInitializable, IDisposable
    {
        private readonly ScenePlayerService player;
        private readonly VillagePresenter presenter;
        private readonly RemotePlayerFactory remoteFactory;
        private readonly RemotePlayerRegistry remoteRegistry;

        public VillageSceneStartup(
            ScenePlayerService player,
            VillagePresenter presenter,
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
            // Remote views die with the previous scene: clear stale roster
            // entries before the presence group switches.
            remoteRegistry.Clear();
            remoteFactory.Configure(presenter.RemotePlayerPrefab);
            remoteFactory.Bind();

            player.BindPrefab(presenter.PlayerPrefab);
            player.EnsureSpawned(presenter.SpawnPoint.position);
            player.NotifySceneEntered(SceneId.Village);

            presenter.Start();
        }

        public void Dispose() => remoteFactory.Unbind();
    }

    /// <summary>Village gameplay presenter: HUD + remote roster + (Stage 8+)
    /// scene transition triggers. Registered per-scene on purpose.</summary>
    public sealed class VillagePresenter
    {
        public RemotePlayerView RemotePlayerPrefab { get; }
        public PlayerView PlayerPrefab { get; }
        public Transform SpawnPoint { get; }

        private readonly HudPresenter hud;
        private readonly StateWatcher stateWatcher;

        public VillagePresenter(
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
            hud.SetSceneName("Village");
            // Stage 8 wires SceneTransitionTriggers here; Stage 10 adds the
            // TaskBoard presenter. Nothing else for Stage 7 in the village.
        }
    }
}
