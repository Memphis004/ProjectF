using Bencodex.Types;
using Libplanet.Action;
using Libplanet.Action.State;
using Libplanet.Crypto;
using ProjectF.Lib.Exceptions;
using ProjectF.Lib.States;

namespace ProjectF.Lib.Actions;

/// <summary>
/// unlock_kitchen_v1 — one-time kitchen unlock for the signer's avatar.
///
/// GDD 10.4: the kitchen is gated behind Auntie — the client shows "Talk to
/// Auntie first" until this action confirms. Phase-1 economics: a flat
/// 50-gold fee (the Gold check reuses the avatar's SpendGold, so a broke
/// player gets the standard NotEnoughGold mapping). Re-unlocking is a no-op
/// rejection (one-time, like create_avatar_v1).
/// </summary>
[ActionType("unlock_kitchen_v1")]
public sealed class UnlockKitchenAction : ActionBase
{
    /// <summary>Flat unlock fee (Phase 1 constant; a future stage may move
    /// it into the tables).</summary>
    public const long UnlockCostGold = 50;

    public override string TypeId => "unlock_kitchen_v1";

    protected override IValue EncodePayload() => new Dictionary(new Dictionary<IKey, IValue>());

    protected override void DecodePayload(IValue payload)
    {
        if (payload is not Dictionary)
        {
            throw new FailedLoadStateException(
                "UnlockKitchenAction payload must be a (possibly empty) Bencodex Dictionary.");
        }
    }

    protected override IWorld ExecuteInternal(IActionContext context)
    {
        IWorld world = context.PreviousState;
        Address signer = context.Signer;
        long blockIndex = context.BlockIndex;

        IAccount avatarAccount = GetOrCreateAccount(world, Addresses.Avatar);
        if (avatarAccount.GetState(signer) is not Dictionary avatarEncoded)
        {
            throw new FailedLoadStateException($"Avatar {signer} does not exist.");
        }

        var avatar = new AvatarState(avatarEncoded);
        // knowledge.md rule 6: explicit signer == avatar.Address check.
        EnsureOwner(context, avatar.Address);

        if (avatar.KitchenUnlocked)
        {
            throw new InvalidOperationException(
                $"Avatar {signer} has already unlocked the kitchen.");
        }

        avatar.SyncStamina(blockIndex); // stamp regen while we're here; free side effect
        avatar.SpendGold(UnlockCostGold);
        avatar.KitchenUnlocked = true;

        return world.SetAccount(
            Addresses.Avatar, avatarAccount.SetState(signer, avatar.Bencoded));
    }
}
