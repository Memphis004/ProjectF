using System.Collections.Generic;
using ProjectF.Presentation.Common;
using ProjectF.Shared.Presence;
using UnityEngine;

// ReSharper disable CheckNamespace
namespace ProjectF.Infrastructure.Network
{
    /// <summary>
    /// Spawns + drives the remote-player views for the current scene. Positions
    /// are INTERPOLATED (presence is 10 Hz — never snap per packet; Stage 12
    /// replaces the lerp with a 150 ms snapshot buffer).
    /// </summary>
    public sealed class RemotePlayerFactory
    {
        private readonly RemotePlayerRegistry _registry;

        private readonly Dictionary<string, RemotePlayerView> _views =
            new(System.StringComparer.Ordinal);

        private RemotePlayerView? _prefab;

        public RemotePlayerFactory(RemotePlayerRegistry registry)
        {
            _registry = registry;
        }

        /// <summary>RemotePlayer prefab from the current scene's LifetimeScope
        /// (set via Configure when the scene startup runs).</summary>
        public void Configure(RemotePlayerView prefab) => _prefab = prefab;

        /// <summary>Wires registry events to spawning/despawning. Call after
        /// Configure in every scene that shows remote players.</summary>
        public void Bind()
        {
            _registry.Added += OnAdded;
            _registry.Updated += OnUpdated;
            _registry.Removed += OnRemoved;
            _registry.Cleared += OnCleared;
        }

        public void Unbind()
        {
            _registry.Added -= OnAdded;
            _registry.Updated -= OnUpdated;
            _registry.Removed -= OnRemoved;
            _registry.Cleared -= OnCleared;
            OnCleared();
        }

        private void OnAdded(PlayerSnapshot snapshot)
        {
            if (_prefab is null)
            {
                Debug.LogWarning(
                    "[presence] RemotePlayer prefab not configured — remote player skipped.");
                return;
            }

            RemotePlayerView view = Object.Instantiate(_prefab);
            view.name = $"RemotePlayer_{snapshot.PlayerName}";
            view.Teleport(new Vector2(snapshot.X, snapshot.Y));
            view.SetVisuals(snapshot.PlayerName, snapshot.Facing, snapshot.Animation);
            _views[snapshot.SessionId] = view;
        }

        private void OnUpdated(PlayerSnapshot snapshot)
        {
            if (_views.TryGetValue(snapshot.SessionId, out RemotePlayerView? view) && view is { })
            {
                view.SetTarget(new Vector2(snapshot.X, snapshot.Y));
                view.SetVisuals(snapshot.PlayerName, snapshot.Facing, snapshot.Animation);
            }
        }

        private void OnRemoved(string sessionId)
        {
            if (_views.Remove(sessionId, out RemotePlayerView? view) && view is { })
            {
                Object.Destroy(view.gameObject);
            }
        }

        private void OnCleared()
        {
            foreach (KeyValuePair<string, RemotePlayerView> kv in _views)
            {
                if (kv.Value is { })
                {
                    Object.Destroy(kv.Value.gameObject);
                }
            }

            _views.Clear();
        }
    }
}
