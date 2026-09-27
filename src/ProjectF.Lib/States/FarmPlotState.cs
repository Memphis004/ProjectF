using System;
using Bencodex.Types;
using ProjectF.Lib.Exceptions;

namespace ProjectF.Lib.States;

/// <summary>
/// One farm plot (3×3 grid per avatar → nine states). Lives in the farm
/// account space (Addresses.Farm) at Addresses.PlotKey(avatar, plotIndex).
///
/// knowledge.md rule 3: growth is lazy — Plant() stamps PlantedAt (the block
/// index of the planting action) and harvest-time code evaluates
/// context.BlockIndex - PlantedAt >= GrowBlocks. GrowBlocks itself comes from
/// data/seed.csv (SeedRow), not from this state.
/// </summary>
public sealed class FarmPlotState
{
    private const string KeyPlotIndex = "plot_index";
    private const string KeySeedId = "seed_id";
    private const string KeyPlantedAt = "planted_at";

    public FarmPlotState(int plotIndex)
    {
        if (plotIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(plotIndex), "Must be >= 0.");
        }

        PlotIndex = plotIndex;
        SeedId = 0;
        PlantedAt = 0;
    }

    public FarmPlotState(IValue bencoded)
    {
        if (bencoded is not Dictionary dict)
        {
            throw new FailedLoadStateException(
                "FarmPlotState bencoded value must be a Bencodex Dictionary.");
        }

        PlotIndex = (int)dict.GetValue<Integer>((Text)KeyPlotIndex);
        SeedId = (int)dict.GetValue<Integer>((Text)KeySeedId);
        PlantedAt = (long)dict.GetValue<Integer>((Text)KeyPlantedAt);
    }

    /// <summary>0-8 within the avatar's 3×3 farm grid.</summary>
    public int PlotIndex { get; }

    /// <summary>seed.csv id of the planted crop; 0 = empty plot.</summary>
    public int SeedId { get; private set; }

    /// <summary>Block index at which the seed went into the ground; 0 = empty plot.</summary>
    public long PlantedAt { get; private set; }

    /// <summary>True when nothing is planted here (SeedId == 0).</summary>
    public bool IsEmpty => SeedId == 0;

    /// <summary>
    /// Fills the empty plot.
    /// </summary>
    /// <exception cref="InvalidOperationException">Plot already planted.</exception>
    public void Plant(int seedId, long plantedAtBlockIndex)
    {
        if (!IsEmpty)
        {
            throw new InvalidOperationException(
                $"Plot {PlotIndex} already holds seed {SeedId} (planted at {PlantedAt}).");
        }

        SeedId = seedId;
        PlantedAt = plantedAtBlockIndex;
    }

    /// <summary>
    /// Empties the plot (after harvest).
    /// </summary>
    /// <exception cref="InvalidOperationException">Plot is already empty.</exception>
    public void Clear()
    {
        if (IsEmpty)
        {
            throw new InvalidOperationException($"Plot {PlotIndex} is already empty.");
        }

        SeedId = 0;
        PlantedAt = 0;
    }

    /// <summary>True when context.BlockIndex - PlantedAt >= GrowBlocks.</summary>
    public bool IsReadyToHarvest(long blockIndex, long growBlocks) =>
        !IsEmpty && blockIndex - PlantedAt >= growBlocks;

    public IValue Bencoded => new Dictionary(new Dictionary<IKey, IValue>
    {
        [(Text)KeyPlotIndex] = (Integer)PlotIndex,
        [(Text)KeySeedId] = (Integer)SeedId,
        [(Text)KeyPlantedAt] = (Integer)PlantedAt,
    });
}
