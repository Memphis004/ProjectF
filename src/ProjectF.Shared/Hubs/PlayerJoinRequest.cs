using MessagePack;

namespace ProjectF.Shared.Hubs;

/// <summary>Initial hub handshake: who is joining, where.</summary>
[MessagePackObject]
public sealed class PlayerJoinRequest
{
    [Key(0)] public string PlayerName { get; set; } = "";

    [Key(1)] public int SceneId { get; set; }

    [Key(2)] public float X { get; set; }

    [Key(3)] public float Y { get; set; }
}
