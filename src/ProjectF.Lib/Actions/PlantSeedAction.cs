using Bencodex.Types;
using Libplanet.Action;
using Libplanet.Action.State;
using Libplanet.Crypto;
using ProjectF.Lib.Exceptions;
using ProjectF.Lib.States;

namespace ProjectF.Lib.Actions;

/// <summary>
/// plant_seed_v1 — consume a seed + stamina, write PlantedAt (knowledge.md
/// rule 3: growth is evaluated lazily at harvest, never scheduled).
/// </summary>
[ActionType("plant_seed_v1")]
public sealed class PlantSeedAction : ActionBase
{
    private int _plotIndex;
    private int _seedItemId;

    public int PlotIndex => _plotIndex;
    public int SeedItemId => _seedItemId;

    public PlantSeedAction()
    {
    }

    public PlantSeedAction(int plotIndex, int seedItemId)
    {
        _plotIndex = plotIndex;
        _seedItemId = seedItemId;
    }

    public override string TypeId => "plant_seed_v1";

    protected override IValue EncodePayload() => new Dictionary(new Dictionary<IKey, IValue>
    {
        [(Text)"plot_index"] = (Integer)_plotIndex,
        [(Text)"seed_item_id"] = (Integer)_seedItemId,
    });

    protected override void DecodePayload(IValue payload)
    {
        if (payload is not Dictionary dict
            || !dict.TryGetValue((Text)"plot_index", out var plotValue)
            || plotValue is not Integer plotIndex
            || !dict.TryGetValue((Text)"seed_item_id", out var seedValue)
            || seedValue is not Integer seedItemId)
        {
            throw new FailedLoadStateException(
                "PlantSeedAction payload must be a Dictionary with Integer " +
                "\"plot_index\" and \"seed_item_id\".");
        }

        _plotIndex = (int)plotIndex;
        _seedItemId = (int)seedItemId;
    }

    protected override IWorld ExecuteInternal(IActionContext context)
    {
        IWorld world = context.PreviousState;
        Address signer = context.Signer;
        long blockIndex = context.BlockIndex;
        var tables = GameTables.Instance;

        // Seed item must exist and be a Seed-category item with a seed row.
        var item = tables.TbItem.GetOrDefault(_seedItemId)
            ?? throw new FailedLoadStateException(
                $"Item {_seedItemId} is not in the game tables.");
        if (item.Category != Tables.ItemCategory.Seed)
        {
            throw new FailedLoadStateException($"Item {_seedItemId} is not a Seed.");
        }

        Tables.Seed? seed = null;
        foreach (Tables.Seed candidate in tables.TbSeed.DataList)
        {
            if (candidate.SeedItemId == _seedItemId)
            {
                seed = candidate;
                break;
            }
        }

        if (seed is null)
        {
            throw new FailedLoadStateException(
                $"Item {_seedItemId} has no seed row in the game tables.");
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

        IAccount farmAccount = GetOrCreateAccount(world, Addresses.Farm);
        Address plotKey = Addresses.PlotKey(signer, _plotIndex);
        var plot = farmAccount.GetState(plotKey) is Dictionary plotEncoded
            ? new FarmPlotState(plotEncoded)
            : new FarmPlotState(_plotIndex);
        if (!plot.IsEmpty)
        {
            throw new InvalidOperationException(
                $"Plot {_plotIndex} already holds seed {plot.SeedId}.");
        }

        // Validate: stamina + seed stock (avatar mutation is deferred to the
        // end so a failed plant cannot stamp StaminaUpdatedAt either).
        avatar.SyncStamina(blockIndex);
        if (avatar.Stamina < seed.StaminaCost)
        {
            throw new NotEnoughStaminaException(
                $"Planting seed {_seedItemId} costs {seed.StaminaCost} stamina; " +
                $"avatar has {avatar.Stamina}.");
        }

        if (inventory.GetCount(_seedItemId) < 1)
        {
            throw new ItemNotFoundException(
                $"Inventory holds no seed item {_seedItemId}.");
        }

        // Mutate.
        avatar.SpendStamina(seed.StaminaCost);
        inventory.RemoveOrThrow(_seedItemId, 1);
        plot.Plant(seed.Id, blockIndex);

        return world
            .SetAccount(Addresses.Avatar, avatarAccount.SetState(signer, avatar.Bencoded))
            .SetAccount(
                Addresses.Inventory, inventoryAccount.SetState(signer, inventory.Bencoded))
            .SetAccount(Addresses.Farm, farmAccount.SetState(plotKey, plot.Bencoded));
    }
}
