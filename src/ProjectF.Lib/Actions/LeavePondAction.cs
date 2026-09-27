using Bencodex.Types;
using Libplanet.Action;
using Libplanet.Action.State;
using Libplanet.Crypto;
using ProjectF.Lib.Exceptions;
using ProjectF.Lib.States;

namespace ProjectF.Lib.Actions;

/// <summary>
/// leave_pond_v1 — voluntary slot release. Releasing a slot one does not
/// hold is rejected so the state root can never be gamed with no-op txs.
/// </summary>
[ActionType("leave_pond_v1")]
public sealed class LeavePondAction : ActionBase
{
    private int _pondId;

    public int PondId => _pondId;

    public LeavePondAction()
    {
    }

    public LeavePondAction(int pondId)
    {
        _pondId = pondId;
    }

    public override string TypeId => "leave_pond_v1";

    protected override IValue EncodePayload() => new Dictionary(new Dictionary<IKey, IValue>
    {
        [(Text)"pond_id"] = (Integer)_pondId,
    });

    protected override void DecodePayload(IValue payload)
    {
        if (payload is not Dictionary dict
            || !dict.TryGetValue((Text)"pond_id", out var pondValue)
            || pondValue is not Integer pondId)
        {
            throw new FailedLoadStateException(
                "LeavePondAction payload must be a Dictionary with an Integer \"pond_id\".");
        }

        _pondId = (int)pondId;
    }

    protected override IWorld ExecuteInternal(IActionContext context)
    {
        IWorld world = context.PreviousState;
        Address signer = context.Signer;

        IAccount pondAccount = GetOrCreateAccount(world, Addresses.Pond);
        Address pondKey = Addresses.PondKey(_pondId);
        if (pondAccount.GetState(pondKey) is not Dictionary encoded)
        {
            throw new FailedLoadStateException($"Pond {_pondId} has no occupancy state yet.");
        }

        var ownership = new PondOwnershipState(encoded);
        if (!ownership.Release(signer))
        {
            throw new InvalidOperationException(
                $"Signer {signer} holds no slot at pond {_pondId}.");
        }

        return world.SetAccount(
            Addresses.Pond, pondAccount.SetState(pondKey, ownership.Bencoded));
    }
}
