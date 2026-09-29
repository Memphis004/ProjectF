using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Bencodex.Types;
using Libplanet.Action;
using Libplanet.Action.Loader;
using Libplanet.Blockchain;
using Libplanet.Net;
using Libplanet.Blockchain.Policies;
using Libplanet.Crypto;
using Libplanet.Net;
using Libplanet.Net.Options;
using Libplanet.Net.Transports;
using Libplanet.Store;
using Libplanet.Store.Trie;
using Libplanet.Types.Blocks;
using Libplanet.Types.Tx;
using ProjectF.Lib;
using ProjectF.Lib.Actions;
using ProjectF.Lib.Genesis;
using ProjectF.Lib.Policy;
using ProjectF.Infrastructure.Network;
using UnityEngine;

// ReSharper disable CheckNamespace
namespace ProjectF.Infrastructure.Blockchain
{
    /// <summary>
    /// Embedded node client. The whole Libplanet surface here is copied from
    /// the Stage 2.5 pipeline proof (Editor/Probe.cs) which runs under
    /// Unity/Mono against the synced netstandard2.1 DLLs — do not "modernize"
    /// any call without re-proving it in the probe.
    ///
    /// Threading contract: swarm/transport tasks run on the threadpool (their
    /// continuations marshal back to the UnitySynchronizationContext — awaiting
    /// them from the main thread deadlocks, per Probe.cs). Public entry points
    /// hop to the threadpool and hand results back to UniTask callers.
    /// </summary>
    /// <summary>NOTE (Stage 11.5, found by the Play-Mode shutdown guard):
    /// this class MUST declare IDisposable — the method alone is not enough.
    /// VContainer tracks teardown via the INTERFACE, and the Editor guard
    /// casts EditorShutdownHook.Chain to IDisposable; without the declaration
    /// BOTH skip disposing this client → the swarm task survived teardown →
    /// the original domain-reload hang.</summary>
    public sealed class LibplanetClient : ILibplanetClient, IDisposable
    {
        // NOTE: 'Dictionary' below always means Bencodex.Types.Dictionary
        // (fully qualified at the interface seam to avoid the
        // System.Collections.Generic collision).
        private readonly NetworkSettings _settings;
        private readonly KeyStore _keyStore;
        private readonly BackgroundTaskRegistry _registry;
        private readonly BackedUpCts _cts;

        private PrivateKey? _playerKey;
        private BlockChain? _chain;
        private Swarm? _swarm;
        private IStore? _store;
        private TrieStateStore? _stateStore;

        // Stage 11.5: every background task this client starts is tracked so
        // shutdown can cancel → await (bounded) → dispose. The swarm task is
        // ALSO held here: Swarm.StartAsync only completes when the swarm has
        // fully stopped, so it is the canonical "did the gossip really end"
        // signal (see DisposeAsync — it is the leak that hung domain reloads).
        private Task? _swarmTask;

        /// <summary>Highest peer tip observed this session (Volatile long —
        /// written by the sampler task, read by UI); 0 when unknown.</summary>
        private long _peerTipTarget;

        public long TipIndex => _chain?.Tip.Index ?? 0L;

        public string TipHash => _chain?.Tip.Hash.ToString() ?? string.Empty;

        // Stage 11: parent of the tip — the reorg detector compares this to
        // the previously-seen tip hash (genesis has no parent → empty).
        public string TipPreviousHash => _chain?.Tip.PreviousHash?.ToString() ?? string.Empty;

        public ChainStatus Status { get; private set; } = ChainStatus.Bootstrapping;

        public int PeerCount => _swarm?.Peers.Count ?? 0;

        /// <summary>Display-only catch-up estimate. The seed tip is sampled
        /// every ~1s on the threadpool while connected (BestKnownTip is
        /// thread-safe); consumers may read it from any thread.</summary>
        public SyncProgress SyncProgress
        {
            get
            {
                long tip = TipIndex;
                long target = _peerTipTarget;
                return new SyncProgress
                {
                    Tip = tip,
                    TargetTip = target,
                    HasTarget = target > tip,
                };
            }
        }

        public string PlayerAddress => _playerKey?.Address.ToString() ?? string.Empty;

        public LibplanetClient(
            NetworkSettings settings, KeyStore keyStore, BackgroundTaskRegistry registry)
        {
            _settings = settings;
            _keyStore = keyStore;
            _registry = registry;
            _cts = new BackedUpCts(registry);
        }

        public async Task<ChainStatus> BootstrapAsync(CancellationToken ct)
        {
            try
            {
                // The core body touches Application.persistentDataPath (via
                // KeyStore and ResolvedStorePath) — a main-thread-only Unity
                // API. Pre-resolve ALL settings-derived paths HERE on the main
                // thread, before the Task.Run hop (found by the E2E chain test:
                // the throw silently degraded every session to offline
                // read-only mode).
                string storePath = _settings.ResolvedStorePath;
                string genesisPath = _settings.ResolvedGenesisPath;
                string hubAddress = _settings.HubAddress;
                int nodePort = _settings.NodePort;
                // Keep the canonical key location ({persistentDataPath}/keys)
                // — resolved HERE, since the core runs off-thread.
                string keysDir = System.IO.Path.Combine(Application.persistentDataPath, "keys");
                Status = await Task.Run(() => BootstrapCoreAsync(storePath, genesisPath, hubAddress, nodePort, keysDir, ct), ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // UX contract: unreachable chain = playable read-only mode, clearly said.
                Debug.LogWarning($"[chain] bootstrap failed — offline read-only mode. {ex.Message}");
                Status = ChainStatus.Offline;
            }

            return Status;
        }

        private async Task<ChainStatus> BootstrapCoreAsync(
            string storeDir, string genesisPath, string hubAddress, int nodePort,
            string keysDir, CancellationToken ct)
        {
            CancellationToken shutdown = _cts.Token;
            _playerKey = _keyStore.LoadOrCreatePlayerKey(keysDir);

            Directory.CreateDirectory(storeDir);

            // --- Bootstrap files: peer.txt / apv.txt / genesis.dat (README
            // "The bootstrap files": no secrets in any of them). When the
            // files are absent we still build a local chain so the game is
            // playable solo (status stays Synced; nothing to sync to).
            string? peerText = ReadTrimmed(Path.Combine(storeDir, "peer.txt"));
            string? apvText = ReadTrimmed(Path.Combine(storeDir, "apv.txt"));
            if (string.IsNullOrEmpty(apvText) &&
                !string.IsNullOrEmpty(_settings.ApvToken))
            {
                apvText = _settings.ApvToken.Trim();
            }

            BoundPeer? seedPeer = null;
            if (!string.IsNullOrEmpty(peerText))
            {
                seedPeer = BoundPeer.ParsePeer(peerText);
            }
            else if (_settings.SeedPeers is { Length: > 0 } &&
                     !string.IsNullOrWhiteSpace(_settings.SeedPeers[0]))
            {
                seedPeer = BoundPeer.ParsePeer(_settings.SeedPeers[0].Trim());
            }

            Block genesis = LoadOrCreateGenesis(storeDir, genesisPath);

            IBlockPolicy policy = BlockPolicySource.GetPolicy();
            IActionLoader actionLoader = TypedActionLoader.Create(typeof(PingAction).Assembly);
            var stateStore = new TrieStateStore(
                new DefaultKeyValueStore(Path.Combine(storeDir, "states")));
            IStore store = new DefaultStore(storeDir);

            var actionEvaluator = new ActionEvaluator(
                policy.PolicyActionsRegistry,
                stateStore: stateStore,
                actionTypeLoader: actionLoader);

            // A joining node receives the genesis pre-signed WITH evaluated
            // state root — commit genesis states into the fresh local store
            // before BlockChain.Create can resolve the root (Probe.cs step 2).
            var genesisPreEval = new PreEvaluationBlock(
                genesis.Header,
                genesis.Transactions,
                Enumerable.Empty<Libplanet.Types.Evidence.EvidenceBase>());
            _ = actionEvaluator.Evaluate(genesisPreEval, null);

            // DefaultStore.GetCanonicalChainId() returns Guid.Empty (not
            // null) on a fresh store — treat BOTH as "no chain yet" or the
            // BlockChain ctor throws "does not contain chain id
            // 00000000-…" (found by the chain E2E).
            Guid? chainId = store.GetCanonicalChainId();
            if (chainId is null || chainId.Value == Guid.Empty)
            {
                // ListChainIds returns IEnumerable — deterministically take
                // the first (Probe.cs adopt-existing-chain behavior).
                chainId = store.ListChainIds().FirstOrDefault();
                if (chainId is { } && chainId.Value != Guid.Empty)
                {
                    store.SetCanonicalChainId(chainId.Value);
                }
                else
                {
                    chainId = null; // fresh store → BlockChain.Create below
                }
            }

            _chain = chainId is { }
                ? new BlockChain(
                    policy, new VolatileStagePolicy(), store, stateStore, genesis,
                    new BlockChainStates(store, stateStore), actionEvaluator)
                : BlockChain.Create(
                    policy, new VolatileStagePolicy(), store, stateStore, genesis,
                    actionEvaluator);
            _store = store;
            _stateStore = stateStore;

            // --- No seed configured: solo local chain (create_avatar etc. all
            // work; there is just nobody to sync with).
            if (seedPeer is null)
            {
                Status = ChainStatus.Synced;
                Debug.LogWarning(
                    "[chain] no SeedPeers/peer.txt configured — running a local " +
                    "solo chain (transactions mine only on this node).");
                return Status;
            }

            // --- Transport + swarm, exactly as Probe.cs wires them. APV token:
            // every node must present the SAME signed token or messages are
            // silently dropped.
            var apvOptions = new AppProtocolVersionOptions();
            if (!string.IsNullOrEmpty(apvText))
            {
                apvOptions.AppProtocolVersion = AppProtocolVersion.FromToken(apvText);
            }

            var hostOptions = new HostOptions("127.0.0.1", Array.Empty<IceServer>(), nodePort);
            var swarmOptions = new SwarmOptions
            {
                StaticPeers = ImmutableHashSet.Create(seedPeer),
                TxBroadcastInterval = TimeSpan.FromMilliseconds(500),
            };

            ITransport transport = await NetMQTransport.Create(_playerKey, apvOptions, hostOptions);
            _swarm = new Swarm(_chain, _playerKey, transport, swarmOptions, consensusTransport: null);

            // Gossip runs for the whole session — NO time-based auto-cancel:
            // a timeout CTS here silently killed transport after
            // SyncTimeoutSeconds (chain E2E lesson: client fell off the seed
            // mid-session, peers 0). Cancellation comes from the ONE shared
            // shutdown source (_cts); the task is TRACKED so teardown awaits
            // it before disposing the store (spec 1 + 2).
            Task swarmTask = _registry.Run(
                "LibplanetClient.Swarm",
                async () =>
                {
                    try
                    {
                        await _swarm!.StartAsync(shutdown);
                    }
                    catch (OperationCanceledException)
                    {
                        // Expected during teardown — completion is the signal
                        // that gossip/polling really ended.
                    }
                });
            _swarmTask = swarmTask;

            // Sample the seed's tip in the background so the UI can render an
            // honest "N / M" during catch-up. Pure display — a failure here
            // never affects sync (PreloadAsync decides what to pull).
            // Loop with a cancellation check per iteration (spec 1).
            _ = _registry.Run(
                "LibplanetClient.PeerTipSampler",
                () => _registry.Loop(
                    "LibplanetClient.PeerTipSampler",
                    TimeSpan.FromSeconds(1),
                    shutdown,
                    async () =>
                    {
                        Swarm? swarm = _swarm;
                        if (swarm is { } && swarm.Running)
                        {
                            IEnumerable<Libplanet.Net.PeerChainState> states =
                                await swarm.GetPeerChainStateAsync(TimeSpan.FromSeconds(5), shutdown);
                            long best = 0;
                            foreach (Libplanet.Net.PeerChainState state in states)
                            {
                                best = Math.Max(best, state.TipIndex);
                            }

                            if (best > 0)
                            {
                                Volatile.Write(ref _peerTipTarget, best);
                            }
                        }
                    }));

            // Stage 11.5 (spec 3): WaitForRunningAsync is an UNBOUNDED wait —
            // a wedged transport hangs bootstrap forever. Bound it to 15s.
            Task running = _swarm.WaitForRunningAsync();
            bool startedInTime = await BackgroundTaskRegistry
                .WaitForExitAsync(running, TimeSpan.FromSeconds(15));
            if (!startedInTime)
            {
                Debug.LogWarning("[chain] swarm transport did not start in 15s — continuing offline.");
                Status = ChainStatus.Offline;
                return Status;
            }

            try
            {
                await _swarm.BootstrapAsync(
                    new[] { seedPeer },
                    TimeSpan.FromSeconds(5),
                    3,
                    shutdown);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[chain] bootstrap incomplete — {ex.Message}");
            }

            // Stage 11.5 (spec 3): zero reachable peers after a bounded window
            // → give up and continue OFFLINE; never retry discovery forever in
            // the background without honouring cancellation.
            if (PeerCount == 0)
            {
                DateTimeOffset offlineDeadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(5);
                while (PeerCount == 0 && DateTimeOffset.UtcNow < offlineDeadline)
                {
                    try
                    {
                        await Task.Delay(250, shutdown);
                    }
                    catch (OperationCanceledException)
                    {
                        Status = ChainStatus.Offline;
                        return Status;
                    }
                }

                if (PeerCount == 0)
                {
                    Debug.LogWarning(
                        "[chain] no reachable peer within 5s of bootstrap — continuing OFFLINE " +
                        "(swarm stays up; a later connection is picked up by the sampler). Status=" +
                        ChainStatus.Offline);
                    Status = ChainStatus.Offline;
                    return Status;
                }
            }

            // Bulk catch-up: gossip only carries new blocks; a fresh node must
            // pull whole blocks+states. Repeated passes: a single PreloadAsync
            // can complete early on long chains (Probe.cs lesson).
            // Signature verified against 5.5.3: (IProgress<BlockSyncState>, CT)
            // — progress reporting is cosmetic here, so null.
            long before = _chain.Tip.Index;
            for (int pass = 0; pass < 3; pass++)
            {
                // Per-pass budget (NOT a session token): a fresh node pulling
                // a long chain exceeds any fixed session timeout — the old
                // shared CTS aborted PreloadAsync mid-catch-up and killed the
                // whole bootstrap (chain E2E lesson). Linked to ct so an
                // external cancel (app teardown) still propagates; a pass
                // budget expiry just resumes with the next pass.
                using var preloadCts = CancellationTokenSource.CreateLinkedTokenSource(ct, shutdown);
                preloadCts.CancelAfter(TimeSpan.FromSeconds(
                    Math.Max(300, _settings.SyncTimeoutSeconds)));
                try
                {
                    await _swarm.PreloadAsync(
                        (System.IProgress<Libplanet.Net.BlockSyncState>?)null, preloadCts.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    Debug.LogWarning("[chain] preload pass timed out — resuming catch-up.");
                }

                long after = _chain.Tip.Index;
                if (after <= before)
                {
                    break;
                }

                before = after;
            }

            Status = PeerCount > 0 ? ChainStatus.Syncing : ChainStatus.Offline;
            Debug.Log($"[chain] bootstrap done — tip #{TipIndex}, peers {PeerCount}, status {Status}");
            return Status;
        }

        // Stage 11.5: the old inline PeerTipSamplerAsync loop moved into the
        // tracked _registry.Loop call inside BootstrapCoreAsync (cancellation
        // checked per iteration, task tracked, awaited during teardown).

        public IValue? GetState(in Address account, in Address key)
        {
            BlockChain? chain = _chain;
            if (chain is null)
            {
                return null;
            }

            // GetWorldState() (no-arg) covers the tip deterministically —
            // verified via reflection against Libplanet 5.5.3.
            Libplanet.Action.State.IWorldState world = chain.GetWorldState();
            Libplanet.Action.State.IAccountState accountState = world.GetAccountState(account);
            return accountState?.GetState(key);
        }

        public async Task<long> StageAndWaitAsync(
            Bencodex.Types.Dictionary actionPlainValue, TimeSpan timeout, CancellationToken ct)
        {
            PrivateKey playerKey = _playerKey
                ?? throw new InvalidOperationException("Client is not bootstrapped yet.");

            BlockChain? chain = _chain;
            if (chain is null)
            {
                throw new InvalidOperationException("Client is not bootstrapped yet.");
            }

            return await Task.Run(async () =>
            {
                Transaction tx = Transaction.Create(
                    nonce: chain.GetNextTxNonce(playerKey.Address),
                    privateKey: playerKey,
                    genesisHash: chain.Genesis.Hash,
                    actions: new[] { actionPlainValue });
                chain.StageTransaction(tx);
                Debug.Log($"[chain] tx staged {tx.Id}");

                // GetTxExecution(blockHash, txId) returns null unless THAT
                // block contains the tx. There is no GetBlock on BlockChain
                // 5.5.3 to walk backwards, so remember recent tip hashes and
                // probe each — the client syncs every mined block, so the tx's
                // block must pass through Tip within a few polls.
                var recentTipHashes = new List<BlockHash>(64);
                DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;

                while (DateTimeOffset.UtcNow < deadline)
                {
                    ct.ThrowIfCancellationRequested();

                    BlockHash tipHash = chain.Tip.Hash;
                    // BlockHash is a struct without operator!= — compare via
                    // Equals (hex string comparison would also work).
                    if (recentTipHashes.Count == 0 ||
                        !recentTipHashes[^1].Equals(tipHash))
                    {
                        recentTipHashes.Add(tipHash);
                        if (recentTipHashes.Count > 64)
                        {
                            recentTipHashes.RemoveAt(0);
                        }
                    }

                    foreach (BlockHash hash in recentTipHashes)
                    {
                        TxExecution? exec = chain.GetTxExecution(hash, tx.Id);
                        if (exec is null)
                        {
                            continue;
                        }

                        if (exec.Fail)
                        {
                            throw new ActionFailedException(
                                exec.ExceptionNames.FirstOrDefault() ?? "ActionFailed");
                        }

                        return chain.Tip.Index;
                    }

                    await Task.Delay(250, ct);
                }

                throw new TimeoutException(
                    $"Transaction {tx.Id} was not confirmed within {timeout.TotalSeconds:0}s.");
            }, ct);
        }

        private Block LoadOrCreateGenesis(string storeDir, string genesisPath)
        {
            if (File.Exists(genesisPath))
            {
                byte[] bytes = File.ReadAllBytes(genesisPath);
                var decoded = (Dictionary)new Bencodex.Codec().Decode(bytes);
                return BlockMarshaler.UnmarshalBlock(decoded);
            }

            // No genesis file: build one locally (solo mode). Same construction
            // as ProjectF.SeedNode's validator mode, written in the SAME format
            // as the seed's bootstrap files (BlockMarshaler, not Block.Marshal()).
            // ValidatorSet takes List<Validator> (reflection-verified).
            var validatorKey = new PrivateKey();
            var validatorSet = new Libplanet.Types.Consensus.ValidatorSet(
                new System.Collections.Generic.List<Libplanet.Types.Consensus.Validator>
                {
                    new(validatorKey.PublicKey, BigInteger.One),
                });

            IBlockPolicy policy = BlockPolicySource.GetPolicy();
            IActionLoader actionLoader = TypedActionLoader.Create(typeof(PingAction).Assembly);
            var actionEvaluator = new ActionEvaluator(
                policy.PolicyActionsRegistry,
                stateStore: new TrieStateStore(new MemoryKeyValueStore()),
                actionTypeLoader: actionLoader);

            GenesisContext genesis = GenesisBuilder.BuildGenesisContext(
                validatorKey, validatorSet, actionEvaluator);
            File.WriteAllBytes(
                Path.Combine(storeDir, "genesis.dat"),
                new Bencodex.Codec().Encode(
                    Libplanet.Types.Blocks.BlockMarshaler.MarshalBlock(genesis.GenesisBlock)));
            return genesis.GenesisBlock;
        }

        private static string? ReadTrimmed(string path) =>
            File.Exists(path) ? File.ReadAllText(path).Trim() : null;

        /// <summary>Process-wide OS thread count for shutdown evidence —
        /// untracked threads (NetMQ pollers, transport agents) never show up
        /// in the task registry. Process.Threads returns 0 on Unity Mono, so
        /// this walks the Win32 toolhelp snapshot instead. -1 when unavailable
        /// (non-Windows).</summary>
        private static int CountThreads()
        {
            try
            {
                const uint TH32CS_SNAPTHREAD = 0x4;
                IntPtr snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPTHREAD, 0);
                if (snapshot == IntPtr.Zero || snapshot == new IntPtr(-1))
                {
                    return -1;
                }

                try
                {
                    uint pid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
                    var entry = new THREADENTRY32
                    {
                        dwSize = (uint)System.Runtime.InteropServices.Marshal
                            .SizeOf(typeof(THREADENTRY32)),
                    };
                    int count = 0;
                    if (Thread32First(snapshot, ref entry))
                    {
                        do
                        {
                            if (entry.th32OwnerProcessID == pid)
                            {
                                count++;
                            }
                        }
                        while (Thread32Next(snapshot, ref entry));
                    }

                    return count;
                }
                finally
                {
                    CloseHandle(snapshot);
                }
            }
            catch
            {
                return -1;
            }
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool Thread32First(IntPtr snapshot, ref THREADENTRY32 entry);

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool Thread32Next(IntPtr snapshot, ref THREADENTRY32 entry);

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        [System.Runtime.InteropServices.StructLayout(
            System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct THREADENTRY32
        {
            public uint dwSize;
            public uint cntUsage;
            public uint th32ThreadID;
            public uint th32OwnerProcessID;
            public int tpBasePri;
            public int tpDeltaPri;
            public uint dwFlags;
        }

        /// <summary>Surface for an action that executed but FAILED on-chain —
        /// carries the exception type name(s) recorded in the execution.</summary>
        public sealed class ActionFailedException : Exception
        {
            public ActionFailedException(string message)
                : base(message)
            {
            }
        }

        // -------------------------------------------------------------------
        // Stage 11.5 — ordered async shutdown (spec 2):
        //   a. cancel the shared CTS,
        //   b. AWAIT the swarm task (this is the gossip/heartbeat loop that
        //      kept running into disposal and hung domain reloads) with a
        //      3s per-task timeout — plus Swarm.StopAsync as the polite
        //      request first,
        //   c. only THEN dispose the stores.
        // VContainer resolves ILibplanetClient as a sync-IDisposable
        // singleton; the Dispose() bridge below runs this on the threadpool
        // with a bounded wait during teardown.
        // -------------------------------------------------------------------
        public async Task DisposeAsync()
        {
            DateTimeOffset started = DateTimeOffset.UtcNow;

            // Stage 16 evidence: thread census before/after teardown — the
            // "Reloading Domain" stall happens AFTER the guard reports every
            // tracked task clean, so the culprit is threads the registry
            // cannot see (NetMQ poller/agents parked in native recv).
            int threadsBefore = CountThreads();

            // --- a. cancel -------------------------------------------------
            _cts.Cancel();

            // --- b. await every tracked background task ---------------------
            if (_swarm is { } swarm)
            {
                try
                {
                    // Politely ask the swarm to stop FIRST so StartAsync's
                    // internal loops unwind quickly instead of waiting for
                    // every transport op to notice the token.
                    await swarm.StopAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
                }
                catch
                {
                    // Best effort — the awaited task below is authoritative.
                }
            }

            // Await THIS client's tasks only — never another service's loop.
            // ConfigureAwait(false): the sync Dispose bridge blocks the main
            // thread — a sync-context continuation would deadlock (EditMode
            // 14s repro). Finish on the threadpool.
            await _cts.AwaitOwnedAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);

            // --- c. dispose the stores (last) -------------------------------
            try
            {
                _stateStore?.Dispose();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[chain] state store dispose failed: {ex.Message}");
            }

            try
            {
                _store?.Dispose();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[chain] store dispose failed: {ex.Message}");
            }

            _cts.Dispose();
            _swarm = null;
            _chain = null;

            // Stage 16 (the reload-hang culprit): Swarm.StopAsync stops
            // Libplanet's OWN loops, but NetMQ's internal poller/agent
            // threads park in native recv/send — invisible to
            // BackgroundTaskRegistry, which is why the guard can log "all
            // tracked tasks stopped cleanly" and the domain reload STILL
            // stalls. NetMQ's documented finalizer assist is Cleanup —
            // non-blocking (false): NEVER block=true on the main thread.
            try
            {
                NetMQ.NetMQConfig.Cleanup(false);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[chain] NetMQ cleanup failed: {ex.Message}");
            }

            int threadsAfter = CountThreads();
            string threads = threadsBefore > 0 && threadsAfter > 0
                ? $" — threads {threadsBefore}→{threadsAfter}"
                : string.Empty;
            Debug.Log($"[chain] shutdown complete in " +
                      $"{(DateTimeOffset.UtcNow - started).TotalMilliseconds:0}ms{threads}.");
        }

        public void Dispose()
        {
            // Sync bridge: Unity teardown (VContainer Dispose / editor guard)
            // is synchronous. Delegate to the async path and block up to 6s
            // (3s per-task await + margin) — the EditMode test proves this
            // completes far faster in the no-server case. Task.Run FIRST:
            // awaiting on the main thread captures the Unity sync context,
            // which is blocked in .Wait — deadlock until the timeout (the
            // EditMode 14s repro).
            try
            {
                Task.Run(() => DisposeAsync()).Wait(TimeSpan.FromSeconds(6));
            }
            catch (AggregateException ex) when (ex.InnerExceptions.All(i => i is TimeoutException))
            {
                Debug.LogWarning("[chain] shutdown did not finish within 6s — abandoned.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[chain] shutdown exception: {ex.Message}");
            }
        }
    }

    /// <summary>Display-only catch-up estimate for progress UI (spec 9.4 keeps
    /// this optional — "N / M" renders only while a peer-tip target is known;
    /// UI falls back to the honest "block N" line otherwise). NEVER used for
    /// gameplay decisions — the chain stays the sole authority.</summary>
    public readonly struct SyncProgress
    {
        /// <summary>Local tip index (0 before bootstrap).</summary>
        public long Tip { get; init; }

        /// <summary>Highest peer tip seen this session; 0 when unknown.</summary>
        public long TargetTip { get; init; }

        /// <summary>True while the local tip is behind the known target.</summary>
        public bool HasTarget { get; init; }
    }
}
