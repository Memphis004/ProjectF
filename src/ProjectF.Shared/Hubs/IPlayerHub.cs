using MagicOnion;
using ProjectF.Shared.Presence;

namespace ProjectF.Shared.Hubs;

/// <summary>Server → client callbacks. Implement in the client, pass to
/// StreamingHubClient.ConnectAsync. Handlers must not throw.</summary>
public interface IPlayerHubReceiver
{
    void OnSceneSnapshot(PlayerSnapshot[] players);

    void OnJoin(PlayerSnapshot player);

    void OnLeave(string sessionId);

    void OnMove(PlayerSnapshot player);

    void OnEmote(string sessionId, int emoteId);

    void OnPondHint(PondHintMessage hint);
}

/// <summary>Client → server API (streaming hub), grouped per scene.
/// NB: StreamingHub methods must return Task/ValueTask forms — UnaryResult is
/// only valid on plain unary services (MagicOnion 7 rejects it at mapping).</summary>
public interface IPlayerHub : IStreamingHub<IPlayerHub, IPlayerHubReceiver>
{
    /// <summary>Joins the hub for request.SceneId; returns who is already there.</summary>
    ValueTask<PlayerSnapshot[]> JoinAsync(PlayerJoinRequest request);

    /// <summary>Broadcasts the move to the scene group (≤10 Hz from the client).</summary>
    ValueTask MoveAsync(PlayerMoveRequest request);

    ValueTask EmoteAsync(int emoteId);

    /// <summary>Leaves the old scene group and joins the new one.</summary>
    ValueTask ChangeSceneAsync(int sceneId, float x, float y);

    ValueTask LeaveAsync();
}
