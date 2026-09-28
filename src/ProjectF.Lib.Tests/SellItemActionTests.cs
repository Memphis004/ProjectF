// Stage-10 tests for sell_item_v1, on the same scaffolding as ActionsTests
// (TestWorld/TestActionContext + the REAL embedded Luban tables):
//   item 3001 (nilotica fish): Fish, base_price 30
//   item 1001 (worm): Bait — NOT sellable
//   60% sell rate → gold = floor(basePrice * 600 * qty / 1000)

using System;
using Libplanet.Crypto;
using ProjectF.Lib.Actions;
using ProjectF.Lib.Exceptions;
using ProjectF.Lib.States;
using Xunit;

namespace ProjectF.Lib.Tests;

public class SellItemActionTests : IDisposable
{
    private readonly PrivateKey _key = new();
    private readonly Address _signer;
    private readonly TestWorld _world;

    public SellItemActionTests()
    {
        _signer = _key.Address;
        _world = new TestWorld();
        SeedStarterState();
    }

    public void Dispose()
    {
        GameTables.Reset();
    }

    [Fact]
    public void SellRate_is_sixty_percent()
    {
        Assert.Equal(600, SellItemAction.SellRatePermille);
    }

    [Fact]
    public void ComputeGold_floors_to_sixty_percent()
    {
        // 30 gold fish → 18.
        Assert.Equal(18, SellItemAction.ComputeGold(30, 1));
        // Odd prices floor: 35 * 60% = 21; 55 * 60% = 33.
        Assert.Equal(21, SellItemAction.ComputeGold(35, 1));
        Assert.Equal(33, SellItemAction.ComputeGold(55, 1));
        // Quantities multiply before the rate applies: 12 * 3 * 60% = 21.6 → 21.
        Assert.Equal(21, SellItemAction.ComputeGold(12, 3));
    }

    [Fact]
    public void Selling_fish_pays_sixty_percent_and_consumes_stock()
    {
        var inventory = Inventory();
        inventory.Add(3001, 2); // two nilotica
        _world.SetState(Addresses.Inventory, _signer, inventory.Bencoded);

        long goldBefore = Avatar().Gold;

        _world.Execute(new SellItemAction(3001, 2), Ctx(blockIndex: 2));

        Assert.Equal(0, Inventory().GetCount(3001));
        Assert.Equal(goldBefore + SellItemAction.ComputeGold(30, 2), Avatar().Gold);
    }

    [Fact]
    public void Selling_bait_is_rejected()
    {
        var before = Inventory().Bencoded;

        Assert.ThrowsAny<Exception>(
            () => _world.Execute(new SellItemAction(1001, 1), Ctx(blockIndex: 2)));

        Assert.Equal(before, Inventory().Bencoded); // atomic — untouched
    }

    [Fact]
    public void Selling_more_than_held_throws_and_leaves_state_untouched()
    {
        long goldBefore = Avatar().Gold;

        Assert.Throws<ItemNotFoundException>(
            () => _world.Execute(new SellItemAction(3001, 1), Ctx(blockIndex: 2)));

        Assert.Equal(goldBefore, Avatar().Gold);
        Assert.Equal(0, Inventory().GetCount(3001));
    }

    [Fact]
    public void Zero_or_negative_quantity_is_rejected()
    {
        Assert.ThrowsAny<Exception>(
            () => _world.Execute(new SellItemAction(3001, 0), Ctx(blockIndex: 2)));
    }

    [Fact]
    public void Foreign_signer_is_denied()
    {
        var inventory = Inventory();
        inventory.Add(3001, 1);
        _world.SetState(Addresses.Inventory, _signer, inventory.Bencoded);

        Assert.ThrowsAny<Exception>(
            () => _world.Execute(
                new SellItemAction(3001, 1),
                Ctx(blockIndex: 2, signer: new PrivateKey().Address)));
    }

    // ---------------------------------------------------------------
    // helpers (mirrors ActionsTests)
    // ---------------------------------------------------------------

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

    private void SeedStarterState()
    {
        var avatar = new AvatarState("seller", _signer);
        var inventory = new Inventory();
        inventory.Add(2001, 1); // rod
        inventory.Add(1001, 5); // worms (bait — not sellable)

        _world.SetState(Addresses.Avatar, _signer, avatar.Bencoded);
        _world.SetState(Addresses.Inventory, _signer, inventory.Bencoded);
    }

    private AvatarState Avatar() =>
        new(_world.GetState(Addresses.Avatar, _signer)
            ?? throw new InvalidOperationException("no avatar state"));

    private Inventory Inventory() =>
        new(_world.GetState(Addresses.Inventory, _signer)
            ?? throw new InvalidOperationException("no inventory state"));
}
