using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using ProjectF.Shared.Presence;
using UnityEngine;
// UnityEngine.AnimationState (legacy) collides with our presence enum —
// alias the presence one for this file.
using AnimationState = ProjectF.Shared.Presence.AnimationState;

// ReSharper disable CheckNamespace
namespace ProjectF.Infrastructure.Network
{
    /// <summary>
    /// IPresenceClient over <see cref="PresenceConnection"/>. Enforces the
    /// spec's broadcast discipline: SendMove throttled to 10 Hz, but sent
    /// IMMEDIATELY when Direction or AnimationState changes. Any hub failure
    /// degrades to offline; nothing here can block or break gameplay.
    /// </summary>
    public sealed class PlayerHubClient : IPresenceClient, IDisposable
    {
        private const int MovesPerSecond = 10;
        private static readonly long MinMoveIntervalTicks =
            TimeSpan.FromMilliseconds(1000.0 / MovesPerSecond).Ticks;

        private readonly NetworkSettings _settings;
        private readonly PresenceConnection _connection;
        private readonly PresenceReceiver _receiver;

        private PresenceStatus _status = PresenceStatus.Offline;
        private long _lastMoveTicks;
        private Direction _lastDirection = unchecked((Direction)(-1));
        private AnimationState _lastAnimation = unchecked((AnimationState)(-1));
        private readonly string _playerName;
        private int _sceneId;

        public PresenceStatus Status
        {
            get => _status;
            private set
            {
                if (_status == value)
                {
                    return;
                }

                _status = value;
                StatusChanged?.Invoke(value);
            }
        }

        public bool IsOnline => Status == PresenceStatus.Online;

        public event Action<PresenceStatus>? StatusChanged;

        /// <summary>Inbound presence events (snapshot/join/leave/move/emote/pond hint) —
        /// RemotePlayerRegistry and the HUD subscribe to these.</summary>
        public PresenceReceiver Receiver => _receiver;

        public PlayerHubClient(
            NetworkSettings settings, PresenceConnection connection, PresenceReceiver receiver)
        {
            _settings = settings;
            _connection = connection;
            _receiver = receiver;
            _playerName = settings.PlayerName;
        }

        public async UniTask ConnectAsync(
            string playerName, int sceneId, float x, float y, CancellationToken ct)
        {
            _sceneId = sceneId;

            Status = PresenceStatus.Connecting;
            bool joined = await _connection.ConnectAsync(
                string.IsNullOrWhiteSpace(playerName) ? _playerName : playerName,
                sceneId, x, y, ct);
            Status = joined ? PresenceStatus.Online : PresenceStatus.Offline;
        }

        public async UniTask ChangeSceneAsync(int sceneId, float x, float y)
        {
            if (!IsOnline)
            {
                return;
            }

            bool ok = await _connection.ChangeSceneAsync(sceneId, x, y);
            if (ok)
            {
                _sceneId = sceneId;
                // First move in the new scene must go out regardless of
                // duplicate suppression.
                // First move in the new scene must go out regardless of
                // duplicate suppression.
                _lastDirection = unchecked((Direction)(-1));
                _lastAnimation = unchecked((AnimationState)(-1));
            }
            else
            {
                Status = PresenceStatus.Offline;
            }
        }

        public void SendMove(
            int sceneId, float x, float y, Direction facing, AnimationState animation)
        {
            if (!IsOnline || sceneId != _sceneId)
            {
                return;
            }

            bool stateChanged = facing != _lastDirection || animation != _lastAnimation;
            long now = DateTime.UtcNow.Ticks;
            bool throttleElapsed = now - _lastMoveTicks >= MinMoveIntervalTicks;

            if (!stateChanged && !throttleElapsed)
            {
                return; // 10 Hz cap unless facing/animation changed
            }

            _lastMoveTicks = now;
            _lastDirection = facing;
            _lastAnimation = animation;

            _connection.Move(new PlayerMoveRequest
            {
                SceneId = sceneId,
                X = x,
                Y = y,
                Facing = facing,
                Animation = animation,
            });
        }

        public void SendEmote(int emoteId)
        {
            if (!IsOnline)
            {
                return;
            }

            _connection.Emote(emoteId);
        }

        public async UniTask DisconnectAsync()
        {
            await _connection.LeaveAsync();
            await _connection.DisposeAsync();
            Status = PresenceStatus.Offline;
        }

        public void Dispose() => _connection.Dispose();
    }
}
