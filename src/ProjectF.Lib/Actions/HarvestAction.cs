using System;
using Bencodex.Types;
using Libplanet.Action;
using Libplanet.Action.State;
using Libplanet.Crypto;
using ProjectF.Lib.Exceptions;
using ProjectF.Lib.States;

namespace ProjectF.Lib.Actions;

/// <summary>
/// harvest_v1 — evaluate growth lazily (knowledge.md rule 3):
/// context.BlockIndex - PlantedAt must be >= GrowBlocks from data/seed.csv.
/// Yield is rolled in [YieldMin, YieldMax]; the plot is cleared.
/// </summary>
[ActionType("harvest_v1")]
public sealed class HarvestAction : ActionBase
{
    private int _plotIndex;

    public int PlotIndex => _plotIndex;

    public HarvestAction()
    {
    }

    public HarvestAction(int plotIndex)
    {
        _plotIndex = plotIndex;
    }

    public override string TypeId => "harvest_v1";

    protected override IValue EncodePayload() => new Dictionary(new Dictionary<IKey, IValue>
    {
        [(Text)"plot_index"] = (Integer)_plotIndex,
    });

    protected override void DecodePayload(IValue payload)
    {
        if (payload is not Dictionary dict
            || !dict.TryGetValue((Text)"plot_index", out var plotValue)
            || plotValue is not Integer plotIndex)
        {
            throw new FailedLoadStateException(
                "HarvestAction payload must be a Dictionary with an Integer \"plot_index\".");
        }

        _plotIndex = (int)plotIndex;
    }

    protected override IWorld ExecuteInternal(IActionContext context)
    {
        IWorld world = context.PreviousState;
        Address signer = context.Signer;
        long blockIndex = context.BlockIndex;
        var tables = GameTables.Instance;

        IAccount farmAccount = GetOrCreateAccount(world, Addresses.Farm);
        Address plotKey = Addresses.PlotKey(signer, _plotIndex);
        if (farmAccount.GetState(plotKey) is not Dictionary plotEncoded)
        {
            throw new FailedLoadStateException(
                $"Plot {_plotIndex} of {signer} was never planted.");
        }

        var plot = new FarmPlotState(plotEncoded);
        if (plot.IsEmpty)
        {
            throw new InvalidOperationException($"Plot {_plotIndex} is empty.");
        }

        Tables.Seed? seed = null;
        foreach (Tables.Seed candidate in tables.TbSeed.DataList)
        {
            if (candidate.Id == plot.SeedId)
            {
                seed = candidate;
                break;
            }
        }

        if (seed is null)
        {
            throw new FailedLoadStateException(
                $"Plot {_plotIndex} references unknown seed id {plot.SeedId}.");
        }

        // knowledge.md rule 3: the growth check IS the harvest gate.
        if (blockIndex - plot.PlantedAt < seed.GrowBlocks)
        {
            throw new InvalidOperationException(
                $"Plot {_plotIndex} needs {seed.GrowBlocks} blocks (planted at " +
                $"{plot.PlantedAt}, now {blockIndex}); {seed.GrowBlocks - (blockIndex - plot.PlantedAt)} to go.");
        }

        // Yield roll + inventory grant.
        IAccount inventoryAccount = GetOrCreateAccount(world, Addresses.Inventory);
        if (inventoryAccount.GetState(signer) is not Dictionary inventoryEncoded)
        {
            throw new FailedLoadStateException($"Inventory for {signer} does not exist.");
        }

        var inventory = new Inventory(inventoryEncoded);
        Libplanet.Action.IRandom random = context.GetRandom();
        int yield = random.Next(seed.YieldMin, seed.YieldMax + 1);
        inventory.Add(seed.CropItemId, yield);

        plot.Clear();

        return world
            .SetAccount(
                Addresses.Inventory, inventoryAccount.SetState(signer, inventory.Bencoded))
            .SetAccount(Addresses.Farm, farmAccount.SetState(plotKey, plot.Bencoded));
    }
}
