using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using ProjectF.Shared.Presence;

// ReSharper disable CheckNamespace
namespace ProjectF.Infrastructure.Network
{
    /// <summary>
    /// Presence client for "hub is down / single-player visual mode". Every
    /// call is a no-op; <see cref="IPresenceClient.IsOnline"/> is always false,
    /// so <see cref="ProjectF.Infrastructure.Scene.SceneRouter"/> skips the
    /// presence hop entirely and scene loading stays 100% local.
    /// Also used in EditMode tests as the deterministic fake.
    /// </summary>
    public sealed class OfflinePresenceClient : IPresenceClient
    {
        public PresenceStatus Status => PresenceStatus.Offline;

        public bool IsOnline => false;

        public event Action<PresenceStatus>? StatusChanged
        {
            add { }
            remove { }
        }

        public UniTask ConnectAsync(
            string playerName, int sceneId, float x, float y, CancellationToken ct) =>
            UniTask.CompletedTask;

        public UniTask ChangeSceneAsync(int sceneId, float x, float y) =>
            UniTask.CompletedTask;

        public void SendMove(int sceneId, float x, float y, Direction facing,
            AnimationState animation)
        {
        }

        public void SendEmote(int emoteId)
        {
        }

        public UniTask DisconnectAsync() => UniTask.CompletedTask;
    }
}
