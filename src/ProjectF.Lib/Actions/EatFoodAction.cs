using Bencodex.Types;
using Libplanet.Action;
using Libplanet.Action.State;
using Libplanet.Crypto;
using ProjectF.Lib.Exceptions;
using ProjectF.Lib.States;

namespace ProjectF.Lib.Actions;

/// <summary>
/// eat_food_v1 — consume one food item, restoring its stamina value
/// (great variant restores GreatStaminaRestore instead). Stamina stays
/// capped at MaxStamina; excess is lost.
/// </summary>
[ActionType("eat_food_v1")]
public sealed class EatFoodAction : ActionBase
{
    private int _foodItemId;

    public int FoodItemId => _foodItemId;

    public EatFoodAction()
    {
    }

    public EatFoodAction(int foodItemId)
    {
        _foodItemId = foodItemId;
    }

    public override string TypeId => "eat_food_v1";

    protected override IValue EncodePayload() => new Dictionary(new Dictionary<IKey, IValue>
    {
        [(Text)"food_item_id"] = (Integer)_foodItemId,
    });

    protected override void DecodePayload(IValue payload)
    {
        if (payload is not Dictionary dict
            || !dict.TryGetValue((Text)"food_item_id", out var foodValue)
            || foodValue is not Integer foodItemId)
        {
            throw new FailedLoadStateException(
                "EatFoodAction payload must be a Dictionary with an Integer \"food_item_id\".");
        }

        _foodItemId = (int)foodItemId;
    }

    protected override IWorld ExecuteInternal(IActionContext context)
    {
        IWorld world = context.PreviousState;
        Address signer = context.Signer;
        long blockIndex = context.BlockIndex;
        var tables = GameTables.Instance;

        // The food item must exist AND be Food category.
        var item = tables.TbItem.GetOrDefault(_foodItemId)
            ?? throw new FailedLoadStateException(
                $"Item {_foodItemId} is not in the game tables.");
        if (item.Category != Tables.ItemCategory.Food)
        {
            throw new FailedLoadStateException($"Item {_foodItemId} is not a Food.");
        }

        // Food → recipe (normal or great variant).
        Tables.Recipe? recipe = null;
        long restore = 0;
        foreach (Tables.Recipe candidate in tables.TbRecipe.DataList)
        {
            if (candidate.ResultItemId == _foodItemId)
            {
                recipe = candidate;
                restore = candidate.StaminaRestore;
                break;
            }

            if (candidate.GreatResultItemId == _foodItemId)
            {
                recipe = candidate;
                restore = candidate.GreatStaminaRestore;
                break;
            }
        }

        if (recipe is null)
        {
            throw new FailedLoadStateException(
                $"Food item {_foodItemId} has no recipe backing; cannot eat it.");
        }

        IAccount avatarAccount = GetOrCreateAccount(world, Addresses.Avatar);
        if (avatarAccount.GetState(signer) is not Dictionary avatarEncoded)
        {
            throw new FailedLoadStateException($"Avatar {signer} does not exist.");
        }

        var avatar = new AvatarState(avatarEncoded);

        IAccount inventoryAccount = GetOrCreateAccount(world, Addresses.Inventory);
        if (inventoryAccount.GetState(signer) is not Dictionary inventoryEncoded)
        {
            throw new FailedLoadStateException($"Inventory for {signer} does not exist.");
        }

        var inventory = new Inventory(inventoryEncoded);

        // Validate both sides before mutating anything.
        avatar.SyncStamina(blockIndex);
        if (inventory.GetCount(_foodItemId) < 1)
        {
            throw new ItemNotFoundException(
                $"Inventory holds no item {_foodItemId} to eat.");
        }

        inventory.RemoveOrThrow(_foodItemId, 1);
        avatar.RestoreStamina(restore);

        return world
            .SetAccount(Addresses.Avatar, avatarAccount.SetState(signer, avatar.Bencoded))
            .SetAccount(
                Addresses.Inventory, inventoryAccount.SetState(signer, inventory.Bencoded));
    }
}
