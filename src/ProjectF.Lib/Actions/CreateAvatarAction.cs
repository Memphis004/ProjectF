using Bencodex.Types;
using Libplanet.Action;
using Libplanet.Action.State;
using Libplanet.Crypto;
using ProjectF.Lib.Exceptions;
using ProjectF.Lib.States;

namespace ProjectF.Lib.Actions;

/// <summary>
/// create_avatar_v1 — one-time avatar creation for the signer.
///
/// knowledge.md rule 6 is inherent here: the avatar address IS the signer's
/// address, so ownership is unforgeable by construction. Re-creation is
/// rejected (an avatar is a permanent identity).
/// </summary>
[ActionType("create_avatar_v1")]
public sealed class CreateAvatarAction : ActionBase
{
    private string _name = "";

    public string Name => _name;

    public CreateAvatarAction()
    {
    }

    public CreateAvatarAction(string name)
    {
        _name = name;
    }

    public override string TypeId => "create_avatar_v1";

    protected override IValue EncodePayload() => new Dictionary(new Dictionary<IKey, IValue>
    {
        [(Text)"name"] = (Text)_name,
    });

    protected override void DecodePayload(IValue payload)
    {
        if (payload is not Dictionary dict
            || !dict.TryGetValue((Text)"name", out var nameValue)
            || nameValue is not Text name)
        {
            throw new FailedLoadStateException(
                "CreateAvatarAction payload must be a Dictionary with a Text \"name\".");
        }

        _name = name;
    }

    protected override IWorld ExecuteInternal(IActionContext context)
    {
        IWorld world = context.PreviousState;
        Address signer = context.Signer;

        IAccount avatarAccount = GetOrCreateAccount(world, Addresses.Avatar);
        // A real trie returns Bencodex Null for a missing key; the mock-based
        // test world returns C# null. Both mean "no avatar yet".
        if (avatarAccount.GetState(signer) is not null and not Null)
        {
            throw new InvalidOperationException(
                $"Avatar at {signer} already exists; create_avatar_v1 is one-time only.");
        }

        var avatar = new AvatarState(_name, signer);

        var inventory = new Inventory();
        inventory.Add(2001, 1); // wooden rod
        inventory.Add(1001, 5); // worm bait x5

        IAccount updatedAvatar = avatarAccount.SetState(signer, avatar.Bencoded);
        IAccount updatedInventory = GetOrCreateAccount(world, Addresses.Inventory)
            .SetState(signer, inventory.Bencoded);

        return world
            .SetAccount(Addresses.Avatar, updatedAvatar)
            .SetAccount(Addresses.Inventory, updatedInventory);
    }
}
