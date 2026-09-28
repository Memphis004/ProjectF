using System.Collections.Generic;
using NUnit.Framework;
using ProjectF.Presentation.AuntieHouse;
using ProjectF.Presentation.Shop;
using ProjectF.Presentation.Village;
using UnityEngine;

// ReSharper disable CheckNamespace
namespace ProjectF.Tests
{
    /// <summary>
    /// Stage 10 EditMode tests: the pure presenter-logic classes that need no
    /// chain (spec item 6) — affordability, progress computation, stock/level
    /// gating, portion clamping. All numbers mirror data/*.csv balance.
    /// </summary>
    public sealed class Stage10LogicTests
    {
        // -----------------------------------------------------------------
        // Shop — affordability / level gating / stock / clamping / sell rate
        // -----------------------------------------------------------------

        [Test]
        public void Affordable_true_when_gold_covers_total()
        {
            Assert.IsTrue(ShopLogic.Affordable(gold: 100, price: 5, quantity: 20));
            Assert.IsTrue(ShopLogic.Affordable(gold: 100, price: 100, quantity: 1));
        }

        [Test]
        public void Affordable_false_when_gold_is_short()
        {
            Assert.IsFalse(ShopLogic.Affordable(gold: 99, price: 100, quantity: 1));
            // Worm x21 at 5g = 105 > 100.
            Assert.IsFalse(ShopLogic.Affordable(gold: 100, price: 5, quantity: 21));
        }

        [Test]
        public void LevelOk_either_skill_counts()
        {
            // shop 3 requires level 3 (mirrors the chain's fishing OR cooking).
            Assert.IsTrue(ShopLogic.LevelOk(3, fishingLevel: 3, cookingLevel: 1));
            Assert.IsTrue(ShopLogic.LevelOk(3, fishingLevel: 1, cookingLevel: 3));
            Assert.IsFalse(ShopLogic.LevelOk(3, fishingLevel: 2, cookingLevel: 2));
        }

        [Test]
        public void StockLabel_zero_means_unlimited()
        {
            Assert.AreEqual("∞", ShopLogic.StockLabel(dailyStock: 0, boughtToday: 0));
            Assert.AreEqual("20", ShopLogic.StockLabel(dailyStock: 20, boughtToday: 0));
            Assert.AreEqual("5", ShopLogic.StockLabel(dailyStock: 20, boughtToday: 15));
            // Never negative.
            Assert.AreEqual("0", ShopLogic.StockLabel(dailyStock: 20, boughtToday: 99));
        }

        [Test]
        public void ClampQuantity_stays_in_range()
        {
            Assert.AreEqual(1, ShopLogic.ClampQuantity(0, maxQty: 5));
            Assert.AreEqual(5, ShopLogic.ClampQuantity(6, maxQty: 5));
            Assert.AreEqual(3, ShopLogic.ClampQuantity(3, maxQty: 5));
            // Held-count cap: selling 1 of 0 held clamps to 1 (button stays
            // disabled via affordability/stock checks — clamp never returns 0).
            Assert.AreEqual(1, ShopLogic.ClampQuantity(3, maxQty: 0));
        }

        [Test]
        public void SellGold_matches_the_chain_action()
        {
            // Fish 3001 (30g) → 18.
            Assert.AreEqual(18, ShopLogic.SellGold(30, 1));
            // Food 7001 (55g) → 33 (floor).
            Assert.AreEqual(33, ShopLogic.SellGold(55, 1));
            // Crops multiply before the rate: 12g × 3 × 60% = 21.6 → 21.
            Assert.AreEqual(21, ShopLogic.SellGold(12, 3));
        }

        // -----------------------------------------------------------------
        // TaskBoard — progress / submit gating / reroll countdown
        // -----------------------------------------------------------------

        [Test]
        public void Progress_caps_at_the_target()
        {
            var inventory = new Dictionary<int, long> { { 3001, 2 } };
            (long have, long need) = TaskBoardLogic.Progress(inventory, 3001, 3);
            Assert.AreEqual(2, have);
            Assert.AreEqual(3, need);

            inventory[3001] = 10;
            (have, need) = TaskBoardLogic.Progress(inventory, 3001, 3);
            Assert.AreEqual(3, have); // "3/3", never "10/3"
        }

        [Test]
        public void Progress_handles_missing_items()
        {
            (long have, long need) = TaskBoardLogic.Progress(
                new Dictionary<int, long>(), 3001, 3);
            Assert.AreEqual(0, have);
            Assert.AreEqual(3, need);
        }

        [Test]
        public void CanSubmit_only_when_fully_met()
        {
            var inventory = new Dictionary<int, long> { { 3001, 3 } };
            Assert.IsTrue(TaskBoardLogic.CanSubmit(inventory, 3001, 3));
            inventory[3001] = 2;
            Assert.IsFalse(TaskBoardLogic.CanSubmit(inventory, 3001, 3));
        }

        [Test]
        public void RerollCountdown_blocks_and_minutes()
        {
            // Rerolled at block 1, now block 11 → 590 blocks left.
            Assert.AreEqual(590, TaskBoardLogic.BlocksToReroll(11, lastRerolledAt: 1));
            // 590 blocks × 2s = 1180s → ceil(1180/60) = 20 minutes.
            Assert.AreEqual(20, TaskBoardLogic.MinutesToReroll(11, lastRerolledAt: 1));
            // Never negative — the board is overdue.
            Assert.AreEqual(0, TaskBoardLogic.BlocksToReroll(5000, lastRerolledAt: 1));
            Assert.AreEqual(0, TaskBoardLogic.MinutesToReroll(5000, lastRerolledAt: 1));
            // Partial minutes round UP (a 1-block wait is still "~1 min").
            Assert.AreEqual(1, TaskBoardLogic.MinutesToReroll(599, lastRerolledAt: 0));
        }

        // -----------------------------------------------------------------
        // Kitchen — portion clamping / stamina / great chance / materials
        // -----------------------------------------------------------------

        [Test]
        public void ClampPortions_stays_between_1_and_10()
        {
            Assert.AreEqual(1, CraftLogic.ClampPortions(-5));
            Assert.AreEqual(1, CraftLogic.ClampPortions(0));
            Assert.AreEqual(7, CraftLogic.ClampPortions(7));
            Assert.AreEqual(10, CraftLogic.ClampPortions(11));
        }

        [Test]
        public void StaminaCost_scales_linearly()
        {
            // Recipe 1 (grilled fish): 3 stamina per portion.
            Assert.AreEqual(3, CraftLogic.StaminaCost(3, 1));
            Assert.AreEqual(30, CraftLogic.StaminaCost(3, 10));
        }

        [Test]
        public void GreatChance_matches_the_chain_formula()
        {
            // min(40, 5 + level*2): the cap only bites at level >= 18
            // (unreachable in-game, levels 1-10 — but the formula is honest).
            Assert.AreEqual(7, CraftLogic.GreatChance(1));
            Assert.AreEqual(15, CraftLogic.GreatChance(5));
            Assert.AreEqual(25, CraftLogic.GreatChance(10));
            Assert.AreEqual(40, CraftLogic.GreatChance(99)); // capped
        }

        [Test]
        public void HasMaterials_checks_every_line_times_portions()
        {
            var inventory = new Dictionary<int, long>
            {
                { 3001, 1 }, // fish (recipe 1 needs 1)
                { 6001, 5 }, // salt (recipe 1 needs 1)
            };
            var materials = new List<(int, int)> { (3001, 1), (6001, 1) };

            Assert.IsTrue(CraftLogic.HasMaterials(inventory, materials, portions: 1));
            Assert.IsFalse(CraftLogic.HasMaterials(inventory, materials, portions: 2));
            // Missing line fails even with the others stocked.
            Assert.IsFalse(CraftLogic.HasMaterials(
                new Dictionary<int, long> { { 3001, 5 } }, materials, 1));
        }
    }
}
