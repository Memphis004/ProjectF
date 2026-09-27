using System;
using System.Collections.Generic;
using Bencodex.Types;
using Libplanet.Action;
using Libplanet.Action.State;
using Libplanet.Crypto;
using ProjectF.Lib.Exceptions;
using ProjectF.Lib.States;

namespace ProjectF.Lib.Actions;

/// <summary>
/// craft_food_v1 — cook at Auntie's kitchen.
///
/// Spec flow: require KitchenUnlocked → Portions 1..10 → validate cooking
/// level → spend recipe.StaminaCost * Portions → remove ALL materials
/// atomically (checked first, then removed — any miss throws before a single
/// material is touched) → per portion roll min(40, 5 + cookingLevel*2)% for
/// the "great" variant.
/// </summary>
[ActionType("craft_food_v1")]
public sealed class CraftFoodAction : ActionBase
{
    private int _recipeId;
    private int _portions;

    public int RecipeId => _recipeId;
    public int Portions => _portions;

    public CraftFoodAction()
    {
    }

    public CraftFoodAction(int recipeId, int portions)
    {
        _recipeId = recipeId;
        _portions = portions;
    }

    public override string TypeId => "craft_food_v1";

    protected override IValue EncodePayload() => new Dictionary(new Dictionary<IKey, IValue>
    {
        [(Text)"recipe_id"] = (Integer)_recipeId,
        [(Text)"portions"] = (Integer)_portions,
    });

    protected override void DecodePayload(IValue payload)
    {
        if (payload is not Dictionary dict
            || !dict.TryGetValue((Text)"recipe_id", out var recipeValue)
            || recipeValue is not Integer recipeId
            || !dict.TryGetValue((Text)"portions", out var portionsValue)
            || portionsValue is not Integer portions)
        {
            throw new FailedLoadStateException(
                "CraftFoodAction payload must be a Dictionary with Integer " +
                "\"recipe_id\" and \"portions\".");
        }

        _recipeId = (int)recipeId;
        _portions = (int)portions;
    }

    protected override IWorld ExecuteInternal(IActionContext context)
    {
        IWorld world = context.PreviousState;
        Address signer = context.Signer;
        long blockIndex = context.BlockIndex;
        var tables = GameTables.Instance;

        var recipe = tables.TbRecipe.GetOrDefault(_recipeId)
            ?? throw new FailedLoadStateException(
                $"Recipe {_recipeId} is not in the game tables.");

        if (_portions is < 1 or > 10)
        {
            throw new ArgumentOutOfRangeException(
                nameof(_portions), "Portions must be between 1 and 10.");
        }

        IAccount avatarAccount = GetOrCreateAccount(world, Addresses.Avatar);
        if (avatarAccount.GetState(signer) is not Dictionary avatarEncoded)
        {
            throw new FailedLoadStateException($"Avatar {signer} does not exist.");
        }

        var avatar = new AvatarState(avatarEncoded);
        if (!avatar.KitchenUnlocked)
        {
            throw new PermissionDeniedException(
                $"Avatar {signer} has not unlocked the kitchen yet.");
        }

        if (avatar.CookingLevel < recipe.RequiredLevel)
        {
            throw new InvalidOperationException(
                $"Cooking level {avatar.CookingLevel} < recipe requirement " +
                $"{recipe.RequiredLevel}.");
        }

        IAccount inventoryAccount = GetOrCreateAccount(world, Addresses.Inventory);
        if (inventoryAccount.GetState(signer) is not Dictionary inventoryEncoded)
        {
            throw new FailedLoadStateException($"Inventory for {signer} does not exist.");
        }

        var inventory = new Inventory(inventoryEncoded);

        // Atomic material check FIRST: no state mutation below this line may
        // happen unless every material is present in full.
        List<Tables.RecipeMaterial> materials = new();
        foreach (Tables.RecipeMaterial material in tables.TbRecipeMaterial.DataList)
        {
            if (material.RecipeId == _recipeId)
            {
                materials.Add(material);
            }
        }

        if (materials.Count == 0)
        {
            throw new FailedLoadStateException(
                $"Recipe {_recipeId} has no materials in the game tables.");
        }

        foreach (Tables.RecipeMaterial material in materials)
        {
            long required = (long)material.Count * _portions;
            if (inventory.GetCount(material.ItemId) < required)
            {
                throw new ItemNotFoundException(
                    $"Recipe {_recipeId} needs {required}x item {material.ItemId}; " +
                    $"inventory holds {inventory.GetCount(material.ItemId)}.");
            }
        }

        // Stamina next (checked, not yet saved).
        avatar.SyncStamina(blockIndex);
        avatar.SpendStamina((long)recipe.StaminaCost * _portions);

        // Everything validated — NOW mutate.
        foreach (Tables.RecipeMaterial material in materials)
        {
            inventory.RemoveOrThrow(material.ItemId, (long)material.Count * _portions);
        }

        // Per-portion "great" roll.
        int cookingLevel = avatar.CookingLevel;
        int greatChance = Math.Min(40, 5 + cookingLevel * 2);
        Libplanet.Action.IRandom random = context.GetRandom();
        int greatCount = 0;
        for (var i = 0; i < _portions; i++)
        {
            if (random.Next(100) < greatChance)
            {
                greatCount++;
            }
        }

        int normalCount = _portions - greatCount;
        if (normalCount > 0)
        {
            inventory.Add(recipe.ResultItemId, normalCount);
        }

        if (greatCount > 0)
        {
            inventory.Add(recipe.GreatResultItemId, greatCount);
        }

        avatar.AddCookingExp((long)recipe.ExpReward * _portions);

        return world
            .SetAccount(
                Addresses.Avatar,
                avatarAccount.SetState(signer, avatar.Bencoded))
            .SetAccount(
                Addresses.Inventory,
                inventoryAccount.SetState(signer, inventory.Bencoded));
    }
}
