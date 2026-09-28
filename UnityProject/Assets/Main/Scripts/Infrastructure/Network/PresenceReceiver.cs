using System;
using ProjectF.Shared.Hubs;
using ProjectF.Shared.Presence;

// ReSharper disable CheckNamespace
namespace ProjectF.Infrastructure.Network
{
    /// <summary>
    /// Bridges MagicOnion receiver callbacks into plain C# events. Handlers
    /// must not throw (IPlayerHubReceiver contract) — the remote registry
    /// subscribes on the main thread via UniTask, never from gRPC threads
    /// directly.
    /// </summary>
    public sealed class PresenceReceiver : IPlayerHubReceiver
    {
        public event Action<PlayerSnapshot[]>? SceneSnapshot;
        public event Action<PlayerSnapshot>? Join;
        public event Action<string>? Leave;
        public event Action<PlayerSnapshot>? Move;
        public event Action<string, int>? Emote;
        public event Action<PondHintMessage>? PondHint;

        public int InboundCount { get; private set; }

        public void OnSceneSnapshot(PlayerSnapshot[] players)
        {
            InboundCount++;
            SceneSnapshot?.Invoke(players);
        }

        public void OnJoin(PlayerSnapshot player)
        {
            InboundCount++;
            Join?.Invoke(player);
        }

        public void OnLeave(string sessionId)
        {
            InboundCount++;
            Leave?.Invoke(sessionId);
        }

        public void OnMove(PlayerSnapshot player)
        {
            InboundCount++;
            Move?.Invoke(player);
        }

        public void OnEmote(string sessionId, int emoteId)
        {
            InboundCount++;
            Emote?.Invoke(sessionId, emoteId);
        }

        public void OnPondHint(PondHintMessage hint)
        {
            InboundCount++;
            PondHint?.Invoke(hint);
        }
    }
}
