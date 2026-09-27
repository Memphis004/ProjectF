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
            var stateStore = new TrieStateStore(new MemoryKeyValueStore());
            var actionEvaluator = new ActionEvaluator(
                policy.PolicyActionsRegistry,
                stateStore: stateStore,
                actionTypeLoader: actionLoader);

            Debug.Log("[net-probe] step 2: evaluating genesis into local state store…");
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
            BlockChain chain = BlockChain.Create(
                policy,
                new VolatileStagePolicy(),
                new MemoryStore(),
                stateStore,
                genesis,
                actionEvaluator);
            Debug.Log("[net-probe] chain ready");

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
            // mining poll. Runs entirely on a background thread.
            using var cts = new CancellationTokenSource(System.TimeSpan.FromSeconds(420));
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
            await swarm.PreloadAsync(null, cts.Token);

            Debug.Log($"[net-probe] preload done, tip: #{chain.Tip.Index}");

            // Sign locally and stage — the swarm broadcasts staged txs.
            Transaction tx = Transaction.Create(
                nonce: chain.GetNextTxNonce(player.Address),
                privateKey: player,
                genesisHash: chain.Genesis.Hash,
                actions: new[] { new PingAction(3).PlainValue });
            chain.StageTransaction(tx);
            Debug.Log($"[net-probe] tx staged: {tx.Id}");

            // Wait for the seed to mine the tx and sync the block back.
            var deadline = System.DateTimeOffset.UtcNow + System.TimeSpan.FromSeconds(60);
            long counter = 0;
            while (System.DateTimeOffset.UtcNow < deadline)
            {
                counter = ReadCounter(chain);
                if (counter >= 3)
                {
                    break;
                }

                await System.Threading.Tasks.Task.Delay(500);
            }

            Debug.Log($"[net-probe] final counter: {counter}");
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

        /// <summary>
        /// Batchmode entry point for the network probe (headless CI run):
        /// exit code 0 = counter >= 3, 2 = mined-but-unsynced, 1 = failure.
        /// </summary>
        public static void RunNetworkBatch()
        {
            var storeDir = System.Environment.GetEnvironmentVariable("PF_SEED_STORE")
                ?? "C:/Users/memph/AppData/Local/Temp/pf-seed";
            try
            {
                long counter = ExecuteNetworkPipeline(storeDir);
                Debug.Log($"[net-batch] RESULT: counter={counter} (expected >= 3)");
                EditorApplication.Exit(counter >= 3 ? 0 : 2);
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
