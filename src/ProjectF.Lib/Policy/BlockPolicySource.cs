using System;
using System.Collections.Generic;
using Libplanet.Action;
using Libplanet.Blockchain;
using Libplanet.Blockchain.Policies;
using Libplanet.Blocks;
using Libplanet.Types.Blocks;
using Libplanet.Types.Tx;

namespace ProjectF.Lib.Policy;

/// <summary>
/// Builds the network <see cref="IBlockPolicy"/> for ProjectF.
///
/// knowledge.md: closed test network — fixed 2s target block interval and
/// byte/tx caps. Libplanet 5.x has no PoW mining left (removed in 4.6/5.0);
/// block production happens via <c>BlockChain.ProposeBlock</c> in the SeedNode.
/// </summary>
public static class BlockPolicySource
{
    /// <summary>Target interval between two blocks.</summary>
    public const int TargetBlockIntervalMs = 2_000;

    /// <summary>Upper bound of transactions accepted into a single block.</summary>
    public const int MaxTransactionsPerBlock = 100;

    /// <summary>Upper bound of the byte size of a single transaction.</summary>
    public const int MaxTransactionBytes = 30_000;

    /// <summary>Upper bound of the byte size of a single block.</summary>
    public const long MaxBlockBytes = 1024 * 1024L;

    /// <summary>Builds the standard ProjectF block policy.</summary>
    public static IBlockPolicy GetPolicy() => new ProjectFBlockPolicy();

    /// <summary>
    /// Direct <see cref="IBlockPolicy"/> implementation. Member set and return
    /// types below were derived from the compiler's own interface report
    /// against Libplanet 5.5.3 (docs pages were incomplete).
    /// </summary>
    private sealed class ProjectFBlockPolicy : IBlockPolicy
    {
        // --- properties required by IBlockPolicy ---

        public IAction? BlockAction => null;

        public IPolicyActionsRegistry PolicyActionsRegistry { get; } =
            new PolicyActionsRegistry();

        public long MaxBlockBytes => MaxBlockBytesConst;

        public int MaxTransactionsPerBlock => BlockPolicySource.MaxTransactionsPerBlock;

        public int MaxTransactionsPerSignerPerBlock => BlockPolicySource.MaxTransactionsPerBlock;

        public int MaxGasPrice => 0;

        // --- methods required by IBlockPolicy ---

        public bool DoesTransactionFollowsPolicy(
            Transaction tx, IReadOnlyList<Transaction> minTransactions)
        {
            // Closed test network: generous for now.
            // TODO(stage-3): validate tx size against MaxTransactionBytes and
            // reject unknown action type_ids once the action registry exists.
            return true;
        }

        public BlockPolicyViolationException? ValidateNextBlock(
            BlockChain blockChain, Block nextBlock)
        {
            // TODO(stage-3): enforce timestamp spacing using nextBlock.Timestamp
            // vs. the tip once ProposeBlock wiring lands in the SeedNode.
            return null; // null means "valid" per IBlockPolicy contract.
        }

        public TxPolicyViolationException? ValidateNextBlockTx(
            BlockChain blockChain, Transaction transaction)
        {
            return null;
        }

        // Constant-size policy: the next block may carry up to
        // MaxTransactionsPerBlock transactions of MaxTransactionBytes each.
        // (The Libplanet default scales with the previous block's byte size;
        // a naive "blockBytes * N" here collapses to ~100 bytes at genesis
        // and silently empties every proposed block.)
        public long GetMaxTransactionsBytes(long blockBytes) =>
            MaxTransactionsPerBlockConst * MaxTransactionBytesConst;

        public int GetMinTransactionsPerBlock(long blockBytes) => 0;

        public int GetMaxTransactionsPerBlock(long blockBytes) =>
            BlockPolicySource.MaxTransactionsPerBlock;

        public int GetMaxTransactionsPerSignerPerBlock(long blockBytes) =>
            BlockPolicySource.MaxTransactionsPerBlock;

        public long GetMaxEvidencePendingDuration(long blockBytes) => long.MaxValue;
    }

    /// <summary>Kept as a constant so the property above stays expression-bodied.</summary>
    private const long MaxBlockBytesConst = 1024 * 1024L;

    private const int MaxTransactionsPerBlockConst = 100;

    private const int MaxTransactionBytesConst = 30_000;
}
