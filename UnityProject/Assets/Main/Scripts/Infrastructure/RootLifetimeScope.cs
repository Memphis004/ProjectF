using ProjectF.Infrastructure.Blockchain;
using ProjectF.Infrastructure.DataTables;
using ProjectF.Infrastructure.Network;
using ProjectF.Infrastructure.Scene;
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
    /// </summary>
    public sealed class RootLifetimeScope : LifetimeScope
    {
        [SerializeField]
        private NetworkSettings networkSettings = default!;

        public NetworkSettings Settings => networkSettings;

        protected override void Configure(IContainerBuilder builder)
        {
            // Config + identity
            builder.RegisterInstance(networkSettings);

            // Chain layer (consensus — owns ALL ownership/economy truth)
            builder.Register<KeyStore>(Lifetime.Singleton);
            builder.Register<ILibplanetClient, LibplanetClient>(Lifetime.Singleton);
            builder.Register<ActionQueue>(Lifetime.Singleton);
            builder.Register<StateWatcher>(Lifetime.Singleton);

            // Presence layer (cosmetic — degrades to Offline status when the
            // hub is down; SceneRouter checks IsOnline per hop).
            builder.Register<PresenceReceiver>(Lifetime.Singleton);
            builder.Register<PresenceConnection>(Lifetime.Singleton);
            builder.Register<PlayerHubClient>(Lifetime.Singleton);
            builder.Register<IPresenceClient>(c => c.Resolve<PlayerHubClient>(), Lifetime.Singleton);

            // Scene routing (depends on IPresenceClient — the Stage 7 rule:
            // presence changes only after the Unity scene is live).
            builder.Register<SceneEvents>(Lifetime.Singleton).As<ISceneEvents>();
            builder.Register<SceneRouter>(Lifetime.Singleton);

            // Remote player roster (presence events → scene-local views).
            builder.Register<RemotePlayerRegistry>(Lifetime.Singleton);
            builder.Register<RemotePlayerFactory>(Lifetime.Singleton);

            // THE local player (app lifetime — spawned once, kept alive
            // across scene switches, repositioned per scene).
            builder.Register<ScenePlayerService>(Lifetime.Singleton);

            // Presentation data (Luban binaries from StreamingAssets).
            builder.Register<UnityTableService>(Lifetime.Singleton);

            // NOTE: HudPresenter is registered per SCENE (it needs that scene's
            // HudView instance) — see each XxxLifetimeScope.Configure.

            builder.RegisterEntryPoint<AppBootstrapper>();
        }
    }
}
