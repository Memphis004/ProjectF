using System;
using System.Threading;
using Cysharp.Threading.Tasks;

// ReSharper disable CheckNamespace
namespace ProjectF.Infrastructure.Network
{
    /// <summary>Lifecycle of the cosmetic (MagicOnion) connection.</summary>
    public enum PresenceStatus
    {
        /// <summary>Not connecting, not joined — the game runs in single-player visual mode.</summary>
        Offline = 0,
        /// <summary>ConnectAsync in flight.</summary>
        Connecting = 1,
        /// <summary>Connected and joined to a scene group.</summary>
        Online = 2,
    }

    /// <summary>
    /// Cosmetic presence facade. Everything here is ALLOWED to fail without
    /// gameplay consequence (knowledge.md rule 1: presence never decides
    /// ownership; UX contract: offline hub = single-player visual mode).
    /// <see cref="ProjectF.Infrastructure.Scene.SceneRouter"/> depends on this
    /// interface, and on <see cref="IsOnline"/> being false when the hub is
    /// unreachable, so scene loading never waits on the hub.
    /// Implementations must NEVER throw for connectivity reasons — failures
    /// surface as <see cref="PresenceStatus.Offline"/>.
    /// </summary>
    public interface IPresenceClient
    {
        PresenceStatus Status { get; }

        /// <summary>True only when connected AND joined to a scene group.
        /// SceneRouter checks this before calling ChangeSceneAsync.</summary>
        bool IsOnline { get; }

        /// <summary>Raised whenever <see cref="Status"/> changes — HUD presence dot.</summary>
        event Action<PresenceStatus>? StatusChanged;

        /// <summary>Connects to the hub and joins the initial scene group.
        /// NEVER throws for connectivity reasons — failures degrade to
        /// <see cref="PresenceStatus.Offline"/>.</summary>
        UniTask ConnectAsync(string playerName, int sceneId, float x, float y, CancellationToken ct);

        /// <summary>Leaves the old scene group and joins the new one. Called by
        /// SceneRouter ONLY after the target Unity scene is loaded and active.
        /// Must be safe to skip entirely when offline.</summary>
        UniTask ChangeSceneAsync(int sceneId, float x, float y);

        /// <summary>Position/animation broadcast — ≤10 Hz, immediate on
        /// direction/animation change (enforced by <see cref="PlayerHubClient"/>).</summary>
        void SendMove(int sceneId, float x, float y, ProjectF.Shared.Presence.Direction facing,
            ProjectF.Shared.Presence.AnimationState animation);

        /// <summary>Emote broadcast (Stage 12 adds rate limiting on the server).</summary>
        void SendEmote(int emoteId);

        /// <summary>Clean leave + disconnect (app shutdown / return to title).</summary>
        UniTask DisconnectAsync();
    }
}
