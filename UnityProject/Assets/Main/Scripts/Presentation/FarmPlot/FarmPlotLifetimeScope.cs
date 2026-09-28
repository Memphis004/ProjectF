using System;
using ProjectF.Infrastructure.Blockchain;
using ProjectF.Infrastructure.Network;
using ProjectF.Presentation.Common;
using UnityEngine;
using VContainer;
using VContainer.Unity;

// ReSharper disable CheckNamespace
namespace ProjectF.Presentation.FarmPlot
{
    /// <summary>Scene 4 container — the pond/farm scene (data/pond.csv:
    /// scene_id 4). Hosts the FishingSpot (pondId 1) and the farm grid.</summary>
    public sealed class FarmPlotLifetimeScope : LifetimeScope
    {
        [SerializeField]
        private PlayerView playerPrefab = default!;

        [SerializeField]
        private RemotePlayerView remotePlayerPrefab = default!;

        [SerializeField]
        private HudView hudView = default!;

        [SerializeField]
        private FishingView fishingView = default!;

        [SerializeField]
        private Transform spawnPoint = default!;

        protected override void Configure(IContainerBuilder builder)
        {
            builder.RegisterInstance(playerPrefab);
            builder.RegisterInstance(remotePlayerPrefab);
            builder.RegisterInstance(hudView);
            builder.RegisterInstance(fishingView);
            builder.RegisterInstance(spawnPoint);

            builder.Register<HudPresenter>(Lifetime.Singleton);
            builder.Register<FishingPresenter>(Lifetime.Singleton);
            builder.Register<FarmPlotPresenter>(Lifetime.Singleton);
            builder.RegisterEntryPoint<FarmPlotSceneStartup>();
        }
    }

    public sealed class FarmPlotSceneStartup : IInitializable, IDisposable
    {
        private readonly ScenePlayerService player;
        private readonly FarmPlotPresenter presenter;
        private readonly RemotePlayerFactory remoteFactory;
        private readonly RemotePlayerRegistry remoteRegistry;

        public FarmPlotSceneStartup(
            ScenePlayerService player,
            FarmPlotPresenter presenter,
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
            player.NotifySceneEntered(Infrastructure.Scene.SceneId.FarmPlot);
            player.PushInputGate(presenter.InputGate);
            UiSceneStartup.AttachHud(presenter.HudView);

            presenter.Start();
        }

        public void Dispose()
        {
            presenter.Dispose();
            remoteFactory.Unbind();
        }
    }

    /// <summary>Farm scene presenter: fishing (pondId 1) + farm tiles.
    /// pond.csv: id 1, scene 4, slot_count 2, occupy_blocks 120.</summary>
    public sealed class FarmPlotPresenter : IDisposable
    {
        /// <summary>data/pond.csv row 1 — the village small pond.</summary>
        public const int VillagePondId = 1;

        public RemotePlayerView RemotePlayerPrefab { get; }
        public PlayerView PlayerPrefab { get; }
        public Transform SpawnPoint { get; }

        private readonly HudPresenter hud;
        private readonly StateWatcher stateWatcher;
        private readonly FishingPresenter fishing;
        private bool started;

        public PlayerInputGate InputGate { get; }
        public HudView HudView { get; }

        public FarmPlotPresenter(
            HudView hudView, HudPresenter hudPresenter, StateWatcher stateWatcher,
            PlayerInputGate inputGate,
            FishingView fishingView, FishingPresenter fishingPresenter,
            RemotePlayerView remotePlayerPrefab, PlayerView playerPrefab, Transform spawnPoint)
        {
            HudView = hudView;
            hud = hudPresenter;
            this.stateWatcher = stateWatcher;
            InputGate = inputGate;
            fishing = fishingPresenter;
            RemotePlayerPrefab = remotePlayerPrefab;
            PlayerPrefab = playerPrefab;
            SpawnPoint = spawnPoint;
        }

        public void Start()
        {
            if (started)
            {
                return;
            }

            started = true;
            hud.Bind(stateWatcher);
            hud.SetSceneName("FarmPlot");
            fishing.Bind();
            // Stage 8 wires the FishingSpot trigger + FarmTile grid here;
            // Stage 13 adds the cast mini-game between occupy and fishing_v1.
        }

        public void Dispose() => fishing.Dispose();
    }
}
