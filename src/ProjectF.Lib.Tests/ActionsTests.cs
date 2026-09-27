// Stage-4 action tests, built on TestWorld/TestActionContext (hand-rolled
// IActionContext — Libplanet.Mocks 5.5.3 ships none) and the REAL embedded
// Luban tables, so every number asserted here is the live game balance:
//   pond 1: slots 2, occupy 120, base hit 40, pool 3001-3004
//   rod 2001: discount 0 → fishing stamina cost max(1, 5-0) = 5
//   seed 4001 (tomato): grow 180, cost 2 stamina, yield 2..4 → crop 5001
//   recipe 1 (grilled fish): 1x fish 3001 + 1x salt 6001, 3 stamina,
//     restore 15, result 7001 / great 7002
//   shop 1: worm 1001 @ 5 gold, required level 1; shop 3 requires level 3
//   task 1: deliver 3x fish 3001 → 40 gold + 30 exp

using System;
using System.Collections.Generic;
using System.Linq;
using Bencodex.Types;
using Libplanet.Crypto;
using ProjectF.Lib.Actions;
using ProjectF.Lib.Exceptions;
using ProjectF.Lib.States;
using Xunit;

namespace ProjectF.Lib.Tests;

public class ActionsTests : IDisposable
{
    private const int VillagePondId = 1;
    private const int PondOccupyBlocks = 120;
    private const int TomatoGrowBlocks = 180;

    private readonly PrivateKey _key = new();
    private readonly Address _signer;
    private readonly TestWorld _world;

    public ActionsTests()
    {
        _signer = _key.Address;
        _world = new TestWorld();
        SeedStarterState(blockIndex: 1);
    }

    public void Dispose()
    {
        GameTables.Reset();
    }

    // -----------------------------------------------------------------
    // Required Stage-4 test 1: fishing consumes bait + stamina.
    // -----------------------------------------------------------------

    [Fact]
    public void Fishing_consumes_bait_and_stamina()
    {
        OccupyPond(blockIndex: 1);
        long staminaBefore = Avatar().Stamina;
        Assert.Equal(5, Inventory().GetCount(1001)); // worm x5 starter

        _world.Execute(new FishingAction(VillagePondId, 1001), Ctx(2, seed: 12345));

        Assert.Equal(4, Inventory().GetCount(1001)); // one bait consumed
        AvatarState avatar = Avatar();
        Assert.Equal(staminaBefore - 5, avatar.Stamina); // wooden rod: max(1, 5-0)
        Assert.Equal(2, avatar.StaminaUpdatedAt); // stamped at the fishing block
    }

    // -----------------------------------------------------------------
    // Required Stage-4 test 2: fishing without a pond slot throws.
    // -----------------------------------------------------------------

    [Fact]
    public void Fishing_without_pond_slot_throws()
    {
        // Pond 1 has occupancy state, but the signer holds no slot in it
        // (a second address occupies so the state exists at all).
        _world.Execute(new OccupyPondAction(VillagePondId), Ctx(1, signer: new PrivateKey().Address));

        Assert.Throws<InvalidOperationException>(
            () => _world.Execute(new FishingAction(VillagePondId, 1001), Ctx(2)));
    }

    // -----------------------------------------------------------------
    // Required Stage-4 test 3: 2-slot pond, third occupant throws
    // PondFullException.
    // -----------------------------------------------------------------

    [Fact]
    public void Third_occupant_of_full_two_slot_pond_throws_PondFullException()
    {
        var holderA = new PrivateKey();
        var holderB = new PrivateKey();
        var holderC = new PrivateKey();

        _world.Execute(new OccupyPondAction(VillagePondId), Ctx(1, signer: holderA.Address));
        _world.Execute(new OccupyPondAction(VillagePondId), Ctx(2, signer: holderB.Address));

        Assert.Throws<PondFullException>(
            () => _world.Execute(new OccupyPondAction(VillagePondId), Ctx(3, signer: holderC.Address)));

        // The failed third occupy must not have changed anything.
        var ownership = Pond();
        Assert.False(ownership.HasActiveSlot(holderC.Address, 3));
        Assert.Equal(2, ownership.ActiveCount(3));

        // And the pond frees up again after expiry (lazy release at block 200:
        // both original leases expired at 121/122).
        _world.Execute(new OccupyPondAction(VillagePondId), Ctx(200, signer: holderC.Address));
        Assert.True(Pond().HasActiveSlot(holderC.Address, 200));
        Assert.Equal(1, Pond().ActiveCount(200));
    }

    // -----------------------------------------------------------------
    // Required Stage-4 test 4: craft with missing material leaves state
    // untouched (atomicity).
    // -----------------------------------------------------------------

    [Fact]
    public void Craft_with_missing_material_leaves_state_untouched()
    {
        // Kitchen unlocked, salt stocked — but recipe 1 also needs 1x fish
        // 3001, which the starter kit does not contain.
        UnlockKitchenWithSalt(blockIndex: 1);

        IValue avatarBefore = _world.GetState(Addresses.Avatar, _signer)!;
        IValue inventoryBefore = _world.GetState(Addresses.Inventory, _signer)!;

        Assert.Throws<ItemNotFoundException>(
            () => _world.Execute(new CraftFoodAction(1, 1), Ctx(2)));

        // Byte-identical state after the failed craft — nothing mutated, not
        // even the StaminaUpdatedAt stamp.
        Assert.Equal(avatarBefore, _world.GetState(Addresses.Avatar, _signer));
        Assert.Equal(inventoryBefore, _world.GetState(Addresses.Inventory, _signer));
    }

    // -----------------------------------------------------------------
    // Required Stage-4 test 5: harvest before GrowBlocks throws.
    // -----------------------------------------------------------------

    [Fact]
    public void Harvest_before_GrowBlocks_throws()
    {
        // seed 4001 (tomato): grow 180 blocks, cost 2 stamina.
        _world.Execute(new PlantSeedAction(0, 4001), Ctx(10));
        Assert.Equal(98, Avatar().Stamina);

        // One block short of the boundary → throws.
        Assert.Throws<InvalidOperationException>(
            () => _world.Execute(new HarvestAction(0), Ctx(10 + TomatoGrowBlocks - 1)));

        // Nothing consumed, plot untouched by the failed harvest.
        var plot = Plot(0);
        Assert.False(plot.IsEmpty);
        Assert.Equal(1, Inventory().GetCount(4001)); // seed gone at plant time, not twice

        // At the boundary itself it succeeds (spec rule is >=).
        _world.Execute(new HarvestAction(0), Ctx(10 + TomatoGrowBlocks));
        Assert.True(Plot(0).IsEmpty);
        int yield = (int)Inventory().GetCount(5001);
        Assert.InRange(yield, 2, 4); // seed table yield_min..yield_max
    }

    // -----------------------------------------------------------------
    // Required Stage-4 test 6: determinism — identical inputs (same world
    // snapshot + same RandomSeed) produce byte-identical results.
    // -----------------------------------------------------------------

    [Fact]
    public void Repeated_runs_with_same_seed_produce_identical_results()
    {
        const int seed = 12345;

        (IValue avatar, IValue inventory, IValue pond, IValue board) Run(int runSeed)
        {
            var world = new TestWorld();
            SeedStarterState(blockIndex: 1, world);

            world.Execute(new OccupyPondAction(VillagePondId), Ctx(1, world: world));
            world.Execute(new FishingAction(VillagePondId, 1001), Ctx(2, world: world, seed: runSeed));
            world.Execute(new FishingAction(VillagePondId, 1001), Ctx(3, world: world, seed: runSeed));
            world.Execute(new FishingAction(VillagePondId, 1001), Ctx(4, world: world, seed: runSeed));

            return (
                world.GetState(Addresses.Avatar, _signer)!,
                world.GetState(Addresses.Inventory, _signer)!,
                world.GetState(Addresses.Pond, Addresses.PondKey(VillagePondId))!,
                world.GetState(Addresses.Farm, Addresses.PlotKey(_signer, 0))!);
        }

        var run1 = Run(seed);
        var run2 = Run(seed);

        // Byte-identical state across independent runs with the same seed.
        Assert.Equal(run1.avatar, run2.avatar);
        Assert.Equal(run1.inventory, run2.inventory);
        Assert.Equal(run1.pond, run2.pond);
        Assert.Equal(run1.board, run2.board);

        // Sanity against vacuous equality: three baits were consumed either
        // way, so the inventory state actually moved.
        var decoded = new Inventory(run1.inventory);
        Assert.Equal(2, decoded.GetCount(1001)); // 5 starter worms - 3 casts

        // A different seed changes the outcome: seed 12345 → hit draw (47%
        // hit chance, draw 45), seed 999 → miss draw (99). Exp differs
        // (catch exp vs miss exp), inventory differs (fish granted or not).
        var run3 = Run(999);
        Assert.NotEqual(run1.avatar, run3.avatar);
        Assert.NotEqual(run1.inventory, run3.inventory);
    }

    // -----------------------------------------------------------------
    // Supporting coverage: create/renew/release, owner checks, eating,
    // shop, taskboard round-trip.
    // -----------------------------------------------------------------

    [Fact]
    public void Occupy_renews_existing_slot_instead_of_throwing()
    {
        _world.Execute(new OccupyPondAction(VillagePondId), Ctx(1));
        Assert.Equal(1 + PondOccupyBlocks, Pond().Slots[_signer]);

        _world.Execute(new OccupyPondAction(VillagePondId), Ctx(50));
        Assert.Equal(50 + PondOccupyBlocks, Pond().Slots[_signer]);
    }

    [Fact]
    public void LeavePond_releases_and_cannot_release_twice()
    {
        _world.Execute(new OccupyPondAction(VillagePondId), Ctx(1));
        _world.Execute(new LeavePondAction(VillagePondId), Ctx(2));
        Assert.False(Pond().HasActiveSlot(_signer, 2));

        Assert.Throws<InvalidOperationException>(
            () => _world.Execute(new LeavePondAction(VillagePondId), Ctx(3)));
    }

    [Fact]
    public void CreateAvatar_is_one_time_and_starter_kit_is_exact()
    {
        var fresh = new TestWorld();
        var freshCtx = TestActionContext.Create(fresh.World, _signer, 1);
        fresh.Execute(new CreateAvatarAction("jun"), freshCtx);

        var avatar = new AvatarState(fresh.GetState(Addresses.Avatar, _signer)!);
        Assert.Equal("jun", avatar.Name);
        Assert.Equal(300, avatar.Gold);
        Assert.Equal(100, avatar.Stamina);
        Assert.False(avatar.KitchenUnlocked);

        var inventory = new Inventory(fresh.GetState(Addresses.Inventory, _signer)!);
        Assert.Equal(1, inventory.GetCount(2001)); // wooden rod
        Assert.Equal(5, inventory.GetCount(1001)); // worm x5

        Assert.Throws<InvalidOperationException>(
            () => fresh.Execute(new CreateAvatarAction("again"), freshCtx));
    }

    [Fact]
    public void Fishing_without_rod_throws_ItemNotFound()
    {
        OccupyPond(blockIndex: 1);
        var inventory = Inventory();
        inventory.RemoveOrThrow(2001);
        _world.SetState(Addresses.Inventory, _signer, inventory.Bencoded);

        Assert.Throws<ItemNotFoundException>(
            () => _world.Execute(new FishingAction(VillagePondId, 1001), Ctx(2)));

        // Bait untouched — the throw happens before any consumption.
        Assert.Equal(5, Inventory().GetCount(1001));
    }

    [Fact]
    public void Craft_success_consumes_and_rewards_and_can_eat_result()
    {
        UnlockKitchenWithSalt(blockIndex: 1);
        var inventory = Inventory();
        inventory.Add(3001, 1); // the missing fish — recipe 1 needs exactly 1
        _world.SetState(Addresses.Inventory, _signer, inventory.Bencoded);

        long staminaBefore = Avatar().Stamina;
        _world.Execute(new CraftFoodAction(1, 1), Ctx(2));

        inventory = Inventory();
        Assert.Equal(0, inventory.GetCount(3001)); // fish consumed
        Assert.Equal(2, inventory.GetCount(6001)); // 3 stocked - 1 used
        Assert.Equal(staminaBefore - 3, Avatar().Stamina); // recipe 1 cost

        // Either the normal (7001) or great (7002) result was produced —
        // exactly one portion total.
        Assert.Equal(1, inventory.GetCount(7001) + inventory.GetCount(7002));

        // Eating restores 15 (7001) or 30 (7002) stamina, capped at max.
        long before = Avatar().Stamina;
        _world.Execute(new EatFoodAction(7001), Ctx(3));
        AvatarState avatar = Avatar();
        Assert.True(avatar.Stamina > before || avatar.Stamina == avatar.MaxStamina);
        Assert.Equal(0, Inventory().GetCount(7001));
    }

    [Fact]
    public void BuyItem_deducts_gold_and_enforces_level()
    {
        // Shop entry 1: worm 1001 @ 5 gold, required level 1.
        long goldBefore = Avatar().Gold;
        _world.Execute(new BuyItemAction(1, 3), Ctx(2));

        Assert.Equal(goldBefore - 15, Avatar().Gold);
        Assert.Equal(5 + 3, Inventory().GetCount(1001));

        // Shop entry 3 requires level 3 → avatar (level 1) is refused.
        Assert.Throws<InvalidOperationException>(
            () => _world.Execute(new BuyItemAction(3, 1), Ctx(3)));
        Assert.Equal(goldBefore - 15, Avatar().Gold); // no gold leaked
    }

    [Fact]
    public void SubmitTask_pays_rewards_and_marks_completed_once()
    {
        // Daily board with task 1 (deliver 3x fish 3001 → 40 gold + 30 exp).
        GiveTaskBoard(1);
        var inventory = Inventory();
        inventory.Add(3001, 3);
        _world.SetState(Addresses.Inventory, _signer, inventory.Bencoded);
        long goldBefore = Avatar().Gold;

        _world.Execute(new SubmitTaskAction(1), Ctx(5));

        var avatar = Avatar();
        Assert.Equal(goldBefore + 40, avatar.Gold);
        Assert.Equal(30, avatar.FishingExp);
        Assert.Equal(0, Inventory().GetCount(3001)); // delivered
        Assert.True(TaskBoard().IsCompleted(1));

        // Double submission is refused.
        Assert.Throws<InvalidOperationException>(
            () => _world.Execute(new SubmitTaskAction(1), Ctx(6)));
    }

    [Fact]
    public void RerollTaskBoard_draws_three_level_eligible_tasks_deterministically()
    {
        // Level 1 avatar: eligible pool = tasks 1, 3, 5 (required_level 1)
        // — exactly TasksPerDay, so the board must be exactly that set.
        _world.Execute(new RerollTaskBoardAction(), Ctx(2, seed: 12345));

        var board = TaskBoard();
        Assert.Equal(new[] { 1, 3, 5 }, board.TaskIds);
        Assert.Equal(2, board.LastRerolledAt);
        Assert.All(board.TaskIds, id => Assert.False(board.IsCompleted(id)));

        // After a period passes, the same seed + same pool → same board again
        // (the draw is a pure function of seed and pool).
        _world.Execute(new RerollTaskBoardAction(), Ctx(2 + TaskBoardState.RerollPeriodBlocks, seed: 12345));
        Assert.Equal(new[] { 1, 3, 5 }, TaskBoard().TaskIds);
    }

    [Fact]
    public void RerollTaskBoard_respects_period_gate_and_flags()
    {
        // Level 9 avatar → every task eligible.
        var avatar = Avatar();
        avatar.AddFishingExp(3400); // level 9
        _world.SetState(Addresses.Avatar, _signer, avatar.Bencoded);

        _world.Execute(new RerollTaskBoardAction(), Ctx(100, seed: 7));
        Assert.Equal(100, TaskBoard().LastRerolledAt);
        Assert.Equal(3, TaskBoard().Tasks.Count);

        // 599 blocks later: not due yet → throws, board untouched.
        Assert.Throws<InvalidOperationException>(
            () => _world.Execute(new RerollTaskBoardAction(), Ctx(100 + 599)));
        Assert.Equal(100, TaskBoard().LastRerolledAt);

        // Complete one task (stock its delivery first), then verify the
        // reroll resets completion flags.
        int firstTaskId = TaskBoard().TaskIds.First();
        var firstTask = GameTables.Instance.TbTask.Get(firstTaskId);
        var inventory = Inventory();
        inventory.Add(firstTask.TargetItemId, firstTask.TargetCount);
        _world.SetState(Addresses.Inventory, _signer, inventory.Bencoded);
        _world.Execute(new SubmitTaskAction(firstTaskId), Ctx(101));
        Assert.True(TaskBoard().IsCompleted(firstTaskId));

        _world.Execute(new RerollTaskBoardAction(), Ctx(100 + 600, seed: 7));
        var rerolled = TaskBoard();
        Assert.Equal(700, rerolled.LastRerolledAt);
        Assert.All(rerolled.TaskIds, id => Assert.False(rerolled.IsCompleted(id)));
    }

    [Fact]
    public void RerollTaskBoard_is_deterministic_across_runs()
    {
        var avatar = Avatar();
        avatar.AddFishingExp(3400); // level 9 → full pool
        _world.SetState(Addresses.Avatar, _signer, avatar.Bencoded);

        (int[] ids, long stamped) Run()
        {
            var world = new TestWorld();
            SeedStarterState(blockIndex: 1, world);
            var a = new AvatarState("tester", _signer);
            a.AddFishingExp(3400);
            world.SetState(Addresses.Avatar, _signer, a.Bencoded);
            world.Execute(new RerollTaskBoardAction(), Ctx(50, world: world, seed: 424242));
            var b = new TaskBoardState(world.GetState(Addresses.TaskBoard, _signer)!);
            return (new List<int>(b.TaskIds).ToArray(), b.LastRerolledAt);
        }

        var run1 = Run();
        var run2 = Run();
        Assert.Equal(run1.ids, run2.ids);
        Assert.Equal(run1.stamped, run2.stamped);

        // 6 eligible tasks, choose 3 → C(6,3) = 20 boards; 424242 draws a real subset.
        Assert.Equal(3, run1.ids.Length);
        Assert.Equal(new HashSet<int> { run1.ids[0], run1.ids[1], run1.ids[2] }.Count, 3);
    }

    [Fact]
    public void SubmitTask_without_required_items_throws_and_leaves_state_untouched()
    {
        GiveTaskBoard(1);
        var inventory = Inventory();
        inventory.Add(3001, 2); // short of 3
        _world.SetState(Addresses.Inventory, _signer, inventory.Bencoded);

        IValue goldBefore = _world.GetState(Addresses.Avatar, _signer)!;
        IValue invBefore = _world.GetState(Addresses.Inventory, _signer)!;

        Assert.Throws<ItemNotFoundException>(
            () => _world.Execute(new SubmitTaskAction(1), Ctx(5)));

        Assert.Equal(goldBefore, _world.GetState(Addresses.Avatar, _signer));
        Assert.Equal(invBefore, _world.GetState(Addresses.Inventory, _signer));
        Assert.False(TaskBoard().IsCompleted(1));
    }

    // -----------------------------------------------------------------
    // Fixtures
    // -----------------------------------------------------------------

    private TestActionContext Ctx(
        long blockIndex,
        TestWorld? world = null,
        Address? signer = null,
        int seed = TestActionContext.DefaultRandomSeed) =>
        TestActionContext.Create(
            (world ?? _world).World,
            signer ?? _signer,
            blockIndex,
            seed);

    private void SeedStarterState(long blockIndex, TestWorld? world = null)
    {
        var target = world ?? _world;

        var avatar = new AvatarState("tester", _signer);
        var inventory = new Inventory();
        inventory.Add(2001, 1); // wooden rod
        inventory.Add(1001, 5); // worm x5
        inventory.Add(4001, 2); // tomato seeds (grow 180, cost 2)

        target.SetState(Addresses.Avatar, _signer, avatar.Bencoded);
        target.SetState(Addresses.Inventory, _signer, inventory.Bencoded);
    }

    private void OccupyPond(long blockIndex) =>
        _world.Execute(new OccupyPondAction(VillagePondId), Ctx(blockIndex));

    private void UnlockKitchenWithSalt(long blockIndex)
    {
        var avatar = Avatar();
        avatar.KitchenUnlocked = true;
        _world.SetState(Addresses.Avatar, _signer, avatar.Bencoded);

        var inventory = Inventory();
        inventory.Add(6001, 3); // salt — but no fish 3001
        _world.SetState(Addresses.Inventory, _signer, inventory.Bencoded);
    }

    private void GiveTaskBoard(params int[] taskIds)
    {
        var board = new TaskBoardState(_signer);
        board.SetTasks(taskIds, rerolledAtBlockIndex: 1);
        _world.SetState(Addresses.TaskBoard, _signer, board.Bencoded);
    }

    private AvatarState Avatar() =>
        new(_world.GetState(Addresses.Avatar, _signer)
            ?? throw new InvalidOperationException("no avatar state"));

    private Inventory Inventory() =>
        new(_world.GetState(Addresses.Inventory, _signer)
            ?? throw new InvalidOperationException("no inventory state"));

    private PondOwnershipState Pond() =>
        new(_world.GetState(Addresses.Pond, Addresses.PondKey(VillagePondId))
            ?? throw new InvalidOperationException("no pond state"));

    private FarmPlotState Plot(int index) =>
        new(_world.GetState(Addresses.Farm, Addresses.PlotKey(_signer, index))
            ?? throw new InvalidOperationException($"no plot {index} state"));

    private TaskBoardState TaskBoard() =>
        new(_world.GetState(Addresses.TaskBoard, _signer)
            ?? throw new InvalidOperationException("no taskboard state"));
}
