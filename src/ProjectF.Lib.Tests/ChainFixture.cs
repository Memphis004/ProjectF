using System;
using System.Collections.Immutable;
using System.Numerics;
using Libplanet.Action;
using Libplanet.Action.Loader;
using Libplanet.Action.State;
using Libplanet.Action.Sys;
using Libplanet.Blockchain;
using Libplanet.Blockchain.Policies;
using Libplanet.Crypto;
using Libplanet.Store;
using Libplanet.Store.Trie;
using Libplanet.Types.Blocks;
using Libplanet.Types.Consensus;
using Libplanet.Types.Tx;
using ProjectF.Lib.Actions;
using ProjectF.Lib.Genesis;
using ProjectF.Lib.Policy;

namespace ProjectF.Lib.Tests;

/// <summary>
/// Builds a minimal in-memory ProjectF chain, mirroring the flow Libplanet's
/// own 5.5.3 tests use (TestUtils.MakeBlockChainAndActionEvaluator):
/// genesis tx carries the Initialize system action with the validator set,
/// the genesis is signed with the EMPTY root hash (protocol 8 signs the root
/// of the pre-execution empty trie), and the state root hash of the executed
/// genesis is committed by BlockChain.Create via the action evaluator.
/// </summary>
public sealed class ChainFixture : IDisposable
{
    public PrivateKey ValidatorKey { get; } = new();

    public ValidatorSet ValidatorSet { get; }

    public MemoryStore Store { get; } = new();

    public TrieStateStore StateStore { get; } =
        new TrieStateStore(new MemoryKeyValueStore());

    public BlockChain Chain { get; }

    public GenesisContext Genesis { get; }

    public ChainFixture()
    {
        ValidatorSet = new ValidatorSet(
            new List<Libplanet.Types.Consensus.Validator>
            {
                new(ValidatorKey.PublicKey, BigInteger.One),
            });

        IBlockPolicy policy = BlockPolicySource.GetPolicy();
        var actionLoader = TypedActionLoader.Create(typeof(PingAction).Assembly);
        var actionEvaluator = new ActionEvaluator(
            policy.PolicyActionsRegistry,
            stateStore: StateStore,
            actionTypeLoader: actionLoader);

        Genesis = GenesisBuilder.BuildGenesisContext(
            ValidatorKey,
            ValidatorSet,
            actionEvaluator);

        Chain = BlockChain.Create(
            policy,
            new VolatileStagePolicy(),
            Store,
            StateStore,
            Genesis.GenesisBlock,
            actionEvaluator);
    }

    /// <summary>
    /// Builds the signed BlockCommit for <paramref name="block"/> using the
    /// fixture's validator key — equivalent of Libplanet's TestUtils.CreateBlockCommit.
    /// </summary>
    public BlockCommit CreateBlockCommit(Block block)
    {
        if (block.Index <= 0)
        {
            return null!;
        }

        var vote = new VoteMetadata(
            block.Index,
            0,
            block.Hash,
            DateTimeOffset.UtcNow,
            ValidatorKey.PublicKey,
            ValidatorSet.GetValidator(ValidatorKey.PublicKey).Power,
            VoteFlag.PreCommit).Sign(ValidatorKey);

        return new BlockCommit(
            block.Index, 0, block.Hash, ImmutableArray.Create(vote));
    }

    public void Dispose()
    {
        StateStore.Dispose();
    }
}
