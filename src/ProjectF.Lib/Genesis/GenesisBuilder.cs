using System;
using System.Collections.Immutable;
using System.Numerics;
using System.Security.Cryptography;
using Bencodex.Types;
using Libplanet.Action;
using Libplanet.Action.Sys;
using Libplanet.Blockchain;
using Libplanet.Common;
using Libplanet.Crypto;
using Libplanet.Store.Trie;
using Libplanet.Types.Blocks;
using Libplanet.Types.Consensus;
using Libplanet.Types.Evidence;
using Libplanet.Types.Tx;
using ProjectF.Lib.Policy;

namespace ProjectF.Lib.Genesis;

/// <summary>Everything the SeedNode needs to boot a chain or print genesis info.</summary>
public sealed class GenesisContext
{
    public GenesisContext(Block genesisBlock, Transaction genesisTx)
    {
        GenesisBlock = genesisBlock;
        GenesisTx = genesisTx;
    }

    public Block GenesisBlock { get; }

    /// <summary>The signed Initialize transaction inside the genesis block.</summary>
    public Transaction GenesisTx { get; }
}

/// <summary>
/// Builds the ProjectF genesis block, mirroring the flow Libplanet's own
/// 5.5.3 tests use (TestUtils.MakeBlockChainAndActionEvaluator):
///
/// 1. The genesis transaction carries the Initialize system action, which
///    installs the validator set into state (Append validates BlockCommits
///    against it).
/// 2. Protocol 8 genesis blocks are signed with MerkleTrie.EmptyRootHash —
///    the evaluated state root hash is committed by BlockChain.Create.
/// </summary>
public static class GenesisBuilder
{

    /// <summary>
    /// Builds the full genesis context (block + embedded transaction) using
    /// the given action evaluator to compute the executed state root.
    /// </summary>
    public static GenesisContext BuildGenesisContext(
        PrivateKey validatorKey,
        ValidatorSet validatorSet,
        IActionEvaluator actionEvaluator,
        DateTimeOffset? timestamp = null)
    {
        Transaction genesisTx = BuildGenesisTx(validatorKey, validatorSet, timestamp);

        BlockContent content = new(
            new BlockMetadata(
                protocolVersion: Block.CurrentProtocolVersion,
                index: 0,
                timestamp: timestamp ?? DateTimeOffset.MinValue,
                miner: validatorKey.PublicKey.Address,
                publicKey: validatorKey.PublicKey,
                previousHash: null,
                txHash: BlockContent.DeriveTxHash(ImmutableArray.Create(genesisTx)),
                lastCommit: null,
                evidenceHash: null),
            transactions: ImmutableArray.Create(genesisTx),
            evidence: ImmutableArray<EvidenceBase>.Empty);

        PreEvaluationBlock preEval = content.Propose();

        // Evaluate the genesis tx (Initialize) so its states are committed
        // to the state store, and sign the genesis with the EVALUATED state
        // root hash (Libplanet 5.5.3 / protocol 8 behaviour; newer protocol
        // versions sign the empty root hash instead).
        IReadOnlyList<ICommittedActionEvaluation> evaluations =
            actionEvaluator.Evaluate(preEval, null);
        HashDigest<SHA256> stateRootHash = evaluations[^1].OutputState;

        Block genesisBlock = preEval.Sign(validatorKey, stateRootHash);
        return new GenesisContext(genesisBlock, genesisTx);
    }

    /// <summary>
    /// Convenience wrapper used by tests and the SeedNode when only the block
    /// itself is needed.
    /// </summary>
    public static Block BuildGenesisBlock(
        PrivateKey validatorKey,
        ValidatorSet validatorSet,
        DateTimeOffset? timestamp = null)
    {
        var policy = BlockPolicySource.GetPolicy();
        var actionEvaluator = new ActionEvaluator(
            policy.PolicyActionsRegistry,
            stateStore: new Libplanet.Store.TrieStateStore(
                new Libplanet.Store.Trie.MemoryKeyValueStore()),
            actionTypeLoader: Libplanet.Action.Loader.TypedActionLoader.Create(
                typeof(GenesisBuilder).Assembly));

        return BuildGenesisContext(
            validatorKey, validatorSet, actionEvaluator, timestamp).GenesisBlock;
    }

    /// <summary>
    /// The signed Initialize transaction embedded in the genesis block.
    /// Libplanet 5.x requires the genesis tx signer to be a validator, so the
    /// transaction is signed with the validator key at nonce 0.
    /// </summary>
    public static Transaction BuildGenesisTx(
        PrivateKey validatorKey,
        ValidatorSet validatorSet,
        DateTimeOffset? timestamp = null)
    {
        var initialize = new Initialize(
            validatorSet,
            ImmutableDictionary<Address, IValue>.Empty);

        return Transaction.Create(
            nonce: 0,
            privateKey: validatorKey,
            genesisHash: null,
            actions: new[] { initialize.PlainValue },
            timestamp: timestamp ?? DateTimeOffset.MinValue);
    }
}
