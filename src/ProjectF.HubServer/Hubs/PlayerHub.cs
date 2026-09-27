using MagicOnion.Server.Hubs;
using ProjectF.HubServer.Presence;
using ProjectF.Shared.Hubs;
using ProjectF.Shared.Presence;

namespace ProjectF.HubServer.Hubs;

/// <summary>
/// Presence hub: one instance per connection, grouped by scene-{id}.
/// Broadcasts stay cosmetic — nothing here mutates chain state.
/// </summary>
public sealed class PlayerHub : StreamingHubBase<IPlayerHub, IPlayerHubReceiver>, IPlayerHub
{
    private readonly PresenceRegistry _registry;

    private string _sessionId = "";
    private PlayerSnapshot _snapshot = new();
    private int _sceneId;

    public PlayerHub(PresenceRegistry registry)
    {
        _registry = registry;
    }

    private IGroup<IPlayerHubReceiver> Scene { get; set; } = null!;

    private static string GroupName(int sceneId) => $"scene-{sceneId}";

    public async ValueTask<PlayerSnapshot[]> JoinAsync(PlayerJoinRequest request)
    {
        _sessionId = Context.ContextId.ToString("N");
        _sceneId = request.SceneId;
        _snapshot = new PlayerSnapshot
        {
            SessionId = _sessionId,
            PlayerName = request.PlayerName,
            SceneId = request.SceneId,
            X = request.X,
            Y = request.Y,
            Facing = Direction.Down,
            Animation = AnimationState.Idle,
        };

        Scene = await Group.AddAsync(GroupName(request.SceneId));
        _registry.Upsert(_snapshot);

        // Tell everyone already in the scene, then hand the joiner the roster.
        Scene.All.OnJoin(_snapshot);
        return _registry.OthersIn(request.SceneId, _sessionId);
    }

    public ValueTask MoveAsync(PlayerMoveRequest request)
    {
        if (_snapshot.SessionId.Length == 0 || _snapshot.SceneId != request.SceneId)
        {
            // Not joined, or a move for a scene we are not in (stale client).
            return ValueTask.CompletedTask;
        }

        _snapshot.X = request.X;
        _snapshot.Y = request.Y;
        _snapshot.Facing = request.Facing;
        _snapshot.Animation = request.Animation;
        _registry.Upsert(_snapshot);

        Scene.All.OnMove(_snapshot);
        return ValueTask.CompletedTask;
    }

    public ValueTask EmoteAsync(int emoteId)
    {
        if (_snapshot.SessionId.Length == 0)
        {
            return ValueTask.CompletedTask;
        }

        Scene.All.OnEmote(_sessionId, emoteId);
        return ValueTask.CompletedTask;
    }

    public async ValueTask ChangeSceneAsync(int sceneId, float x, float y)
    {
        if (_snapshot.SessionId.Length == 0 || _snapshot.SceneId == sceneId)
        {
            return; // not joined yet, or nothing to change
        }

        var oldGroup = Scene;
        int oldSceneId = _sceneId;
        _snapshot.SceneId = sceneId;
        _snapshot.X = x;
        _snapshot.Y = y;
        _snapshot.Animation = AnimationState.Idle;

        // Leave the old group BEFORE joining the new one so the old group's
        // membership cleanup cannot race the new group's join broadcast.
        _registry.Remove(oldSceneId, _sessionId);
        oldGroup.All.OnLeave(_sessionId); // no ghost players in the old scene
        await oldGroup.RemoveAsync(Context);
        Scene = await Group.AddAsync(GroupName(sceneId));
        _sceneId = sceneId;
        _registry.Upsert(_snapshot);
        Scene.All.OnJoin(_snapshot);

        // Roster of the new scene goes to the CALLER ONLY: JoinAsync returns it
        // as its result, but ChangeSceneAsync has no return value, so it is
        // pushed as OnSceneSnapshot to this connection alone (Single() targets
        // one group member; All() would broadcast it to the whole scene).
        Scene.Single(ConnectionId).OnSceneSnapshot(_registry.OthersIn(sceneId, _sessionId));
    }

    public async ValueTask LeaveAsync()
    {
        if (_snapshot.SessionId.Length > 0 && Scene is { } scene)
        {
            _registry.Remove(_sceneId, _sessionId);
            await scene.RemoveAsync(Context);
            Scene.All.OnLeave(_sessionId);
            _snapshot.SessionId = ""; // mark as left; OnDisconnected becomes a no-op
        }
    }

    protected override ValueTask OnConnecting()
    {
        Console.WriteLine($"[presence] connecting {Context.ContextId:N}");
        return CompletedTask;
    }

    /// <summary>No ghost players: the server removes the player and tells the
    /// survivors whenever the connection dies — clean LeaveAsync or not.</summary>
    protected override ValueTask OnDisconnected()
    {
        if (_snapshot.SessionId.Length > 0)
        {
            _registry.Remove(_sceneId, _sessionId);
            Console.WriteLine($"[presence] disconnected {_sessionId} (scene {_sceneId})");
            // Group membership is torn down by the framework after this hook
            // returns; the remaining group members receive OnLeave here.
            Scene?.All.OnLeave(_sessionId);
            _snapshot.SessionId = "";
        }

        return CompletedTask;
    }
}
