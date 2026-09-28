using System;
using ProjectF.Infrastructure.Network;
using ProjectF.Infrastructure.Scene;
using UnityEngine;
using Object = UnityEngine.Object;

// ReSharper disable CheckNamespace
namespace ProjectF.Presentation.Common
{
    /// <summary>
    /// Owns THE local player. Spawned once (from the first gameplay scene's
    /// prefab) and kept alive with DontDestroyOnLoad; every scene entry
    /// repositions it at that scene's spawn point.
    ///
    /// DELIBERATELY app-lifetime, registered once on RootLifetimeScope (NOT
    /// per scene — a per-scene registration would spawn a second player on
    /// every scene change). Scene scopes only call
    /// <see cref="BindPrefab"/> + <see cref="EnsureSpawned"/> through their
    /// presenters; the instance survives across scenes.
    /// </summary>
    public sealed class ScenePlayerService
    {
        private readonly IPresenceClient _presence;

        private PlayerView? _player;
        private PlayerView? _prefab;
        private PresenceBroadcaster? _broadcaster;

        public ScenePlayerService(IPresenceClient presence)
        {
            _presence = presence;
        }

        public PlayerView Player => _player
            ?? throw new InvalidOperationException(
                "Player not spawned yet — BindPrefab + EnsureSpawned must run first.");

        public bool HasPlayer => _player is { };

        /// <summary>Remembers THIS scene's prefab; instantiates only if the
        /// player does not exist yet (first gameplay scene wins).</summary>
        public void BindPrefab(PlayerView prefab)
        {
            _prefab = prefab;
            if (_player is null)
            {
                _player = Object.Instantiate(prefab);
                _player.name = "Player";
                Object.DontDestroyOnLoad(_player.gameObject);

                _broadcaster = _player.GetComponent<PresenceBroadcaster>();
            }
        }

        /// <summary>Places the (existing or fresh) player at the scene spawn.</summary>
        public void EnsureSpawned(Vector3 position)
        {
            if (_player is null)
            {
                throw new InvalidOperationException(
                    "ScenePlayerService has no prefab binding — call BindPrefab first.");
            }

            _player.Teleport(position);
        }

        /// <summary>Called by scene presenters with the ACTUAL SceneId of the
        /// scene that just loaded (see SceneEvents ordering).</summary>
        public void NotifySceneEntered(SceneId sceneId)
        {
            // Point the presence broadcaster at the scene we just entered;
            // SceneRouter calls ChangeSceneAsync AFTER NotifySceneLoaded, so
            // the first move in the new scene already carries the right id.
            if (_broadcaster is { })
            {
                _broadcaster.Configure(_presence, sceneId);
            }
        }

        public void Teleport(Vector3 position) =>
            _player?.Teleport(position);

        /// <summary>Stage 9: hands the app-lifetime UI focus gate to the
        /// persisted player's input controller (called by every scene's
        /// startup — the player survives scenes, the gate reference is
        /// re-pushed on every spawn just in case).</summary>
        public void PushInputGate(PlayerInputGate gate)
        {
            if (_player is { })
            {
                _player.GetComponent<PlayerInputController>()?.ConfigureGate(gate);
            }
        }
    }
}
