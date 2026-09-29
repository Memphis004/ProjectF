// Stage 2 data validation: parse the hand-maintained CSVs under data/ and
// enforce the seed-row contract from the spec (row counts, unique ids, and
// every cross-reference). These are the same invariants the Luban ref
// validator will enforce once the codegen pipeline runs; keeping them as unit
// tests means balance edits that break references fail before anything else.

using System.Text;
using Xunit;

namespace ProjectF.Lib.Tests;

public class TableDataTests
{
    // ---- CSV loading helpers ------------------------------------------------

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

    /// <summary>Parses one CSV line honouring double-quoted fields (e.g. pond fish_pool).</summary>
    private static string[] ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
            else if (c == '"')
            {
                inQuotes = true;
            }
            else if (c == ',')
            {
                fields.Add(sb.ToString().Trim());
                sb.Clear();
            }
            else
            {
                sb.Append(c);
            }
        }

        fields.Add(sb.ToString().Trim());
        return fields.ToArray();
    }

    /// <summary>Reads a data/*.csv and returns data rows (##-prefixed header/comment rows skipped;
    /// the leading Excel-marker column — empty in every data row — is stripped).</summary>
    private static List<string[]> Load(string file)
    {
        var path = Path.Combine(RepoRoot(), "data", file);
        return File.ReadAllLines(path)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Where(l => !l.StartsWith("##", StringComparison.Ordinal))
            .Select(ParseCsvLine)
            .Select(f => f[0] == string.Empty ? f.Skip(1).ToArray() : f)
            .ToList();
    }

    private static List<string[]> LoadDataRows(string file) => Load(file);

    // ---- item.csv -----------------------------------------------------------

    [Fact]
    public void Item_has_29_unique_rows_in_spec_ranges()
    {
        var rows = LoadDataRows("item.csv");
        Assert.Equal(29, rows.Count);

        var ids = rows.Select(r => int.Parse(r[0])).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());

        void Range(string category, int lo, int hi, int count)
        {
            var inCat = rows.Where(r => r[2] == category).ToList();
            Assert.Equal(count, inCat.Count);
            Assert.All(inCat, r =>
            {
                var id = int.Parse(r[0]);
                Assert.InRange(id, lo, hi);
            });
        }

        Range("Bait", 1001, 1003, 3);
        Range("Rod", 2001, 2003, 3);
        Range("Fish", 3001, 3008, 8);
        Range("Seed", 4001, 4003, 3);
        Range("Crop", 5001, 5003, 3);
        Range("Material", 6001, 6003, 3);
        Range("Food", 7001, 7006, 6);
    }

    // ---- fish.csv -----------------------------------------------------------

    [Fact]
    public void Fish_rows_match_spec_and_reference_existing_fish_items()
    {
        var items = LoadDataRows("item.csv").ToDictionary(r => int.Parse(r[0]), r => r[2]);
        var rows = LoadDataRows("fish.csv");
        Assert.Equal(8, rows.Count);

        var seen = new HashSet<int>();
        foreach (var r in rows)
        {
            var id = int.Parse(r[0]);
            Assert.True(seen.Add(id), $"duplicate fish id {id}");

            var itemId = int.Parse(r[1]);
            Assert.Equal("Fish", items[itemId]); // item_id must be a Fish item

            var rarity = int.Parse(r[2]);
            Assert.InRange(rarity, 1, 4);

            var weight = int.Parse(r[4]);
            Assert.InRange(weight, 1, 100);
        }

        // exactly one legendary: golden carp (level 8, weight 1, exp 150)
        var legendary = rows.Single(r => int.Parse(r[2]) == 4);
        Assert.Equal(3008, int.Parse(legendary[0]));
        Assert.Equal(8, int.Parse(legendary[3]));
        Assert.Equal(1, int.Parse(legendary[4]));
        Assert.Equal(150, int.Parse(legendary[7]));

        // the three commons sit in the 90-100 weight band
        var commons = rows.Where(r => int.Parse(r[2]) == 1).Select(r => int.Parse(r[4])).ToList();
        Assert.Equal(3, commons.Count);
        Assert.All(commons, w => Assert.InRange(w, 90, 100));
    }

    // ---- bait / rod ---------------------------------------------------------

    [Fact]
    public void Bait_and_rod_bonus_values_match_spec()
    {
        var items = LoadDataRows("item.csv").ToDictionary(r => int.Parse(r[0]), r => r[2]);

        var baits = LoadDataRows("bait.csv");
        Assert.Equal(3, baits.Count);
        Assert.Equal((5, 0), (int.Parse(baits[0][2]), int.Parse(baits[0][3])));  // worm
        Assert.Equal((12, 3), (int.Parse(baits[1][2]), int.Parse(baits[1][3]))); // shrimp
        Assert.Equal((20, 10), (int.Parse(baits[2][2]), int.Parse(baits[2][3]))); // gold lure
        Assert.All(baits, r => Assert.Equal("Bait", items[int.Parse(r[1])]));

        var rods = LoadDataRows("rod.csv");
        Assert.Equal(3, rods.Count);
        Assert.Equal((0, 0, 0), (int.Parse(rods[0][2]), int.Parse(rods[0][3]), int.Parse(rods[0][4])));
        Assert.Equal((10, 2, 0), (int.Parse(rods[1][2]), int.Parse(rods[1][3]), int.Parse(rods[1][4])));
        Assert.Equal((18, 6, 1), (int.Parse(rods[2][2]), int.Parse(rods[2][3]), int.Parse(rods[2][4])));
        Assert.All(rods, r => Assert.Equal("Rod", items[int.Parse(r[1])]));

        // starter kit from the spec (create_avatar_v1): wooden rod + worms must exist
        Assert.Contains(1001, items.Keys);
        Assert.Contains(2001, items.Keys);
    }

    // ---- pond.csv -----------------------------------------------------------

    [Fact]
    public void Pond_matches_spec_and_pond_fish_relation_is_valid()
    {
        var fishIds = LoadDataRows("fish.csv").Select(r => int.Parse(r[0])).ToHashSet();
        var ponds = LoadDataRows("pond.csv");

        var village = ponds.Single();
        Assert.Equal(4, int.Parse(village[2]));   // scene_id = farm plot
        Assert.Equal(40, int.Parse(village[4]));  // base_hit_rate
        Assert.Equal(2, int.Parse(village[5]));   // slot_count
        Assert.Equal(120, int.Parse(village[6])); // occupy_blocks

        // fish lists live in pond_fish.csv (Luban csv cells cannot contain commas)
        var pondIds = ponds.Select(r => int.Parse(r[0])).ToHashSet();
        var relations = LoadDataRows("pond_fish.csv");
        Assert.NotEmpty(relations);
        var seenPairs = new HashSet<(int, int)>();
        foreach (var r in relations)
        {
            var pondId = int.Parse(r[1]);
            var fishId = int.Parse(r[2]);
            Assert.Contains(pondId, pondIds);
            Assert.Contains(fishId, fishIds);
            Assert.True(seenPairs.Add((pondId, fishId)), "duplicate pond/fish pair");
        }

        var villagePool = relations
            .Where(r => int.Parse(r[1]) == 1)
            .Select(r => int.Parse(r[2]))
            .ToList();
        Assert.Equal(new[] { 3001, 3002, 3003, 3004 }, villagePool);
    }

    // ---- recipes ------------------------------------------------------------

    [Fact]
    public void Recipes_and_materials_are_consistent()
    {
        var items = LoadDataRows("item.csv").ToDictionary(r => int.Parse(r[0]), r => r[2]);
        var recipes = LoadDataRows("recipe.csv");
        Assert.Equal(3, recipes.Count);

        var recipeIds = new HashSet<int>();
        foreach (var r in recipes)
        {
            Assert.True(recipeIds.Add(int.Parse(r[0])), "duplicate recipe id");
            Assert.Equal("Food", items[int.Parse(r[4])]); // result
            Assert.Equal("Food", items[int.Parse(r[5])]); // great result

            var restore = int.Parse(r[7]);
            var greatRestore = int.Parse(r[8]);
            Assert.True(greatRestore > restore, "great variant must restore more stamina");
        }

        var materials = LoadDataRows("recipe_material.csv");
        Assert.Equal(8, materials.Count);

        var seenPairs = new HashSet<(int, int)>();
        foreach (var m in materials)
        {
            var recipeId = int.Parse(m[1]);
            var itemId = int.Parse(m[2]);
            Assert.Contains(recipeId, recipeIds);
            Assert.Contains(itemId, items.Keys);
            Assert.True(int.Parse(m[3]) > 0, "material count must be positive");
            Assert.True(seenPairs.Add((recipeId, itemId)), "duplicate material in recipe");
        }

        // fish soup (recipe 2) is the 3-material recipe from the spec
        Assert.Equal(3, materials.Count(m => int.Parse(m[1]) == 2));
        // every recipe has at least 2 materials
        Assert.All(recipeIds, id => Assert.True(materials.Count(m => int.Parse(m[1]) == id) >= 2));
    }

    // ---- seed / shop / task -------------------------------------------------

    [Fact]
    public void Seeds_reference_valid_items_and_positive_grow_times()
    {
        var items = LoadDataRows("item.csv").ToDictionary(r => int.Parse(r[0]), r => r[2]);
        var seeds = LoadDataRows("seed.csv");
        Assert.Equal(3, seeds.Count);

        foreach (var s in seeds)
        {
            Assert.Equal("Seed", items[int.Parse(s[1])]);
            Assert.Equal("Crop", items[int.Parse(s[2])]);
            Assert.True(int.Parse(s[3]) > 0, "grow_blocks must be positive");
            Assert.True(int.Parse(s[4]) > 0, "stamina_cost must be positive");
            Assert.InRange(int.Parse(s[6]), int.Parse(s[5]), int.MaxValue); // yield_max >= yield_min
        }
    }

    [Fact]
    public void Shop_prices_match_item_base_price()
    {
        var basePrice = LoadDataRows("item.csv").ToDictionary(r => int.Parse(r[0]), r => int.Parse(r[3]));
        var shop = LoadDataRows("shop.csv");
        Assert.Equal(10, shop.Count);

        foreach (var s in shop)
        {
            var itemId = int.Parse(s[1]);
            Assert.Contains(itemId, basePrice.Keys);
            Assert.Equal(basePrice[itemId], int.Parse(s[2])); // fixed-price NPC shop
            Assert.True(int.Parse(s[3]) >= 0);                // 0 = unlimited
            Assert.True(int.Parse(s[4]) >= 1);
        }
    }

    [Fact]
    public void Tasks_have_two_per_type_and_valid_targets()
    {
        var items = LoadDataRows("item.csv").ToDictionary(r => int.Parse(r[0]), r => r[2]);
        var tasks = LoadDataRows("task.csv");
        Assert.Equal(6, tasks.Count);
        Assert.Equal(2, tasks.Count(t => t[1] == "DeliverFish"));
        Assert.Equal(2, tasks.Count(t => t[1] == "DeliverFood"));
        Assert.Equal(2, tasks.Count(t => t[1] == "DeliverCrop"));

        var expectedCategory = new Dictionary<string, string>
        {
            ["DeliverFish"] = "Fish",
            ["DeliverFood"] = "Food",
            ["DeliverCrop"] = "Crop",
        };

        foreach (var t in tasks)
        {
            Assert.Equal(expectedCategory[t[1]], items[int.Parse(t[2])]);
            Assert.True(int.Parse(t[3]) > 0, "target_count must be positive");
            Assert.True(int.Parse(t[5]) > 0, "reward_gold must be positive");
            Assert.True(int.Parse(t[6]) > 0, "reward_exp must be positive");

            var rewardItem = int.Parse(t[7]);
            var rewardCount = int.Parse(t[8]);
            if (rewardItem == 0)
            {
                Assert.Equal(0, rewardCount);
            }
            else
            {
                Assert.Contains(rewardItem, items.Keys);
                Assert.True(rewardCount > 0);
            }
        }
    }

    // ---- level_exp.csv ------------------------------------------------------

    [Fact]
    public void LevelExp_covers_levels_1_to_10_with_spec_values()
    {
        var rows = LoadDataRows("level_exp.csv");
        Assert.Equal(10, rows.Count);
        Assert.Equal(
            new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 },
            rows.Select(r => int.Parse(r[0])).ToArray());
        Assert.Equal(
            new[] { 0, 50, 140, 300, 560, 950, 1500, 2300, 3400, 5000 },
            rows.Select(r => int.Parse(r[1])).ToArray());
    }
}
