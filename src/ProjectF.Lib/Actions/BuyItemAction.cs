using Bencodex.Types;
using Libplanet.Action;
using Libplanet.Action.State;
using Libplanet.Crypto;
using ProjectF.Lib.Exceptions;
using ProjectF.Lib.States;

namespace ProjectF.Lib.Actions;

/// <summary>
/// buy_item_v1 — fixed-price NPC shop (GDD: no player market in Phase 1-2).
/// Deducts shop.Price gold; respects shop.RequiredLevel. DailyStock is a
/// client-side display concern in Phase 1 (the NPC restocks conceptually
/// daily); the chain enforces price, level and gold only.
/// </summary>
[ActionType("buy_item_v1")]
public sealed class BuyItemAction : ActionBase
{
    private int _shopEntryId;
    private int _quantity;

    public int ShopEntryId => _shopEntryId;
    public int Quantity => _quantity;

    public BuyItemAction()
    {
    }

    public BuyItemAction(int shopEntryId, int quantity = 1)
    {
        _shopEntryId = shopEntryId;
        _quantity = quantity;
    }

    public override string TypeId => "buy_item_v1";

    protected override IValue EncodePayload() => new Dictionary(new Dictionary<IKey, IValue>
    {
        [(Text)"shop_entry_id"] = (Integer)_shopEntryId,
        [(Text)"quantity"] = (Integer)_quantity,
    });

    protected override void DecodePayload(IValue payload)
    {
        if (payload is not Dictionary dict
            || !dict.TryGetValue((Text)"shop_entry_id", out var entryValue)
            || entryValue is not Integer shopEntryId
            || !dict.TryGetValue((Text)"quantity", out var quantityValue)
            || quantityValue is not Integer quantity)
        {
            throw new FailedLoadStateException(
                "BuyItemAction payload must be a Dictionary with Integer " +
                "\"shop_entry_id\" and \"quantity\".");
        }

        _shopEntryId = (int)shopEntryId;
        _quantity = (int)quantity;
    }

    protected override IWorld ExecuteInternal(IActionContext context)
    {
        IWorld world = context.PreviousState;
        Address signer = context.Signer;
        long blockIndex = context.BlockIndex;
        var tables = GameTables.Instance;

        if (_quantity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(_quantity), "Must be >= 1.");
        }

        var entry = tables.TbShop.GetOrDefault(_shopEntryId)
            ?? throw new FailedLoadStateException(
                $"Shop entry {_shopEntryId} is not in the game tables.");

        IAccount avatarAccount = GetOrCreateAccount(world, Addresses.Avatar);
        if (avatarAccount.GetState(signer) is not Dictionary avatarEncoded)
        {
            throw new FailedLoadStateException($"Avatar {signer} does not exist.");
        }

        var avatar = new AvatarState(avatarEncoded);
        // knowledge.md rule 6: explicit signer == avatar.Address check.
        EnsureOwner(context, avatar.Address);
        if (avatar.FishingLevel < entry.RequiredLevel && avatar.CookingLevel < entry.RequiredLevel)
        {
            throw new InvalidOperationException(
                $"Shop entry {_shopEntryId} requires level {entry.RequiredLevel}; " +
                $"avatar has fishing {avatar.FishingLevel}/cooking {avatar.CookingLevel}.");
        }

        IAccount inventoryAccount = GetOrCreateAccount(world, Addresses.Inventory);
        if (inventoryAccount.GetState(signer) is not Dictionary inventoryEncoded)
        {
            throw new FailedLoadStateException($"Inventory for {signer} does not exist.");
        }

        var inventory = new Inventory(inventoryEncoded);

        // Validate gold BEFORE touching anything.
        avatar.SyncStamina(blockIndex); // stamp regen while we're here; free side effect
        long totalCost = (long)entry.Price * _quantity;
        if (avatar.Gold < totalCost)
        {
            throw new NotEnoughGoldException(
                $"Shop entry {_shopEntryId} costs {totalCost} gold; avatar has {avatar.Gold}.");
        }

        avatar.SpendGold(totalCost);
        inventory.Add(entry.ItemId, _quantity);

        return world
            .SetAccount(Addresses.Avatar, avatarAccount.SetState(signer, avatar.Bencoded))
            .SetAccount(
                Addresses.Inventory, inventoryAccount.SetState(signer, inventory.Bencoded));
    }
}
