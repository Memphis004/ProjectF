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
        // TODO(stage-1): exercise via Libplanet.Mocks IActionContext once the
        // mock factory surface is verified; the pipeline-proof checkpoint
        // (Unity -> sign -> mine -> read state) is the primary gate.
        var action = new PingAction(1);
        Assert.Equal("ping_v1", action.TypeId);
    }
}
