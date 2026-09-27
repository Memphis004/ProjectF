using Bencodex.Types;
using Libplanet.Action;
using Libplanet.Action.State;
using ProjectF.Lib.Exceptions;

namespace ProjectF.Lib.Actions;

/// <summary>
/// Minimal action proving the whole pipeline: Unity signs a transaction, the
/// seed node mines it into a block, and the counter state read back from the
/// chain visibly increases (stage-1 checkpoint, section 2b).
/// Deliberately carries no gameplay logic — it stays in the codebase forever
/// as the simplest possible end-to-end integration probe.
/// </summary>
[ActionType(StaticTypeId)]
public sealed class PingAction : ActionBase
{
    public const string StaticTypeId = "ping_v1";

    private long _increment;

    public PingAction()
        : this(1L)
    {
    }

    public PingAction(long increment)
    {
        _increment = increment;
    }

    public override string TypeId => StaticTypeId;

    protected override IValue EncodePayload() => new Dictionary(new Dictionary<IKey, IValue>
    {
        [(Text)"increment"] = (Integer)_increment,
    });

    protected override void DecodePayload(IValue payload)
    {
        if (payload is not Dictionary dict
            || !dict.TryGetValue((Text)"increment", out var incrementValue)
            || incrementValue is not Integer increment)
        {
            throw new FailedLoadStateException(
                "PingAction payload must be a Dictionary with an Integer \"increment\".");
        }

        _increment = (long)increment;
    }

    protected override IWorld ExecuteInternal(IActionContext context)
    {
        IWorld world = context.PreviousState;
        IAccount counter = GetOrCreateAccount(world, Addresses.Ping);
        long current = counter.GetState(Addresses.PingCounter) is Integer value
            ? (long)value
            : 0L;
        IAccount updated = counter.SetState(
            Addresses.PingCounter, (Integer)(current + _increment));
        return world.SetAccount(Addresses.Ping, updated);
    }
}
