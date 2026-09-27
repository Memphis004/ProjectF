// Stage 3 gate: every state's Bencodex encoding must round-trip losslessly.
// For each state X: new X(x.Bencoded).Bencoded must equal x.Bencoded byte for
// byte (Bencodex IValue equality is structural). A divergence here means the
// decoder drops, renames or re-shapes a field — which would corrupt state the
// moment a node re-reads what it (or a peer) wrote.

using System;
using System.Linq;
using Bencodex.Types;
using Libplanet.Crypto;
using ProjectF.Lib.States;
using Xunit;

namespace ProjectF.Lib.Tests;

public class StatesRoundTripTests
{
    // ------------------------------------------------------------------
    // new X(x.Bencoded).Bencoded == x.Bencoded — the literal stage-3 gate.
    // ------------------------------------------------------------------

    [Fact]
    public void AvatarState_bencodes_and_decodes_losslessly()
    {
        var avatar = new AvatarState("farmer_jun", new PrivateKey().Address)
        {
            MaxStamina = 120,
            KitchenUnlocked = true,
        };
        avatar.SyncStamina(40); // regen + stamp StaminaUpdatedAt
        avatar.AddFishingExp(140);
        avatar.AddCookingExp(55);
        avatar.AddGold(12345);

        AssertRoundTrip(avatar);
    }

    [Fact]
    public void Inventory_bencodes_and_decodes_losslessly()
    {
        var inventory = new Inventory();
        inventory.Add(1001, 5);       // worm
        inventory.Add(2001, 1);       // wooden rod
        inventory.Add(3008, 42);
        inventory.Add(5002, 1_000_000_000_000L); // long counts survive too

        AssertRoundTrip(inventory);
    }

    [Fact]
    public void FarmPlotState_bencodes_and_decodes_losslessly()
    {
        var plot = new FarmPlotState(4);
        plot.Plant(4002, 987654321);

        AssertRoundTrip(plot);
    }

    [Fact]
    public void PondOwnershipState_bencodes_and_decodes_losslessly()
    {
        var pond = new PondOwnershipState(1);
        pond.Occupy(new PrivateKey().Address, expiresAtBlock: 100);
        pond.Occupy(new PrivateKey().Address, expiresAtBlock: long.MaxValue);

        AssertRoundTrip(pond);
    }

    [Fact]
    public void TaskBoardState_bencodes_and_decodes_losslessly()
    {
        var taskBoard = new TaskBoardState(new PrivateKey().Address);
        taskBoard.SetTasks(new[] { 3, 1, 5 }, rerolledAtBlockIndex: 777);
        taskBoard.MarkCompleted(5);

        AssertRoundTrip(taskBoard);
    }

    // ------------------------------------------------------------------
    // Freshly-constructed states must also survive: decode(encode(x))
    // reproduces every field value, not just the bytes.
    // ------------------------------------------------------------------

    [Fact]
    public void AvatarState_decoded_fields_match_original()
    {
        Address address = new PrivateKey().Address;
        var avatar = new AvatarState("ดารา", address)
        {
            MaxStamina = 150,
            KitchenUnlocked = true,
        };
        avatar.SyncStamina(25);
        avatar.AddFishingExp(950); // level 6
        avatar.AddCookingExp(49);
        avatar.AddGold(77);

        var decoded = new AvatarState(avatar.Bencoded);

        Assert.Equal(address, decoded.Address);
        Assert.Equal("ดารา", decoded.Name);
        Assert.Equal(950, decoded.FishingExp);
        Assert.Equal(49, decoded.CookingExp);
        Assert.Equal(6, decoded.FishingLevel);
        Assert.Equal(1, decoded.CookingLevel);
        Assert.Equal(avatar.Stamina, decoded.Stamina);
        Assert.Equal(150, decoded.MaxStamina);
        Assert.Equal(25, decoded.StaminaUpdatedAt);
        Assert.Equal(avatar.Gold, decoded.Gold);
        Assert.True(decoded.KitchenUnlocked);
    }

    [Fact]
    public void Inventory_decoded_counts_match_original()
    {
        var inventory = new Inventory();
        inventory.Add(1001, 5);
        inventory.Add(3008, 2);
        inventory.TryRemove(1001, 2);

        var decoded = new Inventory(inventory.Bencoded);

        Assert.Equal(inventory.GetCount(1001), decoded.GetCount(1001));
        Assert.Equal(inventory.GetCount(3008), decoded.GetCount(3008));
        Assert.Equal(inventory.GetCount(9999), decoded.GetCount(9999)); // missing → 0
        Assert.Equal(
            inventory.All.ToList(),
            decoded.All.ToList());
    }

    [Fact]
    public void FarmPlotState_decoded_fields_match_original()
    {
        var plot = new FarmPlotState(8);
        plot.Plant(4003, 555);

        var decoded = new FarmPlotState(plot.Bencoded);

        Assert.Equal(8, decoded.PlotIndex);
        Assert.Equal(4003, decoded.SeedId);
        Assert.Equal(555, decoded.PlantedAt);
        Assert.False(decoded.IsEmpty);
    }

    [Fact]
    public void PondOwnershipState_decoded_slots_match_original()
    {
        Address holderA = new PrivateKey().Address;
        Address holderB = new PrivateKey().Address;
        var pond = new PondOwnershipState(1);
        pond.Occupy(holderA, 100);
        pond.Occupy(holderB, 200);

        var decoded = new PondOwnershipState(pond.Bencoded);

        Assert.Equal(1, decoded.PondId);
        Assert.Equal(pond.Slots, decoded.Slots);
        Assert.True(decoded.HasActiveSlot(holderA, 99));
        Assert.False(decoded.HasActiveSlot(holderA, 100)); // expiry is exclusive
        Assert.Equal(2, decoded.ActiveCount(50));
    }

    [Fact]
    public void TaskBoardState_decoded_fields_match_original()
    {
        Address avatar = new PrivateKey().Address;
        var taskBoard = new TaskBoardState(avatar);
        taskBoard.SetTasks(new[] { 2, 6, 4 }, 900);
        taskBoard.MarkCompleted(4);

        var decoded = new TaskBoardState(taskBoard.Bencoded);

        Assert.Equal(avatar, decoded.AvatarAddress);
        Assert.Equal(900, decoded.LastRerolledAt);
        Assert.Equal(new[] { 2, 4, 6 }, decoded.TaskIds);
        Assert.True(decoded.IsCompleted(4));
        Assert.False(decoded.IsCompleted(2));
        Assert.False(decoded.AllCompleted);
    }

    // ------------------------------------------------------------------
    // Determinism: encoding must not depend on mutation order.
    // ------------------------------------------------------------------

    [Fact]
    public void Inventory_encoding_is_insertion_order_independent()
    {
        var a = new Inventory();
        a.Add(3001);
        a.Add(1001, 3);

        var b = new Inventory();
        b.Add(1001, 3);
        b.Add(3001);

        Assert.Equal(a.Bencoded, b.Bencoded);
    }

    [Fact]
    public void PondOwnershipState_encoding_is_occupy_order_independent()
    {
        Address a = new PrivateKey().Address;
        Address b = new PrivateKey().Address;

        var first = new PondOwnershipState(1);
        first.Occupy(a, 10);
        first.Occupy(b, 20);

        var second = new PondOwnershipState(1);
        second.Occupy(b, 20);
        second.Occupy(a, 10);

        Assert.Equal(first.Bencoded, second.Bencoded);
    }

    // ------------------------------------------------------------------
    // Malformed input is rejected with FailedLoadStateException, never an
    // unexpected InvalidCastException or NullReferenceException.
    // ------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(MalformedValues))]
    public void Malformed_bencoded_values_throw_FailedLoadStateException(
        Func<object> decode, Type expectedException)
    {
        Assert.Throws(expectedException, () => _ = decode());
    }

    public static TheoryData<Func<object>, Type> MalformedValues => new()
    {
        // Non-Dictionary roots.
        { () => new AvatarState((IValue)new Text("nope")), typeof(ProjectF.Lib.Exceptions.FailedLoadStateException) },
        { () => new Inventory((IValue)new Integer(1)), typeof(ProjectF.Lib.Exceptions.FailedLoadStateException) },
        { () => new FarmPlotState((IValue)new List()), typeof(ProjectF.Lib.Exceptions.FailedLoadStateException) },
        { () => new PondOwnershipState((IValue)new Text("nope")), typeof(ProjectF.Lib.Exceptions.FailedLoadStateException) },
        { () => new TaskBoardState((IValue)new Null()), typeof(ProjectF.Lib.Exceptions.FailedLoadStateException) },

        // Wrong field types / shapes.
        { () => new AvatarState(new Dictionary(new Dictionary<IKey, IValue>())), typeof(ProjectF.Lib.Exceptions.FailedLoadStateException) },
        { () => new Inventory(new Dictionary(new Dictionary<IKey, IValue>
            { [(Text)"items"] = (Integer)3 })), typeof(ProjectF.Lib.Exceptions.FailedLoadStateException) },
        { () => new Inventory(new Dictionary(new Dictionary<IKey, IValue>
            { [(Text)"items"] = new List(new IValue[] { (Text)"bad" }) })), typeof(ProjectF.Lib.Exceptions.FailedLoadStateException) },
        { () => new PondOwnershipState(new Dictionary(new Dictionary<IKey, IValue>
            { [(Text)"pond_id"] = (Integer)1 })), typeof(ProjectF.Lib.Exceptions.FailedLoadStateException) },
        { () => new TaskBoardState(new Dictionary(new Dictionary<IKey, IValue>
            { [(Text)"tasks"] = new List() })), typeof(ProjectF.Lib.Exceptions.FailedLoadStateException) },
    };

    private static void AssertRoundTrip<T>(T state)
        where T : notnull
    {
        // Disambiguate: every state exposes IValue Bencoded, but T could in
        // principle shadow it — resolve via the interface-free concrete type.
        IValue bencoded = state switch
        {
            AvatarState a => a.Bencoded,
            Inventory i => i.Bencoded,
            FarmPlotState f => f.Bencoded,
            PondOwnershipState p => p.Bencoded,
            TaskBoardState t => t.Bencoded,
            _ => throw new InvalidOperationException($"Unexpected state type {typeof(T)}"),
        };

        IValue decoded = state switch
        {
            AvatarState a => new AvatarState(bencoded).Bencoded,
            Inventory i => new Inventory(bencoded).Bencoded,
            FarmPlotState f => new FarmPlotState(bencoded).Bencoded,
            PondOwnershipState p => new PondOwnershipState(bencoded).Bencoded,
            TaskBoardState t => new TaskBoardState(bencoded).Bencoded,
            _ => throw new InvalidOperationException($"Unexpected state type {typeof(T)}"),
        };

        Assert.Equal(bencoded, decoded);
        Assert.Equal(bencoded.GetHashCode(), decoded.GetHashCode());
    }
}
