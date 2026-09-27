using System;
using System.Collections.Immutable;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Libplanet.Action;
using Libplanet.Action.Loader;
using Libplanet.Blockchain;
using Libplanet.Blockchain.Policies;
using Libplanet.Crypto;
using Libplanet.Net;
using Libplanet.Net.Options;
using Libplanet.Net.Transports;
using Libplanet.Store;
using Libplanet.Store.Trie;
using Libplanet.Types.Blocks;
using Libplanet.Types.Consensus;
using ProjectF.Lib.Genesis;
using ProjectF.Lib.Policy;

namespace ProjectF.SeedNode;

/// <summary>
/// Boots the ProjectF chain (genesis persisted with canonical-chain-id
/// fallback — the planet-clicker devlog's ChainIdNotFoundException fix) and
/// runs the P2P swarm.
/// </summary>
public sealed class SwarmRunner : IAsyncDisposable
{
    private readonly NodeOptions _options;
    private readonly PrivateKey _nodeKey;

    public SwarmRunner(NodeOptions options, PrivateKey nodeKey)
    {
        _options = options;
        _nodeKey = nodeKey;
    }

    public BlockChain Chain { get; private set; } = null!;

    public ValidatorSet ValidatorSet { get; private set; } = null!;

    public Swarm? Swarm { get; private set; }

    private Task _swarmTask = Task.CompletedTask;

    /// <summary>The node's key — also the chain validator/miner key.</summary>
    public PrivateKey NodeKey => _nodeKey;

    /// <summary>Copy-pasteable peer string for Unity's NetworkSettings.</summary>
    public string PeerInfo =>
        PeerString.Format(_nodeKey.PublicKey, _options.Host, _options.Port);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        IBlockPolicy policy = BlockPolicySource.GetPolicy();
        IActionLoader actionLoader = TypedActionLoader.Create(
            typeof(ProjectF.Lib.Actions.PingAction).Assembly);

        (IStore store, TrieStateStore stateStore) = OpenStores(_options.StorePath);
        var actionEvaluator = new ActionEvaluator(
            policy.PolicyActionsRegistry,
            stateStore: stateStore,
            actionTypeLoader: actionLoader);

        ValidatorSet = new ValidatorSet(
            new System.Collections.Generic.List<Validator>
            {
                new(_nodeKey.PublicKey, BigInteger.One),
            });

        // The genesis tx signer must be the chain validator; for the closed
        // test network the node key IS the validator key.
        GenesisContext genesis = GenesisBuilder.BuildGenesisContext(
            _nodeKey,
            ValidatorSet,
            actionEvaluator);

        Chain = BootChain(store, stateStore, policy, actionEvaluator, genesis);

        Console.WriteLine($"Genesis block: {Chain.Genesis.Hash}");
        Console.WriteLine($"Node address:  {_nodeKey.Address}");
        Console.WriteLine("[boot] chain ready");

        // Dev convenience: shareable bootstrapping artifacts. A joining node
        // (Unity) cannot reconstruct the genesis deterministically — it needs
        // the block bytes plus the peer string. DEV-ONLY: privkey.txt lets the
        // Unity probe present the same AppProtocolVersion as the seed.
        if (!string.IsNullOrWhiteSpace(_options.StorePath))
        {
            var codec = new Bencodex.Codec();
            System.IO.File.WriteAllBytes(
                System.IO.Path.Combine(_options.StorePath, "genesis.dat"),
                codec.Encode(Libplanet.Types.Blocks.BlockMarshaler.MarshalBlock(Chain.Genesis)));
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(_options.StorePath, "peer.txt"), PeerInfo);
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(_options.StorePath, "privkey.txt"),
                Convert.ToHexString(_nodeKey.ByteArray.ToArray()));
            Console.WriteLine($"Bootstrap files written to {_options.StorePath}");
        }

        // ---- Swarm ----
        var hostOptions = new HostOptions(
            _options.Host, Array.Empty<IceServer>(), _options.Port);
        var appProtocolVersionOptions = new AppProtocolVersionOptions
        {
            AppProtocolVersion = AppProtocolVersion.Sign(_nodeKey, 1),
        };
        var swarmOptions = new SwarmOptions
        {
            StaticPeers = _options.StaticPeers
                .Select(PeerString.Parse)
                .ToImmutableHashSet(),
        };

        Console.WriteLine("[boot] creating transport…");
        ITransport transport = await NetMQTransport.Create(
            _nodeKey,
            appProtocolVersionOptions,
            hostOptions);
        Console.WriteLine("[boot] transport ready");
        Swarm = new global::Libplanet.Net.Swarm(
            Chain,
            _nodeKey,
            transport,
            swarmOptions,
            consensusTransport: null);

        Console.WriteLine("[boot] starting swarm…");
        // Libplanet 5.x Swarm.StartAsync() is long-running: it does NOT return
        // until the swarm stops (its final await waits on the internal
        // broadcast/poll loops). Fire it off and wait for the transport to
        // bind instead.
        _swarmTask = RunSwarmAsync(cancellationToken);
        try
        {
            await Swarm!.WaitForRunningAsync().WaitAsync(TimeSpan.FromSeconds(15));
        }
        catch (TimeoutException)
        {
            throw new TimeoutException(
                "Swarm transport did not start within 15 seconds.");
        }
        Console.WriteLine("[boot] swarm running");

        if (_options.StaticPeers.Length > 0)
        {
            _ = Swarm.BootstrapAsync(
                _options.StaticPeers.Select(PeerString.Parse),
                null,
                3,
                cancellationToken);
        }

        Console.WriteLine("Peer string (paste into Unity NetworkSettings.SeedPeers):");
        Console.WriteLine($"  {PeerInfo}");
    }

    public async ValueTask DisposeAsync()
    {
        if (Swarm is { } swarm)
        {
            try
            {
                await swarm.StopAsync(TimeSpan.FromSeconds(5));
            }
            catch
            {
                // Best-effort shutdown.
            }

            try
            {
                await _swarmTask;
            }
            catch
            {
                // Swarm already logged its own failure.
            }

            swarm.Dispose();
        }
    }

    private async Task RunSwarmAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Swarm!.StartAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Swarm stopped.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Swarm stopped with error: {ex.Message}");
        }
    }

    /// <summary>
    /// The BlockCommit for a block produced by this node's validator —
    /// signed PreCommit vote with the validator's on-chain power.
    /// </summary>
    public Libplanet.Types.Blocks.BlockCommit CreateBlockCommit(
        Libplanet.Types.Blocks.Block block)
    {
        var vote = new VoteMetadata(
            block.Index,
            0,
            block.Hash,
            DateTimeOffset.UtcNow,
            _nodeKey.PublicKey,
            BigInteger.One,
            VoteFlag.PreCommit).Sign(_nodeKey);

        return new Libplanet.Types.Blocks.BlockCommit(
            block.Index, 0, block.Hash, ImmutableArray.Create(vote));
    }

    private static (IStore, TrieStateStore) OpenStores(string storePath)
    {
        if (string.IsNullOrWhiteSpace(storePath))
        {
            // In-memory mode: handy for CI smoke runs; not for real play.
            return (new MemoryStore(), new TrieStateStore(new MemoryKeyValueStore()));
        }

        return (
            new DefaultStore(storePath),
            new TrieStateStore(new DefaultKeyValueStore(storePath + "/states")));
    }

    /// <summary>
    /// Loads or creates the canonical chain in the given store. The
    /// BlockChain constructor re-validates that the stored genesis matches
    /// the deterministic ProjectF genesis (it throws
    /// InvalidGenesisBlockException otherwise, e.g. on a validator key change).
    /// If the store has block data but no canonical chain id (the
    /// crash-recovery case from the planet-clicker devlog), the first chain
    /// in the store is adopted as canonical.
    /// </summary>
    private static BlockChain BootChain(
        IStore store,
        TrieStateStore stateStore,
        IBlockPolicy policy,
        ActionEvaluator actionEvaluator,
        GenesisContext genesis)
    {
        Guid? chainId = store.GetCanonicalChainId();
        if (chainId is null)
        {
            System.Collections.Generic.IEnumerable<Guid> chainIds = store.ListChainIds();
            if (chainIds.Any())
            {
                // Crash-recovery: block data exists but no canonical id set.
                chainId = chainIds.First();
                store.SetCanonicalChainId(chainId.Value);
                Console.WriteLine($"Adopted existing chain {chainId} as canonical.");
            }
        }

        if (chainId is { })
        {
            // Restart case: the ctor validates the stored genesis against our
            // deterministic genesis and loads the chain with its tip.
            return new BlockChain(
                policy,
                new VolatileStagePolicy(),
                store,
                stateStore,
                genesis.GenesisBlock,
                new BlockChainStates(store, stateStore),
                actionEvaluator);
        }

        // Fresh store: create the chain (persists genesis + canonical id).
        return BlockChain.Create(
            policy,
            new VolatileStagePolicy(),
            store,
            stateStore,
            genesis.GenesisBlock,
            actionEvaluator);
    }
}
