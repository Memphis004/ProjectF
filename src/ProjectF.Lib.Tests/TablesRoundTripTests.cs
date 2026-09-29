// Luban pipeline round-trip proof: data/*.csv --tools/gen.ps1--> *.bytes +
// Gen/*.cs  ->  new Tables(loader) reads every table back from the binary.
// This test runs the REAL generated code against the REAL generated binary,
// so any schema/data drift breaks the build before it can reach the chain.

using ProjectF.Tables;
// The generated manager class is `Tables` inside namespace `ProjectF.Tables` —
// the namespace wins unqualified resolution, so alias the class explicitly.
using TablesClass = ProjectF.Tables.Tables;
using Xunit;

namespace ProjectF.Lib.Tests;

public class TablesRoundTripTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir is not null; i++)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ProjectF.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent!;
        }

        throw new InvalidOperationException("repo root (ProjectF.sln) not found");
    }

    private static TablesClass LoadTables()
    {
        var bytesDir = Path.Combine(RepoRoot(), "UnityProject", "Assets", "StreamingAssets", "Tables");
        return new TablesClass(name =>
        {
            var path = Path.Combine(bytesDir, name + ".bytes");
            return new Luban.ByteBuf(File.ReadAllBytes(path));
        });
    }

    [Fact]
    public void Tables_load_from_binary_with_spec_row_counts()
    {
        var tables = LoadTables();

        Assert.Equal(29, tables.TbItem.DataList.Count);
        Assert.Equal(8, tables.TbFish.DataList.Count);
        Assert.Equal(3, tables.TbBait.DataList.Count);
        Assert.Equal(3, tables.TbRod.DataList.Count);
        Assert.Single(tables.TbPond.DataList);
        Assert.Equal(4, tables.TbPondFish.DataList.Count);
        Assert.Equal(3, tables.TbRecipe.DataList.Count);
        Assert.Equal(8, tables.TbRecipeMaterial.DataList.Count);
        Assert.Equal(3, tables.TbSeed.DataList.Count);
        Assert.Equal(10, tables.TbShop.DataList.Count);
        Assert.Equal(6, tables.TbTask.DataList.Count);
        Assert.Equal(10, tables.TbLevelExp.DataList.Count);
    }

    [Fact]
    public void Item_table_matches_seed_values()
    {
        var tables = LoadTables();

        var worm = tables.TbItem.Get(1001);
        Assert.Equal("ITEM_NAME_BAIT_WORM", worm.NameKey);
        Assert.Equal(ItemCategory.Bait, worm.Category);
        Assert.Equal(5, worm.BasePrice);
        Assert.True(worm.Stackable);

        var goldenCarp = tables.TbItem.Get(3008);
        Assert.Equal(ItemCategory.Fish, goldenCarp.Category);
        Assert.Equal(400, goldenCarp.BasePrice);

        var steelRod = tables.TbItem.Get(2003);
        Assert.Equal(ItemCategory.Rod, steelRod.Category);
        Assert.False(steelRod.Stackable);
    }

    [Fact]
    public void Fish_and_pond_data_survive_the_round_trip()
    {
        var tables = LoadTables();

        // golden carp: rarity 4, level 8, weight 1, exp 150
        var goldenCarp = tables.TbFish.Get(3008);
        Assert.Equal(4, goldenCarp.Rarity);
        Assert.Equal(8, goldenCarp.RequiredLevel);
        Assert.Equal(1, goldenCarp.BaseWeight);
        Assert.Equal(150, goldenCarp.ExpReward);

        // village pond: scene 4, hit 40, 2 slots, 120 blocks
        var pond = tables.TbPond.Get(1);
        Assert.Equal(4, pond.SceneId);
        Assert.Equal(40, pond.BaseHitRate);
        Assert.Equal(2, pond.SlotCount);
        Assert.Equal(120, pond.OccupyBlocks);

        // pond_fish is the relational replacement of the old fish_pool column
        var pondFish = tables.TbPondFish.DataList
            .Where(pf => pf.PondId == 1)
            .Select(pf => pf.FishId)
            .ToList();
        Assert.Equal(new[] { 3001, 3002, 3003, 3004 }, pondFish);
    }

    [Fact]
    public void LevelExp_carries_the_spec_progression()
    {
        var tables = LoadTables();

        Assert.Equal(
            new[] { 0, 50, 140, 300, 560, 950, 1500, 2300, 3400, 5000 },
            tables.TbLevelExp.DataList.OrderBy(r => r.Level).Select(r => r.RequiredExp).ToArray());
    }
}
