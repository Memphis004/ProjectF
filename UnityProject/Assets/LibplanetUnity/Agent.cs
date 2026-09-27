using Bencodex.Types;
using Libplanet;
using Libplanet.Action;
using Libplanet.Blockchain;
using Libplanet.Blockchain.Renderers;
using Libplanet.Blocks;
using Libplanet.Crypto;
using Libplanet.Net;
using Libplanet.Store;
using Libplanet.Tx;
using LibplanetUnity.Action;
using LibplanetUnity.Helper;
using NetMQ;
using Serilog;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Libplanet.Node;
using UnityEngine;
using UnityEditor;

namespace LibplanetUnity
{
    public class Agent : MonoSingleton<Agent>
    {
        private const string StoreDir = "planetarium";

        private static readonly string CommandLineOptionsJsonPath =
            Path.Combine(Application.streamingAssetsPath, "command_line_options.json");

        public static readonly string GenesisBlockPath =
            Path.Combine(Application.streamingAssetsPath, "genesis");

        public static readonly string SwarmConfigPath =
            Path.Combine(Application.streamingAssetsPath, "swarm_config.json");

        public static readonly string DefaultPrivateKeyPath =
            Path.Combine(Application.persistentDataPath, "private_key");

        public static readonly string DefaultStoragePath =
            Path.Combine(Application.persistentDataPath, StoreDir);

        private static IEnumerator _miner;

        private static IEnumerator _swarmRunner;

        private readonly ConcurrentQueue<System.Action> _actions = new ConcurrentQueue<System.Action>();

        // Set by the sign-in screen before Agent.Initialize() is called.  When present it
        // takes precedence over --private-key, clo.json, and the persistentDataPath file.
        private static PrivateKey _pendingPrivateKey;

        private static IReadOnlyList<BoundPeer> _seedPeers = new List<BoundPeer>();

        private PrivateKey PrivateKey { get; set; }

        private NodeConfig<PolymorphicAction<ActionBase>> _nodeConfig;

        private Swarm<PolymorphicAction<ActionBase>> _swarm;

        private BlockChain<PolymorphicAction<ActionBase>> _blockChain;

        private CancellationTokenSource _cancellationTokenSource;

        public Address Address { get; private set; }

        public IEnumerable<IRenderer<PolymorphicAction<ActionBase>>> Renderers { get; private set; }

        /// <summary>
        /// Index of the current chain tip, or <c>-1</c> while the chain is not ready.
        /// Safe to read from the main thread (e.g., by UI code polling it).
        /// </summary>
        public long TipIndex => _blockChain?.Tip?.Index ?? -1;

        /// <summary>
        /// Number of peers currently known to this node's swarm (excluding itself).
        /// Safe to read from the main thread; returns <c>0</c> while the swarm is
        /// starting up or shutting down.
        /// </summary>
        public int PeerCount
        {
            get
            {
                var swarm = _swarm;
                if (ReferenceEquals(swarm, null))
                {
                    return 0;
                }

                try
                {
                    return swarm.Peers.Count;
                }
                catch (Exception)
                {
                    // The routing table is transiently unavailable during startup/teardown.
                    return 0;
                }
            }
        }

        /// <summary>
        /// True when this node mines blocks (i.e., it was started without --no-miner).
        /// </summary>
        public bool IsMiner => !ReferenceEquals(_miner, null);

        /// <summary>
        /// True once the swarm has finished starting and is exchanging messages.
        /// </summary>
        public bool IsSwarmRunning => _swarm?.Running ?? false;

        public static void Initialize(IEnumerable<IRenderer<PolymorphicAction<ActionBase>>> renderers)
        {
            instance.InitAgent(renderers);
        }

        /// <summary>
        /// Provides the private key obtained through the sign-in screen.  Must be called
        /// before <see cref="Initialize"/>.  This key wins over every other source.
        /// </summary>
        public static void SetPendingPrivateKey(PrivateKey privateKey)
        {
            _pendingPrivateKey = privateKey ?? throw new ArgumentNullException(nameof(privateKey));
        }

        /// <summary>
        /// True when <see cref="SetPendingPrivateKey"/> was called but <see cref="Initialize"/>
        /// has not consumed the key yet.
        /// </summary>
        public static bool HasPendingPrivateKey => !ReferenceEquals(_pendingPrivateKey, null);

        public static void CreateSwarmConfig()
        {
            SwarmConfig swarmConfig = new SwarmConfig();
            File.Delete(SwarmConfigPath);
            File.WriteAllText(SwarmConfigPath, swarmConfig.ToJson());
        }

        public static void CreateGenesisBlock()
        {
            Block<PolymorphicAction<ActionBase>> genesisBlock =
                NodeUtils<PolymorphicAction<ActionBase>>.CreateGenesisBlock();
            File.Delete(GenesisBlockPath);
            NodeUtils<PolymorphicAction<ActionBase>>.SaveGenesisBlock(GenesisBlockPath, genesisBlock);
        }

        public static void CreatePrivateKey()
        {
            PrivateKey privateKey = new PrivateKey();
            File.Delete(DefaultPrivateKeyPath);
            NodeUtils<PolymorphicAction<ActionBase>>.SavePrivateKey(DefaultPrivateKeyPath, privateKey);
        }

        public IValue GetState(Address address)
        {
            return _blockChain.GetState(address);
        }

        public void MakeTransaction(IEnumerable<ActionBase> gameActions)
        {
            var actions = gameActions.Select(gameAction => (PolymorphicAction<ActionBase>)gameAction).ToList();
            Task.Run(() => MakeTransaction(actions, true));
        }

        private void InitAgent(IEnumerable<IRenderer<PolymorphicAction<ActionBase>>> renderers)
        {
            var options = GetOptions(CommandLineOptionsJsonPath);
            var storagePath = options.StoragePath ?? DefaultStoragePath;
            var appProtocolVersion = options.AppProtocolVersion is null
                ? default
                : AppProtocolVersion.FromToken(options.AppProtocolVersion);
            var trustedAppProtocolVersionSigners = options.TrustedAppProtocolVersionSigners
                .Select(s => new PublicKey(ByteUtil.ParseHex(s)));

            if (options.Logging)
            {
                Log.Logger = new LoggerConfiguration()
                    .MinimumLevel.Debug()
                    .WriteTo.Console()
                    .CreateLogger();
            }

            Init(
                storagePath,
                appProtocolVersion,
                trustedAppProtocolVersionSigners,
                renderers,
                options);

            _miner = options.NoMiner ? null : CoMiner();

            StartSystemCoroutines();
            StartNullableCoroutine(_miner);
        }

        private void Init(
            string storagePath,
            AppProtocolVersion appProtocolVersion,
            IEnumerable<PublicKey> trustedAppProtocolVersionSigners,
            IEnumerable<IRenderer<PolymorphicAction<ActionBase>>> renderers,
            Options options)
        {
            SwarmConfig swarmConfig = GetSwarmConfig();
            Block<PolymorphicAction<ActionBase>> genesis = GetGenesisBlock();
            (IStore store, IStateStore stateStore) = NodeUtils<PolymorphicAction<ActionBase>>.LoadStore(storagePath);

            // The identity used for signing transactions and the swarm identity are
            // intentionally the same now; the sign-in screen (or command line) decides it.
            PrivateKey privateKey = ResolvePrivateKey();
            PrivateKey = privateKey;
            Address = privateKey.PublicKey.ToAddress();

            _nodeConfig = new NodeConfig<PolymorphicAction<ActionBase>>(
                privateKey,
                new NetworkConfig<PolymorphicAction<ActionBase>>(
                    NodeUtils<PolymorphicAction<ActionBase>>.DefaultBlockPolicy,
                    NodeUtils<PolymorphicAction<ActionBase>>.DefaultStagePolicy,
                    genesis),
                swarmConfig,
                store,
                stateStore,
                renderers);
            _nodeConfig.SwarmConfig.InitConfig.Host =
                string.IsNullOrWhiteSpace(options.Host) ? "127.0.0.1" : options.Host.Trim();
            if (options.Port > 0)
            {
                _nodeConfig.SwarmConfig.InitConfig.Port = options.Port;
            }

            var seedPeers = new List<BoundPeer>();
            foreach (string peerString in options.Peers ?? new string[] { })
            {
                if (string.IsNullOrWhiteSpace(peerString))
                {
                    continue;
                }

                try
                {
                    seedPeers.Add(BoundPeer.ParsePeer(peerString.Trim()));
                }
                catch (Exception e)
                {
                    Debug.LogError($"Failed to parse peer string \"{peerString}\": {e}");
                }
            }

            _seedPeers = seedPeers;
            Debug.LogFormat(
                "Agent initialized. Address: {0}, Host: {1}, Port: {2}, Seed peers: {3}",
                Address.ToString(),
                _nodeConfig.SwarmConfig.InitConfig.Host,
                _nodeConfig.SwarmConfig.InitConfig.Port?.ToString() ?? "auto",
                seedPeers.Count);

            _swarm = _nodeConfig.GetSwarm();
            _blockChain = _swarm.BlockChain;

            // A node that never mines anything has no canonical chain in its store yet,
            // which makes PreloadAsync fail with ChainIdNotFoundException on the very
            // first sync.  Bootstrap the genesis block so the store is initialized.
            try
            {
                if (!ReferenceEquals(_blockChain.Genesis, null) &&
                    store.GetCanonicalChainId() is null)
                {
                    _blockChain.Append(_blockChain.Genesis);
                    Debug.Log("Initialized the store with the genesis block.");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Could not initialize the store with the genesis block: {e.Message}");
            }

            _cancellationTokenSource = new CancellationTokenSource();
        }

        private static Options GetOptions(string jsonPath)
        {
            if (File.Exists(jsonPath))
            {
                return JsonUtility.FromJson<Options>(
                    File.ReadAllText(jsonPath)
                );
            }
            else
            {
                return CommnadLineParser.GetCommandLineOptions() ?? new Options();
            }
        }

        /// <summary>
        /// Determines this node's private key with the following precedence:
        /// 1. The key set by the sign-in screen (<see cref="SetPendingPrivateKey"/>).
        /// 2. The --private-key command line option / clo.json "privateKey" field.
        /// 3. The previously saved key at persistentDataPath/private_key, if any.
        /// 4. A freshly generated key (which is best-effort persisted for next launch).
        /// </summary>
        private static PrivateKey ResolvePrivateKey()
        {
            if (!ReferenceEquals(_pendingPrivateKey, null))
            {
                PrivateKey pending = _pendingPrivateKey;
                _pendingPrivateKey = null;
                Debug.Log($"Using private key provided by the sign-in screen. (Address: {pending.PublicKey.ToAddress()})");
                return pending;
            }

            Options options = GetOptions(CommandLineOptionsJsonPath);
            if (!string.IsNullOrWhiteSpace(options.PrivateKey))
            {
                try
                {
                    var key = new PrivateKey(ByteUtil.ParseHex(options.PrivateKey.Trim()));
                    Debug.Log($"Using private key from --private-key / clo.json. (Address: {key.PublicKey.ToAddress()})");
                    return key;
                }
                catch (Exception e)
                {
                    Debug.LogError($"Failed to parse --private-key / clo.json privateKey: {e}");
                }
            }

            if (File.Exists(DefaultPrivateKeyPath))
            {
                try
                {
                    var key = NodeUtils<PolymorphicAction<ActionBase>>.LoadPrivateKey(DefaultPrivateKeyPath);
                    Debug.Log($"Using saved private key from {DefaultPrivateKeyPath}. (Address: {key.PublicKey.ToAddress()})");
                    return key;
                }
                catch (Exception e)
                {
                    Debug.LogError($"Failed to load private key file {DefaultPrivateKeyPath}: {e}. A new key will be generated.");
                }
            }

            var generated = new PrivateKey();
            Debug.Log($"No explicit private key configured. Generated a fresh key. (Address: {generated.PublicKey.ToAddress()})");
            try
            {
                NodeUtils<PolymorphicAction<ActionBase>>.SavePrivateKey(DefaultPrivateKeyPath, generated);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Could not persist generated private key to {DefaultPrivateKeyPath}: {e}");
            }

            return generated;
        }

        public void RunOnMainThread(System.Action action)
        {
            _actions.Enqueue(action);
        }

        private static SwarmConfig GetSwarmConfig()
        {
            if (!File.Exists(SwarmConfigPath))
            {
                CreateSwarmConfig();
            }

            return SwarmConfig.FromJson(File.ReadAllText(SwarmConfigPath));
        }

        private static Block<PolymorphicAction<ActionBase>> GetGenesisBlock()
        {
            if (!File.Exists(GenesisBlockPath))
            {
                CreateGenesisBlock();
            }

            return NodeUtils<PolymorphicAction<ActionBase>>.LoadGenesisBlock(GenesisBlockPath);
        }

        #region Mono

        protected override void OnDestroy()
        {
            NetMQConfig.Cleanup(false);

            base.OnDestroy();
            _swarm?.Dispose();
        }

        #endregion

        private void StartSystemCoroutines()
        {
            _swarmRunner = CoSwarmRunner();

            StartNullableCoroutine(_swarmRunner);
            StartCoroutine(CoProcessActions());
        }

        private Coroutine StartNullableCoroutine(IEnumerator routine)
        {
            return ReferenceEquals(routine, null) ? null : StartCoroutine(routine);
        }

        private IEnumerator CoSwarmRunner()
        {
            if (_swarm is null)
            {
                yield break;
            }

            var bootstrapTask = Task.Run(async () =>
            {
                try
                {
                    // FIXME: Swarm<T> should handle the filtering internally.
                    await _swarm.BootstrapAsync(
                        _seedPeers,
                        dialTimeout: TimeSpan.FromSeconds(5),
                        searchDepth: 1,
                        cancellationToken: _cancellationTokenSource.Token);
                }
                catch (Exception e)
                {
                    Debug.LogFormat("Exception occurred during bootstrap {0}", e);
                }
            });

            yield return new WaitUntil(() => bootstrapTask.IsCompleted);

            Debug.Log("PreloadingStarted event was invoked");

            DateTimeOffset started = DateTimeOffset.UtcNow;
            long existingBlocks = _blockChain?.Tip?.Index ?? 0;
            Debug.Log("Starting preload...");

            var swarmPreloadTask = Task.Run(async () =>
            {
                await _swarm.PreloadAsync(
                    progress: null,
                    render: false,
                    cancellationToken: _cancellationTokenSource.Token);
            });

            yield return new WaitUntil(() => swarmPreloadTask.IsCompleted);
            DateTimeOffset ended = DateTimeOffset.UtcNow;

            if (swarmPreloadTask.Exception is Exception exc)
            {
                // Preload can legitimately fail here: forking a genesis-only chain
                // raises ChainIdNotFoundException inside PreloadAsync.  This is not
                // fatal — the swarm still needs to start so demand-driven block sync,
                // block broadcasts, and transactions can flow.  Log and continue.
                Debug.LogWarningFormat(
                    "Preload terminated with an exception (continuing anyway): {0}",
                    exc
                );
            }

            var index = _blockChain?.Tip?.Index ?? 0;
            Debug.LogFormat(
                "Preload finished; elapsed time: {0}; blocks: {1}",
                ended - started,
                index - existingBlocks
            );

            var swarmStartTask = Task.Run(async () =>
            {
                try
                {
                    await _swarm.StartAsync(cancellationToken: _cancellationTokenSource.Token);
                }
                catch (TaskCanceledException)
                {
                }
                catch (Exception e)
                {
                    Debug.LogErrorFormat(
                        "Swarm terminated with an exception: {0}",
                        e
                    );
                    throw;
                }
            });

            Task.Run(async () =>
            {
                await _swarm.WaitForRunningAsync();

                Debug.LogFormat(
                    "The address of this node: {0},{1},{2}",
                    ByteUtil.Hex(PrivateKey.PublicKey.Format(true)),
                    _swarm.EndPoint.Host,
                    _swarm.EndPoint.Port
                );
            });

            yield return new WaitUntil(() => swarmStartTask.IsCompleted);
        }

        private IEnumerator CoProcessActions()
        {
            while (true)
            {
                if (_actions.TryDequeue(out System.Action action))
                {
                    action();
                }
                yield return new WaitForSeconds(0.1f);
            }
        }

        private static bool WantsToQuit()
        {
            return true;
        }

        [RuntimeInitializeOnLoadMethod]
        private static void RunOnStart()
        {
            Application.wantsToQuit += WantsToQuit;
        }

        private Transaction<PolymorphicAction<ActionBase>> MakeTransaction(
                    IEnumerable<PolymorphicAction<ActionBase>> actions, bool broadcast)
        {
            var polymorphicActions = actions.ToArray();
            Debug.LogFormat("Make Transaction with Actions: `{0}`",
                string.Join(",", polymorphicActions.Select(i => i.InnerAction)));
            return _blockChain.MakeTransaction(PrivateKey, polymorphicActions);
        }

        private IEnumerator CoMiner()
        {
            while (true)
            {
                var txs = new HashSet<Transaction<PolymorphicAction<ActionBase>>>();

                var task = Task.Run(async () =>
                {
                    var block = await _blockChain.MineBlock(PrivateKey);

                    if (_swarm?.Running ?? false)
                    {
                        _swarm.BroadcastBlock(block);
                    }

                    return block;
                });
                yield return new WaitUntil(() => task.IsCompleted);

                if (!task.IsCanceled && !task.IsFaulted)
                {
                    var block = task.Result;
                    Debug.Log($"created block index: {block.Index}, difficulty: {block.Difficulty}");
                }
                else
                {
                    var invalidTxs = txs;
                    var retryActions = new HashSet<IImmutableList<PolymorphicAction<ActionBase>>>();
                    var tipRace = false;

                    if (task.IsFaulted)
                    {
                        foreach (var ex in task.Exception.InnerExceptions)
                        {
                            if (ex is InvalidTxNonceException invalidTxNonceException)
                            {
                                var invalidNonceTx = _blockChain.GetTransaction(invalidTxNonceException.TxId);

                                if (invalidNonceTx.Signer == Address)
                                {
                                    Debug.Log($"Tx[{invalidTxNonceException.TxId}] nonce is invalid. Retry it.");
                                    retryActions.Add(invalidNonceTx.Actions);
                                }
                            }

                            if (ex is InvalidTxException invalidTxException)
                            {
                                Debug.Log($"Tx[{invalidTxException.TxId}] is invalid. mark to unstage.");
                                invalidTxs.Add(_blockChain.GetTransaction(invalidTxException.TxId));
                            }

                            // The chain tip moved (a synced block arrived or a concurrent
                            // mining pass appended) while this block was being mined.
                            // This is benign; mine again against the new tip instead of
                            // dropping the whole mining pass.
                            if (ex is InvalidBlockIndexException ||
                                ex.InnerException is InvalidBlockIndexException ||
                                ex is OperationCanceledException ||
                                ex.InnerException is OperationCanceledException)
                            {
                                Debug.Log("Tip changed while mining. Retrying against the new tip.");
                                tipRace = true;
                            }
                            else
                            {
                                Debug.LogException(ex);
                            }
                        }
                    }

                    foreach (var invalidTx in invalidTxs)
                    {
                        _blockChain.UnstageTransaction(invalidTx);
                    }

                    foreach (var retryAction in retryActions)
                    {
                        MakeTransaction(retryAction, true);
                    }

                    if (tipRace)
                    {
                        continue;
                    }
                }
            }
        }
    }
}
