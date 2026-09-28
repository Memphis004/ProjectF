using System;
using System.Collections.Generic;
using ProjectF.Shared.Presence;
using UnityEngine;

// ReSharper disable CheckNamespace
namespace ProjectF.Infrastructure.Network
{
    /// <summary>
    /// Scene-local roster of remote players, rebuilt on every scene load.
    /// Feeds <see cref="RemotePlayerFactory"/>: Added spawns the view,
    /// Updated/Removed drive it, Clear() is called by each scene startup when
    /// its scene becomes live — which happens BEFORE the presence group
    /// switches (SceneRouter ordering), so no snapshot targets a dead scene.
    /// Subscribes to presence events in the constructor (it lives on the root
    /// container, so there is exactly one subscription for the app lifetime).
    /// </summary>
    public sealed class RemotePlayerRegistry : IDisposable
    {
        private readonly IPresenceClient _presenceClient;

        private readonly Dictionary<string, PlayerSnapshot> _players =
            new(StringComparer.Ordinal);

        public RemotePlayerRegistry(IPresenceClient presenceClient)
        {
            _presenceClient = presenceClient;

            if (_presenceClient is PlayerHubClient hub)
            {
                PresenceReceiver receiver = hub.Receiver;
                receiver.SceneSnapshot += OnSceneSnapshot;
                receiver.Join += OnJoin;
                receiver.Move += OnMove;
                receiver.Leave += OnLeave;
            }
            else
            {
                Debug.Log("[presence] offline client — remote roster disabled.");
            }
        }

        public IReadOnlyDictionary<string, PlayerSnapshot> Players => _players;

        public int Count => _players.Count;

        public event Action<PlayerSnapshot>? Added;
        public event Action<PlayerSnapshot>? Updated;
        public event Action<string>? Removed;
        public event Action? Cleared;

        private void OnSceneSnapshot(PlayerSnapshot[] players)
        {
            // A fresh authoritative roster for the CURRENT scene — the server
            // pushes it on join and after ChangeSceneAsync.
            Clear();
            foreach (PlayerSnapshot player in players)
            {
                Upsert(player);
            }
        }

        private void OnJoin(PlayerSnapshot player) => Upsert(player);

        private void OnMove(PlayerSnapshot player)
        {
            if (_players.ContainsKey(player.SessionId))
            {
                Upsert(player);
            }
        }

        private void Upsert(PlayerSnapshot player)
        {
            bool existed = _players.TryGetValue(player.SessionId, out PlayerSnapshot? old);
            _players[player.SessionId] = player;

            if (existed)
            {
                Updated?.Invoke(player);
            }
            else
            {
                Debug.Log($"[presence] remote joined: {player.PlayerName} ({player.SessionId})");
                Added?.Invoke(player);
            }
        }

        private void OnLeave(string sessionId)
        {
            if (_players.Remove(sessionId))
            {
                Removed?.Invoke(sessionId);
            }
        }

        /// <summary>Called when a new gameplay scene is loaded — drops stale
        /// views immediately (their MonoBehaviours die with the old scene).</summary>
        public void Clear()
        {
            if (_players.Count == 0)
            {
                return;
            }

            _players.Clear();
            Cleared?.Invoke();
        }

        public void Dispose() => Clear();
    }
}
