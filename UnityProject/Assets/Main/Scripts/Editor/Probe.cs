// Stage-1b pipeline-proof probe (gate 4, editor-side).
//
// Runs the full in-process pipeline inside Unity's Mono runtime using the
// synced netstandard2.1 DLLs: build genesis → sign a PingAction transaction
// → propose + append a block → read back the counter state.
//
// Executed via:
//   Unity.exe -batchmode -nographics -projectPath <proj>
//     -executeMethod ProjectF.Editor.Probe.Run -logFile <log> -quit
//
// The same flow against the live SeedNode (network hop) is exercised in the
// Editor GUI via ProjectF.Editor.ProbeMenu (menu item below).

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Threading;
using Bencodex.Types;
using Libplanet.Action;
using Libplanet.Action.Loader;
using Libplanet.Blockchain;
using Libplanet.Blockchain.Policies;
using Libplanet.Crypto;
using Libplanet.Store;
using Libplanet.Store.Trie;
using Libplanet.Types.Blocks;
using Libplanet.Types.Consensus;
using Libplanet.Types.Tx;
using ProjectF.Lib;
using ProjectF.Lib.Actions;
using ProjectF.Lib.Genesis;
using ProjectF.Lib.Policy;
using UnityEditor;
using UnityEngine;

namespace ProjectF.Editor
{
    // v4 — Bencodex.Json/ListSortHelper/Grpc.Core.Api/MagicOnion.Abstractions added.
    public static class Probe
    {
        public static void Run()
        {
            Debug.Log("[probe] starting — Unity runtime pipeline probe");
            try
            {
                long result = ExecutePipeline();
                Debug.Log($"[probe] PASS — ping counter after one block: {result}");
                Debug.Log("[probe] ping counter == 3 ⇒ Unity→sign→mine→state OK");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[probe] FAIL — {ex}");
                EditorApplication.Exit(1);
                return;
            }

            EditorApplication.Exit(0);
        }

        /// <summary>
        /// The pipeline, exercised against an in-memory chain (no network):
        /// identical to ProjectF.Lib.Tests.PipelineSmokeTests, but running
        /// under Unity's runtime — proving the synced DLLs load and execute.
        /// </summary>
        public static long ExecutePipeline()
        {
            var validatorKey = new PrivateKey();
            var player = new PrivateKey();

            IBlockPolicy policy = BlockPolicySource.GetPolicy();
            IActionLoader actionLoader =
                TypedActionLoader.Create(typeof(PingAction).Assembly);
            var stateStore = new TrieStateStore(new MemoryKeyValueStore());
            var actionEvaluator = new ActionEvaluator(
                policy.PolicyActionsRegistry,
                stateStore: stateStore,
                actionTypeLoader: actionLoader);

            var validatorSet = new ValidatorSet(
                new System.Collections.Generic.List<Validator>
                {
                    new(validatorKey.PublicKey, BigInteger.One),
                });

            GenesisContext genesis = GenesisBuilder.BuildGenesisContext(
                validatorKey, validatorSet, actionEvaluator);

            BlockChain chain = BlockChain.Create(
                policy,
                new VolatileStagePolicy(),
                new MemoryStore(),
                stateStore,
                genesis.GenesisBlock,
                actionEvaluator);

            Transaction tx = Transaction.Create(
                nonce: chain.GetNextTxNonce(player.Address),
                privateKey: player,
                genesisHash: chain.Genesis.Hash,
                actions: new[] { new PingAction(3).PlainValue });
            chain.StageTransaction(tx);

            Block block = chain.ProposeBlock(validatorKey);
            chain.Append(block, CreateBlockCommit(chain, validatorKey, validatorSet, block));

            var exec = chain.GetTxExecution(block.Hash, tx.Id);
            if (exec is null)
            {
                throw new InvalidOperationException("tx execution not recorded");
            }

            if (exec.Fail)
            {
                throw new InvalidOperationException("tx failed during evaluation");
            }

            Libplanet.Action.State.IAccountState? pingAccount =
                chain.GetNextWorldState()?.GetAccountState(Addresses.Ping);
            return pingAccount?.GetState(Addresses.PingCounter) is Integer value
                ? (long)value
                : 0L;
        }

        /// <summary>
        /// The NETWORK form of the checkpoint: join the live SeedNode's swarm,
        /// sign a PingAction transaction locally, broadcast it, and wait for
        /// the seed to mine it — then read the counter back from the synced
        /// chain. Unity-side pieces exercised: NetMQ transport under Mono,
        /// tx signing, staging/broadcast, block sync, state read.
        /// </summary>
        /// <param name="storeDir">The SeedNode's store dir containing the
        /// genesis.dat / peer.txt / privkey.txt bootstrap files.</param>
        public static long ExecuteNetworkPipeline(string storeDir)
        {
            // Runs on a threadpool thread: awaiting NetMQ/transport tasks from
            // Unity's main thread deadlocks (their continuations marshal back
            // to the UnitySynchronizationContext, which would be blocked).
            return System.Threading.Tasks.Task.Run(
                () => RunNetworkPipelineAsync(storeDir)).GetAwaiter().GetResult();
        }

        private static async System.Threading.Tasks.Task<long> RunNetworkPipelineAsync(
            string storeDir)
        {
            try
            {
                return await RunNetworkPipelineCoreAsync(storeDir);
            }
            catch (System.Exception ex)
            {
                // Surface the FULL inner exception (the MCP script wrapper
                // truncates TargetInvocationException chains).
                Debug.LogError($"[net-probe] FAILED: {ex}");
                throw;
            }
        }

        private static async System.Threading.Tasks.Task<long> RunNetworkPipelineCoreAsync(
            string storeDir)
        {
            Debug.Log("[net-probe] step 1: reading bootstrap files…");
            var seedKey = new PrivateKey(
                System.IO.File.ReadAllText(System.IO.Path.Combine(storeDir, "privkey.txt")).Trim());
            var seedPeer = Libplanet.Net.BoundPeer.ParsePeer(
                System.IO.File.ReadAllText(System.IO.Path.Combine(storeDir, "peer.txt")).Trim());
            var genesisBytes = System.IO.File.ReadAllBytes(
                System.IO.Path.Combine(storeDir, "genesis.dat"));
            Block genesis = BlockMarshaler.UnmarshalBlock(
                (Dictionary)new Bencodex.Codec().Decode(genesisBytes));
            Debug.Log($"[net-probe] genesis: {genesis.Hash} seed: {seedPeer.EndPoint}");

            var player = new PrivateKey();

            IBlockPolicy policy = BlockPolicySource.GetPolicy();
            IActionLoader actionLoader =
                TypedActionLoader.Create(typeof(PingAction).Assembly);

            // Persistent probe store, NAMESPACED BY GENESIS: a warm store
            // resumes from its tip (delta sync only), while a different chain
            // (e.g. after -FreshSeed) automatically gets a clean store — a
            // stale probe from a previous chain can no longer pollute it.
            var probeStoreDir = storeDir + "-probe-" +
                genesis.Hash.ToString().Substring(0, 8);
            System.IO.Directory.CreateDirectory(probeStoreDir);
            IStore store = new DefaultStore(probeStoreDir);
            var stateStore = new TrieStateStore(
                new DefaultKeyValueStore(probeStoreDir + "/states"));

            var actionEvaluator = new ActionEvaluator(
                policy.PolicyActionsRegistry,
                stateStore: stateStore,
                actionTypeLoader: actionLoader);

            Debug.Log($"[net-probe] step 2: evaluating genesis into {probeStoreDir}…");
            // A joining node receives the genesis block pre-signed with the
            // evaluated state root hash. Its own state store is EMPTY, so the
            // genesis states must be committed here first — otherwise
            // BlockChain.Create cannot resolve the root (the SeedNode never
            // hits this because its GenesisBuilder evaluated the genesis into
            // the same store instance).
            var genesisPreEval = new PreEvaluationBlock(
                genesis.Header,
                genesis.Transactions,
                System.Linq.Enumerable.Empty<Libplanet.Types.Evidence.EvidenceBase>());
            _ = actionEvaluator.Evaluate(genesisPreEval, null);

            Debug.Log("[net-probe] step 2b: creating chain from seed genesis…");
            // Mirror SeedNode.SwarmRunner.BootChain: a warm probe store already
            // holds the canonical chain — adopt it; only a fresh store creates.
            Guid? chainId = store.GetCanonicalChainId();
            if (chainId is null)
            {
                var existing = store.ListChainIds().ToList();
                if (existing.Count > 0)
                {
                    chainId = existing[0];
                    store.SetCanonicalChainId(chainId.Value);
                    Debug.Log($"[net-probe] adopted existing chain {chainId} as canonical");
                }
            }

            BlockChain chain = chainId is { }
                ? new BlockChain(
                    policy,
                    new VolatileStagePolicy(),
                    store,
                    stateStore,
                    genesis,
                    new Libplanet.Blockchain.BlockChainStates(store, stateStore),
                    actionEvaluator)
                : BlockChain.Create(
                    policy,
                    new VolatileStagePolicy(),
                    store,
                    stateStore,
                    genesis,
                    actionEvaluator);
            Debug.Log($"[net-probe] chain ready (tip #{chain.Tip.Index})");

            // Same AppProtocolVersion (signed by the seed key) on both sides so
            // no peer rejects the other's messages in this closed dev network.
            var apv = Libplanet.Net.AppProtocolVersion.Sign(seedKey, 1);
            var apvOptions = new Libplanet.Net.Options.AppProtocolVersionOptions
            {
                AppProtocolVersion = apv,
            };
            var hostOptions = new Libplanet.Net.Options.HostOptions(
                "127.0.0.1", System.Array.Empty<Libplanet.Net.IceServer>(), 0);
            var swarmOptions = new Libplanet.Net.Options.SwarmOptions
            {
                StaticPeers = System.Collections.Immutable.ImmutableHashSet.Create(seedPeer),
                TxBroadcastInterval = System.TimeSpan.FromMilliseconds(500),
            };

            Debug.Log("[net-probe] step 3: creating transport…");
            var transport = await Libplanet.Net.Transports.NetMQTransport.Create(
                player, apvOptions, hostOptions);
            Debug.Log("[net-probe] transport ready");
            var swarm = new global::Libplanet.Net.Swarm(
                chain, player, transport, swarmOptions, consensusTransport: null);
            Debug.Log("[net-probe] swarm constructed");

            // Session budget covers transport + bootstrap + full preload +
            // mining poll. A COLD probe store preloading a multi-thousand-block
            // chain at Mono pace has measured >15 min; warm stores only sync
            // the delta. Use -FreshSeed (scripts/run-netprobe.ps1) to keep
            // chains short when a fast clean spike is wanted.
            using var cts = new CancellationTokenSource(System.TimeSpan.FromSeconds(2400));
            _ = System.Threading.Tasks.Task.Run(
                () => swarm.StartAsync(cts.Token), cts.Token);
            // Task.WaitAsync is .NET 6+; blocking Wait is fine here — this code
            // runs on a threadpool thread with no SynchronizationContext.
            if (!swarm.WaitForRunningAsync().Wait(System.TimeSpan.FromSeconds(15)))
            {
                throw new TimeoutException("swarm transport did not start in 15s");
            }

            Debug.Log("[net-probe] swarm running, bootstrapping to seed…");
            if (!swarm.BootstrapAsync(
                    new[] { seedPeer },
                    System.TimeSpan.FromSeconds(5),
                    3,
                    cts.Token).Wait(System.TimeSpan.FromSeconds(20)))
            {
                Debug.LogWarning("[net-probe] bootstrap did not complete in 20s (continuing)");
            }

            Debug.Log($"[net-probe] bootstrap done, peers: {swarm.Peers.Count}");

            // Bulk catch-up: the seed may be thousands of blocks ahead. Gossip
            // only carries new blocks, so a fresh node must pull the whole
            // range (blocks + states) before it can read tip state. Must be
            // FULLY complete before staging a tx (it mutates the chain).
            Debug.Log("[net-probe] step 4: preloading chain from seed…");
            var preloadClock = System.Diagnostics.Stopwatch.StartNew();
            var progress = new System.Progress<Libplanet.Net.BlockSyncState>(s =>
            {
                // Duck-typed counters: surface whatever block counts the
                // runtime exposes without hard-coding property names.
                foreach (var prop in s.GetType().GetProperties())
                {
                    if (prop.Name.Contains("BlockCount") || prop.Name.Contains("ReceivedBlockCount"))
                    {
                        try
                        {
                            Debug.Log($"[net-probe] preload {prop.Name}={prop.GetValue(s)} " +
                                      $"({preloadClock.ElapsedMilliseconds / 1000.0:F0}s)");
                        }
                        catch
                        {
                            // progress properties are best-effort only
                        }
                    }
                }
            });

            // A single PreloadAsync pass can complete EARLY on a long chain
            // (observed: returned at #1497 while the seed was at #3960) — the
            // node then lacks the tail blocks and neither gossip nor the state
            // read can see fresh txs. Keep running passes while they make
            // forward progress; later passes pick up where the last stopped,
            // and the seed's own new blocks during the passes keep the final
            // gap small enough for gossip to bridge.
            var passStopwatch = System.Diagnostics.Stopwatch.StartNew();
            int pass = 0;
            long lastTip;
            long currentTip = chain.Tip.Index;   // Block.Index is long in 5.5.3
            do
            {
                lastTip = currentTip;
                await swarm.PreloadAsync(progress, cts.Token);
                currentTip = chain.Tip.Index;
                pass++;
                Debug.Log($"[net-probe] preload pass {pass} done, tip: #{currentTip} " +
                          $"(+{currentTip - lastTip}, {passStopwatch.ElapsedMilliseconds / 1000.0:F0}s total)");
            }
            while (currentTip > lastTip && pass < 8 && !cts.Token.IsCancellationRequested);

            Debug.Log($"[net-probe] preload finished after {pass} pass(es), tip: #{chain.Tip.Index}");

            // Stage 2.5 criterion: the counter must INCREASE from the value
            // read right before this run's tx — the chain accumulates prior
            // runs' pings, so an absolute threshold proves nothing.
            long baseline = ReadCounter(chain);
            LastBaselineCounter = baseline;
            Debug.Log($"[net-probe] baseline counter: {baseline}");

            // Sign locally and stage — the swarm broadcasts staged txs.
            Transaction tx = Transaction.Create(
                nonce: chain.GetNextTxNonce(player.Address),
                privateKey: player,
                genesisHash: chain.Genesis.Hash,
                actions: new[] { new PingAction(3).PlainValue });
            chain.StageTransaction(tx);
            Debug.Log($"[net-probe] tx staged: {tx.Id}");

            // Wait for the seed to mine the tx and sync the block back. If the
            // counter does not move, run one more preload pass (a late gap may
            // remain) and keep polling briefly before giving up.
            var deadline = System.DateTimeOffset.UtcNow + System.TimeSpan.FromSeconds(60);
            long counter = baseline;
            while (System.DateTimeOffset.UtcNow < deadline)
            {
                counter = ReadCounter(chain);
                if (counter > baseline)
                {
                    break;
                }

                await System.Threading.Tasks.Task.Delay(500);
            }

            if (counter <= baseline)
            {
                Debug.Log("[net-probe] counter flat - one recovery preload pass…");
                await swarm.PreloadAsync(progress, cts.Token);
                Debug.Log($"[net-probe] recovery tip: #{chain.Tip.Index}");
                var recoveryDeadline = System.DateTimeOffset.UtcNow + System.TimeSpan.FromSeconds(30);
                while (System.DateTimeOffset.UtcNow < recoveryDeadline)
                {
                    counter = ReadCounter(chain);
                    if (counter > baseline)
                    {
                        break;
                    }

                    await System.Threading.Tasks.Task.Delay(500);
                }
            }

            Debug.Log($"[net-probe] final counter: {counter} (baseline {baseline}, delta {counter - baseline})");
            try
            {
                await swarm.StopAsync(System.TimeSpan.FromSeconds(3));
            }
            catch
            {
                // Best-effort shutdown.
            }

            transport.Dispose();
            swarm.Dispose();
            return counter;
        }

        /// <summary>Counter value read right before the last run's tx was staged
        /// (Stage 2.5: success = final counter strictly greater than this).</summary>
        public static long LastBaselineCounter { get; private set; }

        /// <summary>
        /// Batchmode entry point for the network probe (headless CI run):
        /// exit code 0 = counter increased over baseline, 2 = synced but no
        /// increase, 1 = failure.
        /// </summary>
        public static void RunNetworkBatch()
        {
            var storeDir = System.Environment.GetEnvironmentVariable("PF_SEED_STORE")
                ?? "C:/Users/memph/AppData/Local/Temp/pf-seed";
            try
            {
                long counter = ExecuteNetworkPipeline(storeDir);
                bool increased = counter > LastBaselineCounter;
                Debug.Log($"[net-batch] RESULT: counter={counter} baseline={LastBaselineCounter} " +
                          $"increased={increased} (expected increase)");
                EditorApplication.Exit(increased ? 0 : 2);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[net-batch] FAILED: {ex}");
                EditorApplication.Exit(1);
            }
        }

        private static long ReadCounter(BlockChain chain)
        {
            Libplanet.Action.State.IAccountState? pingAccount =
                chain.GetNextWorldState()?.GetAccountState(Addresses.Ping);
            return pingAccount?.GetState(Addresses.PingCounter) is Integer value
                ? (long)value
                : 0L;
        }

        private static BlockCommit CreateBlockCommit(
            BlockChain chain, PrivateKey key, ValidatorSet validatorSet, Block block)
        {
            if (block.Index <= 0)
            {
                return null!;
            }

            BigInteger power = validatorSet.GetValidator(key.PublicKey).Power;
            var vote = new VoteMetadata(
                block.Index,
                0,
                block.Hash,
                DateTimeOffset.UtcNow,
                key.PublicKey,
                power,
                VoteFlag.PreCommit).Sign(key);

            return new BlockCommit(
                block.Index, 0, block.Hash, ImmutableArray.Create(vote));
        }
    }
}
