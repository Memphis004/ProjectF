using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Libplanet.Types.Blocks;

namespace ProjectF.SeedNode;

/// <summary>
/// Block production loop: proposes a block every TargetBlockIntervalMs
/// (carrying whatever transactions are staged), appends it with this node's
/// validator commit, and retries immediately against the new tip if a race
/// is lost (the planet-clicker devlog's "Tip changed while mining" lesson).
/// </summary>
public sealed class MinerLoop
{
    private readonly SwarmRunner _runner;
    private readonly int _targetBlockIntervalMs;

    public MinerLoop(SwarmRunner runner, int targetBlockIntervalMs)
    {
        _runner = runner;
        _targetBlockIntervalMs = Math.Max(200, targetBlockIntervalMs);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        long lastTipIndex = _runner.Chain.Tip.Index;

        Console.WriteLine(
            $"Miner loop started (target interval: {_targetBlockIntervalMs} ms).");

        while (!cancellationToken.IsCancellationRequested)
        {
            DateTimeOffset startedAt = DateTimeOffset.UtcNow;

            try
            {
                Block tip = _runner.Chain.Tip;

                // From index >= 2 the next block must carry the previous
                // block's commit in its header (Libplanet's PBFT rule).
                Block block = _runner.Chain.ProposeBlock(
                    _runner.NodeKey,
                    lastCommit: tip.Index > 0 ? _runner.CreateBlockCommit(tip) : null);

                _runner.Chain.Append(block, _runner.CreateBlockCommit(block));
                lastTipIndex = block.Index;
                Console.WriteLine(
                    $"Block #{block.Index} mined (txs: {block.Transactions.Count()}).");
            }
            catch (Exception ex)
                when (ex is InvalidOperationException
                    or Libplanet.Types.Blocks.InvalidBlockException
                    or OperationCanceledException)
            {
                // Tip changed while proposing/appending — retry immediately
                // against the new tip instead of waiting a full interval.
                Console.WriteLine(
                    $"Tip race while mining (retrying): {ex.GetType().Name}: {ex.Message}");
                continue;
            }

            TimeSpan elapsed = DateTimeOffset.UtcNow - startedAt;
            TimeSpan remaining = TimeSpan.FromMilliseconds(_targetBlockIntervalMs) - elapsed;
            if (remaining > TimeSpan.Zero)
            {
                try
                {
                    await Task.Delay(remaining, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        Console.WriteLine($"Miner loop stopped (last tip #{lastTipIndex}).");
    }
}
