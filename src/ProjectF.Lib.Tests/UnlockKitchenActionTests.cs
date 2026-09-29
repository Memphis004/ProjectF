// Tests for unlock_kitchen_v1 — same scaffolding as SellItemActionTests
// (TestWorld/TestActionContext + the REAL embedded Luban tables).

using System;
using Libplanet.Crypto;
using ProjectF.Lib.Actions;
using ProjectF.Lib.Exceptions;
using ProjectF.Lib.States;
using Xunit;

namespace ProjectF.Lib.Tests;

public class UnlockKitchenActionTests : IDisposable
{
    private readonly PrivateKey _key = new();
    private readonly Address _signer;
    private readonly TestWorld _world;

    public UnlockKitchenActionTests()
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
    public void Unlock_deducts_fee_and_sets_flag()
    {
        long goldBefore = Avatar().Gold;

        _world.Execute(new UnlockKitchenAction(), Ctx(blockIndex: 2));

        Assert.True(Avatar().KitchenUnlocked);
        Assert.Equal(goldBefore - UnlockKitchenAction.UnlockCostGold, Avatar().Gold);
    }

    [Fact]
    public void Unlock_without_enough_gold_throws_and_is_atomic()
    {
        var avatar = Avatar();
        avatar.SpendGold(avatar.Gold); // drain to zero
        _world.SetState(Addresses.Avatar, _signer, avatar.Bencoded);

        Assert.Throws<NotEnoughGoldException>(
            () => _world.Execute(new UnlockKitchenAction(), Ctx(blockIndex: 2)));

        Assert.False(Avatar().KitchenUnlocked);
    }

    [Fact]
    public void Double_unlock_is_rejected()
    {
        _world.Execute(new UnlockKitchenAction(), Ctx(blockIndex: 2));

        Assert.ThrowsAny<Exception>(
            () => _world.Execute(new UnlockKitchenAction(), Ctx(blockIndex: 3)));
    }

    [Fact]
    public void Unlock_without_avatar_is_rejected()
    {
        var other = new TestWorld();

        Assert.ThrowsAny<Exception>(
            () => other.Execute(
                new UnlockKitchenAction(),
                TestActionContext.Create(other.World, new PrivateKey().Address, 1)));
    }

    [Fact]
    public void Foreign_signer_is_denied()
    {
        Assert.ThrowsAny<Exception>(
            () => _world.Execute(
                new UnlockKitchenAction(),
                Ctx(blockIndex: 2, signer: new PrivateKey().Address)));
    }

    // ---------------------------------------------------------------
    // helpers (mirrors SellItemActionTests)
    // ---------------------------------------------------------------

    private TestActionContext Ctx(
        long blockIndex,
        TestWorld? world = null,
        Address? signer = null) =>
        TestActionContext.Create(
            (world ?? _world).World,
            signer ?? _signer,
            blockIndex);

    private void SeedStarterState()
    {
        var avatar = new AvatarState("cook", _signer);
        var inventory = new Inventory();
        inventory.Add(2001, 1); // rod
        inventory.Add(1001, 5); // worms

        _world.SetState(Addresses.Avatar, _signer, avatar.Bencoded);
        _world.SetState(Addresses.Inventory, _signer, inventory.Bencoded);
    }

    private AvatarState Avatar() =>
        new(_world.GetState(Addresses.Avatar, _signer)
            ?? throw new InvalidOperationException("no avatar state"));
}
