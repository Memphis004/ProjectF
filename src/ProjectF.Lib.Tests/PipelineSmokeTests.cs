using System;
using System.Linq;
using Bencodex.Types;
using Libplanet.Blockchain;
using Libplanet.Crypto;
using Libplanet.Types.Blocks;
using Libplanet.Types.Tx;
using ProjectF.Lib;
using ProjectF.Lib.Actions;
using Xunit;

namespace ProjectF.Lib.Tests;

/// <summary>
/// Stage-1b pipeline-proof: genesis → sign tx → ProposeBlock → Append →
/// counter state visibly increased. This is the same flow the Unity probe
/// exercises against the live SeedNode, minus the network hop.
/// </summary>
public class PipelineSmokeTests : IDisposable
{
    private readonly ChainFixture _fixture = new();

    [Fact]
    public void PingTx_ProposedAndAppended_IncrementsCounterOnChain()
    {
        BlockChain chain = _fixture.Chain;
        PrivateKey player = new();

        long before = ReadPingCounter(chain);

        // Unity-side flow: sign a standalone tx carrying the action payload.
        Transaction tx = Transaction.Create(
            nonce: chain.GetNextTxNonce(player.Address),
            privateKey: player,
            genesisHash: chain.Genesis.Hash,
            actions: new[] { new PingAction(3).PlainValue });

        chain.StageTransaction(tx);

        Assert.True(
            chain.GetStagedTransactionIds().Contains(tx.Id),
            "tx was not staged");

        // SeedNode-side flow: propose + append with a validator commit.
        Block block = chain.ProposeBlock(_fixture.ValidatorKey);
        chain.Append(block, _fixture.CreateBlockCommit(block));

        Assert.True(
            block.Transactions.Any(t => t.Id == tx.Id),
            $"proposed block did not include the tx (block tx count: " +
            $"{block.Transactions.Count()})");

        var exec = chain.GetTxExecution(block.Hash, tx.Id);
        Assert.NotNull(exec);
        Assert.False(exec.Fail, "tx failed — see test-output for the action exception");

        Assert.Equal(before + 3, ReadPingCounter(chain));
    }

    [Fact]
    public void TwoPingBlocks_CounterAccumulates()
    {
        BlockChain chain = _fixture.Chain;
        PrivateKey player = new();

        for (int i = 0; i < 2; i++)
        {
            Transaction tx = Transaction.Create(
                nonce: chain.GetNextTxNonce(player.Address),
                privateKey: player,
                genesisHash: chain.Genesis.Hash,
                actions: new[] { new PingAction(1).PlainValue });
            chain.StageTransaction(tx);

            // From index >= 2 the next block must carry the previous block's
            // commit in its header (Libplanet's PBFT rule).
            Block block = chain.ProposeBlock(
                _fixture.ValidatorKey,
                lastCommit: chain.Tip.Index > 0
                    ? _fixture.CreateBlockCommit(chain.Tip)
                    : null);
            chain.Append(block, _fixture.CreateBlockCommit(block));
        }

        Assert.Equal(2, ReadPingCounter(chain));
    }

    [Fact]
    public void PingAction_RoundTripsThroughLoader()
    {
        // The TypedActionLoader must deserialize the action envelope from a
        // real (proposed) block — proving the wire format is on-chain safe.
        BlockChain chain = _fixture.Chain;
        PrivateKey player = new();

        Transaction tx = Transaction.Create(
            nonce: chain.GetNextTxNonce(player.Address),
            privateKey: player,
            genesisHash: chain.Genesis.Hash,
            actions: new[] { new PingAction(7).PlainValue });
        chain.StageTransaction(tx);

        Block block = chain.ProposeBlock(_fixture.ValidatorKey);
        chain.Append(block, _fixture.CreateBlockCommit(block));

        Transaction stored = chain.GetTransaction(tx.Id);
        IValue payload = stored.Actions.Single();
        var roundTripped = new PingAction();
        roundTripped.LoadPlainValue(payload);
        Assert.Equal(new PingAction(7).PlainValue, roundTripped.PlainValue);
    }

    private static long ReadPingCounter(BlockChain chain)
    {
        // GetNextWorldState() is the tip state — the same accessor Libplanet's
        // own tests use to read post-append state. (The parameterless
        // GetWorldState() lags one block behind in 5.5.3.)
        Libplanet.Action.State.IAccountState? pingAccount =
            chain.GetNextWorldState()?.GetAccountState(Addresses.Ping);
        return pingAccount?.GetState(Addresses.PingCounter) is Integer value
            ? (long)value
            : 0L;
    }

    public void Dispose() => _fixture.Dispose();
}
