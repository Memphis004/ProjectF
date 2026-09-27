using MessagePack;

namespace ProjectF.Shared.Presence;

/// <summary>
/// Everything the presence layer broadcasts about one player. Cosmetic ONLY —
/// never put ownership/economy data here (knowledge.md rule 1).
/// </summary>
[MessagePackObject]
public sealed class PlayerSnapshot
{
    [Key(0)] public string SessionId { get; set; } = "";

    [Key(1)] public string PlayerName { get; set; } = "";

    /// <summary>Logical scene id (Luban scene table; village pond = 4).</summary>
    [Key(2)] public int SceneId { get; set; }

    [Key(3)] public float X { get; set; }

    [Key(4)] public float Y { get; set; }

    [Key(5)] public Direction Facing { get; set; }

    [Key(6)] public AnimationState Animation { get; set; }
}
