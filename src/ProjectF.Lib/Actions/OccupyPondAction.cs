using Bencodex.Types;
using Libplanet.Action;
using Libplanet.Action.State;
using Libplanet.Crypto;
using ProjectF.Lib.Exceptions;
using ProjectF.Lib.States;

namespace ProjectF.Lib.Actions;

/// <summary>
/// occupy_pond_v1 — claim/renew a fishing slot at a pond.
///
/// knowledge.md rule 3: expired slots are swept lazily here BEFORE any
/// capacity decision, so a full pond frees itself as soon as anyone touches
/// it after expiry. Already-held (possibly expired) slots are renewed in
/// place instead of throwing.
/// </summary>
[ActionType("occupy_pond_v1")]
public sealed class OccupyPondAction : ActionBase
{
    private int _pondId;

    public int PondId => _pondId;

    public OccupyPondAction()
    {
    }

    public OccupyPondAction(int pondId)
    {
        _pondId = pondId;
    }

    public override string TypeId => "occupy_pond_v1";

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
                "OccupyPondAction payload must be a Dictionary with an Integer \"pond_id\".");
        }

        _pondId = (int)pondId;
    }

    protected override IWorld ExecuteInternal(IActionContext context)
    {
        IWorld world = context.PreviousState;
        Address signer = context.Signer;
        long blockIndex = context.BlockIndex;

        var pond = GameTables.Instance.TbPond.GetOrDefault(_pondId)
            ?? throw new FailedLoadStateException($"Pond {_pondId} is not in the game tables.");

        IAccount pondAccount = GetOrCreateAccount(world, Addresses.Pond);
        Address pondKey = Addresses.PondKey(_pondId);
        var ownership = pondAccount.GetState(pondKey) is Dictionary encoded
            ? new PondOwnershipState(encoded)
            : new PondOwnershipState(_pondId);

        // knowledge.md rule 3: lazy expiry sweep before any decision.
        // knowledge.md rule 6: slot ownership is keyed by the signer's
        // Address inside the shared pond state — actions always pass
        // context.Signer, never a payload-controlled address.
        ownership.ReleaseExpired(blockIndex);

        if (ownership.HasActiveSlot(signer, blockIndex))
        {
            // Already held: renew the lease.
            ownership.Renew(signer, blockIndex + pond.OccupyBlocks);
        }
        else
        {
            if (ownership.OccupiedCount >= pond.SlotCount)
            {
                throw new PondFullException(
                    $"Pond {_pondId} is full ({pond.SlotCount} slots) at block {blockIndex}.");
            }

            ownership.Occupy(signer, blockIndex + pond.OccupyBlocks);
        }

        return world.SetAccount(
            Addresses.Pond, pondAccount.SetState(pondKey, ownership.Bencoded));
    }
}
