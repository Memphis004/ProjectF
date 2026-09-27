using MessagePack;

namespace ProjectF.Shared.Presence;

/// <summary>Server→clients hint that a pond slot freed up / about to expire.</summary>
[MessagePackObject]
public sealed class PondHintMessage
{
    [Key(0)] public int PondId { get; set; }

    /// <summary>Number of free slots after the change.</summary>
    [Key(1)] public int FreeSlots { get; set; }
}
