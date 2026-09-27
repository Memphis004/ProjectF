using System.Collections.Concurrent;
using ProjectF.Shared.Presence;

namespace ProjectF.HubServer.Presence;

/// <summary>
/// In-memory presence state, keyed by scene. Ownership/economy state lives on
/// the chain (SeedNode); this registry is cosmetic-only and loses everything
/// on restart by design (knowledge.md rule 1: the two layers never mix).
/// </summary>
public sealed class PresenceRegistry
{
    private readonly ConcurrentDictionary<int, ConcurrentDictionary<string, PlayerSnapshot>> _scenes = new();

    private ConcurrentDictionary<string, PlayerSnapshot> GetScene(int sceneId) =>
        _scenes.GetOrAdd(sceneId, _ => new ConcurrentDictionary<string, PlayerSnapshot>());

    public void Upsert(PlayerSnapshot snapshot) =>
        GetScene(snapshot.SceneId)[snapshot.SessionId] = snapshot;

    public bool Remove(int sceneId, string sessionId) =>
        GetScene(sceneId).TryRemove(sessionId, out _);

    public PlayerSnapshot? Get(int sceneId, string sessionId) =>
        GetScene(sceneId).TryGetValue(sessionId, out var snap) ? snap : null;

    /// <summary>Everyone currently in the scene except the given session.</summary>
    public PlayerSnapshot[] OthersIn(int sceneId, string exceptSessionId) =>
        GetScene(sceneId).Values
            .Where(p => p.SessionId != exceptSessionId)
            .OrderBy(p => p.SessionId, StringComparer.Ordinal) // deterministic snapshot order
            .ToArray();

    public int Count(int sceneId) => GetScene(sceneId).Count;
}
