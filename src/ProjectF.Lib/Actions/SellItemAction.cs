using Bencodex.Types;
using Libplanet.Action;
using Libplanet.Action.State;
using Libplanet.Crypto;
using ProjectF.Lib.Exceptions;
using ProjectF.Lib.States;

namespace ProjectF.Lib.Actions;

/// <summary>
/// sell_item_v1 — sell Fish/Crop/Food back to an NPC at base_price with a
/// 60% sell rate (GDD 2.4: shops buy at a discount, no player market in
/// Phase 1-2).
///
/// Deterministic floor rounding: gold = floor(basePrice * SellRate * quantity).
/// Bait/Rod/Seed/Material are NOT sellable (they are inputs, not outputs).
/// No RNG needed — knowledge.md rule 2 determinism is trivially preserved.
/// </summary>
[ActionType("sell_item_v1")]
public sealed class SellItemAction : ActionBase
{
    /// <summary>Portion of base_price the NPC pays (GDD 2.4: 60%).</summary>
    public const long SellRatePermille = 600;

    private int _itemId;
    private int _quantity;

    public int ItemId => _itemId;
    public int Quantity => _quantity;

    public SellItemAction()
    {
    }

    public SellItemAction(int itemId, int quantity = 1)
    {
        _itemId = itemId;
        _quantity = quantity;
    }

    public override string TypeId => "sell_item_v1";

    /// <summary>Pure gold computation — shared with client previews and tests.</summary>
    public static long ComputeGold(int basePrice, int quantity) =>
        (long)basePrice * quantity * SellRatePermille / 1000;

    protected override IValue EncodePayload() => new Dictionary(new Dictionary<IKey, IValue>
    {
        [(Text)"item_id"] = (Integer)_itemId,
        [(Text)"quantity"] = (Integer)_quantity,
    });

    protected override void DecodePayload(IValue payload)
    {
        if (payload is not Dictionary dict
            || !dict.TryGetValue((Text)"item_id", out var itemValue)
            || itemValue is not Integer itemId
            || !dict.TryGetValue((Text)"quantity", out var quantityValue)
            || quantityValue is not Integer quantity)
        {
            throw new FailedLoadStateException(
                "SellItemAction payload must be a Dictionary with Integer " +
                "\"item_id\" and \"quantity\".");
        }

        _itemId = (int)itemId;
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

        var item = tables.TbItem.GetOrDefault(_itemId)
            ?? throw new FailedLoadStateException(
                $"Item {_itemId} is not in the game tables.");

        if (item.Category != Tables.ItemCategory.Fish
            && item.Category != Tables.ItemCategory.Crop
            && item.Category != Tables.ItemCategory.Food)
        {
            throw new InvalidOperationException(
                $"Item {_itemId} (category {item.Category}) is not sellable; " +
                "only Fish/Crop/Food can be sold.");
        }

        IAccount avatarAccount = GetOrCreateAccount(world, Addresses.Avatar);
        if (avatarAccount.GetState(signer) is not Dictionary avatarEncoded)
        {
            throw new FailedLoadStateException($"Avatar {signer} does not exist.");
        }

        var avatar = new AvatarState(avatarEncoded);
        // knowledge.md rule 6: explicit signer == avatar.Address check.
        EnsureOwner(context, avatar.Address);

        IAccount inventoryAccount = GetOrCreateAccount(world, Addresses.Inventory);
        if (inventoryAccount.GetState(signer) is not Dictionary inventoryEncoded)
        {
            throw new FailedLoadStateException($"Inventory for {signer} does not exist.");
        }

        var inventory = new Inventory(inventoryEncoded);

        // Validate stock FIRST — atomic, nothing mutates on failure.
        if (inventory.GetCount(_itemId) < _quantity)
        {
            throw new ItemNotFoundException(
                $"Inventory holds {inventory.GetCount(_itemId)} of item {_itemId}, " +
                $"needed {_quantity}.");
        }

        long gold = ComputeGold(item.BasePrice, _quantity);

        inventory.RemoveOrThrow(_itemId, _quantity);
        avatar.AddGold(gold);
        avatar.SyncStamina(blockIndex); // stamp regen; no stamina is spent here

        return world
            .SetAccount(Addresses.Avatar, avatarAccount.SetState(signer, avatar.Bencoded))
            .SetAccount(
                Addresses.Inventory, inventoryAccount.SetState(signer, inventory.Bencoded));
    }
}
