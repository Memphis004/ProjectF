using Bencodex.Types;
using Libplanet.Action;
using ProjectF.Lib.Actions;
using Xunit;

namespace ProjectF.Lib.Tests;

public class PingActionTests
{
    [Fact]
    public void PlainValue_Envelope_HasTypeIdAndValues()
    {
        var action = new PingAction(3);

        IValue plainValue = action.PlainValue;

        var dict = Assert.IsType<Dictionary>(plainValue);
        Assert.Equal((Text)PingAction.StaticTypeId, dict[(Text)"type_id"]);
        Assert.True(dict.ContainsKey((Text)"values"));
    }

    [Fact]
    public void Execute_IncrementsCounterFromZero()
    {
        var world = new TestWorld();
        var signer = new Libplanet.Crypto.PrivateKey();
        var context = TestActionContext.Create(world.World, signer.Address, blockIndex: 1);

        world.Execute(new PingAction(1), context);

        Assert.Equal((Integer)1, world.GetState(Addresses.Ping, Addresses.PingCounter));
    }

    [Fact]
    public void Execute_AccumulatesAcrossBlocks()
    {
        var world = new TestWorld();
        var signer = new Libplanet.Crypto.PrivateKey().Address;
        var context = TestActionContext.Create(world.World, signer, blockIndex: 1);

        world.Execute(new PingAction(1), context);
        context = TestActionContext.Create(world.World, signer, blockIndex: 2);
        world.Execute(new PingAction(41), context);

        Assert.Equal((Integer)42, world.GetState(Addresses.Ping, Addresses.PingCounter));
    }

    [Fact]
    public void Execute_RoundTripsThroughPlainValue()
    {
        var world = new TestWorld();
        var signer = new Libplanet.Crypto.PrivateKey().Address;
        var context = TestActionContext.Create(world.World, signer, blockIndex: 1);

        var original = new PingAction(7);
        var rehydrated = new PingAction();
        rehydrated.LoadPlainValue(original.PlainValue);
        world.Execute(rehydrated, context);

        Assert.Equal((Integer)7, world.GetState(Addresses.Ping, Addresses.PingCounter));
    }
}
