using System;
using System.Threading;
using System.Threading.Tasks;
using Bencodex.Types;

// ReSharper disable CheckNamespace
namespace ProjectF.Infrastructure.Blockchain
{
    /// <summary>
    /// The Unity-side view of the local (embedded) Libplanet node: read
    /// confirmed state at the current tip, stage transactions, await
    /// confirmation. Mirrors the API surface proven by
    /// Assets/Main/Scripts/Editor/Probe.cs (Stage 2.5 checkpoint) — swarm +
    /// transport run on the threadpool; only the result lands on the main
    /// thread (UniTask/UniSwitchToMainThread discipline is up to callers).
    /// </summary>
    public interface ILibplanetClient
    {
        /// <summary>Current local tip index (0 before bootstrap).</summary>
        long TipIndex { get; }

        /// <summary>Current local tip hash, hex (empty before bootstrap).</summary>
        string TipHash { get; }

        /// <summary>Stage 11: parent hash of the current tip, hex (empty for
        /// genesis / before bootstrap). Lets StateWatcher verify that a new
        /// tip actually EXTENDS the previous one — the reorg check.</summary>
        string TipPreviousHash { get; }

        ChainStatus Status { get; }

        /// <summary>Peer count from the swarm (0 when offline).</summary>
        int PeerCount { get; }

        /// <summary>The player key's address, hex (empty before key load).</summary>
        string PlayerAddress { get; }

        /// <summary>
        /// Starts the embedded node: load/create key → read bootstrap files →
        /// swarm → bootstrap + preload from the seeds. Completes with
        /// <see cref="ChainStatus"/> even when no seed is reachable (offline
        /// read-only mode) — it does not throw for network reasons.
        /// </summary>
        Task<ChainStatus> BootstrapAsync(CancellationToken ct);

        /// <summary>Reads a raw Bencodex value from an account at the current tip.</summary>
        IValue? GetState(in Libplanet.Crypto.Address account, in Libplanet.Crypto.Address key);

        /// <summary>Display-only catch-up estimate (Stage 11): 0 when the seed
        /// tip is unknown (renders the honest "block N" fallback), otherwise
        /// the highest peer tip seen this session. NEVER gates gameplay —
        /// Spec 9.4 keeps progress optional and purely cosmetic.</summary>
        SyncProgress SyncProgress { get; }

        /// <summary>
        /// Signs (player key) + stages the action's PlainValue and waits until a
        /// mined block executes it. Throws on validation failures (the action
        /// threw on-chain — action exception type is preserved in Fail) and on
        /// <see cref="TimeoutException"/> when no block confirms within
        /// <paramref name="timeout"/>. Transport failures are surfaced as the
        /// underlying exception; <see cref="ActionQueue"/> turns them into
        /// bool + event outcomes so the UI never sees raw throws.
        /// </summary>
        Task<long> StageAndWaitAsync(
            Bencodex.Types.Dictionary actionPlainValue,
            TimeSpan timeout,
            CancellationToken ct);
    }
}
