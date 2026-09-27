using MessagePack;

namespace ProjectF.Shared.Presence;

/// <summary>Sent by the client at ≤10 Hz (presence layer, never per-frame).</summary>
[MessagePackObject]
public sealed class PlayerMoveRequest
{
    [Key(0)] public int SceneId { get; set; }

    [Key(1)] public float X { get; set; }

    [Key(2)] public float Y { get; set; }

    [Key(3)] public Direction Facing { get; set; }

    [Key(4)] public AnimationState Animation { get; set; }
}
