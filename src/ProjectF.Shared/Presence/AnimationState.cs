namespace ProjectF.Shared.Presence;

/// <summary>Animation clip selector the remote clients play. Serialized as byte.</summary>
public enum AnimationState : byte
{
    Idle = 0,
    Walk = 1,
    Fishing = 2,
    Watering = 3,
    Cooking = 4,
}
