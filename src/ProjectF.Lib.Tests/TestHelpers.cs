// Stage-4 test scaffolding, written against the REAL Libplanet 5.5.3 API
// surface (verified by reflection over the restored assemblies — every type
// and member used here exists; no guessed names).
//
// The discovery that shaped this design: Libplanet.Mocks 5.5.3 contains only
// MockWorldState / MockBlockChainStates / MockUtil — there is NO
// MockActionContext in 5.5.x (it was removed with the old IContext style).
// So TestActionContext is hand-rolled against the IActionContext interface
// (13 members, all verified) and TestWorld wraps the official MockWorldState.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Bencodex.Types;
using Libplanet.Action;
using Libplanet.Action.State;
using Libplanet.Crypto;
using Libplanet.Mocks;
using Libplanet.Types.Blocks;
using Libplanet.Types.Assets;
using Libplanet.Types.Evidence;
using Libplanet.Types.Tx;

namespace ProjectF.Lib.Tests;

/// <summary>
/// Fully deterministic IRandom for tests: returns the next value of a
/// caller-supplied script (looping), so fishing/cooking rolls are replayable
/// regardless of how many draws an action makes.
/// </summary>
public sealed class TestRandom : IRandom
{
    private readonly int[] _script;
    private int _cursor;

    public TestRandom(int seed)
        : this(new[] { seed })
    {
    }

    public TestRandom(int[] script)
    {
        if (script is null || script.Length == 0)
        {
            throw new ArgumentException("Script must contain at least one value.", nameof(script));
        }

        _script = script;
    }

    public int Seed => _script[0];

    public int Next() => Next(0, int.MaxValue);

    public int Next(int upperBound) => Next(0, upperBound);

    public int Next(int lowerBound, int upperBound)
    {
        if (upperBound <= lowerBound)
        {
            throw new ArgumentOutOfRangeException(nameof(upperBound), "Must be > lowerBound.");
        }

        int value = _script[_cursor];
        _cursor = (_cursor + 1) % _script.Length;
        long span = (long)upperBound - lowerBound;
        return lowerBound + (int)(value % span);
    }

    public void NextBytes(byte[] buffer)
    {
        for (var i = 0; i < buffer.Length; i++)
        {
            buffer[i] = (byte)Next(0, 256);
        }
    }
}

/// <summary>
/// In-memory test world backed by the official Libplanet.Mocks state store.
/// Offers state get/set plus a fluent Execute(action) that threads the world
/// through IAction.Execute() exactly like the chain would.
/// </summary>
public sealed class TestWorld
{
    private readonly MockBlockChainStates _chainStates;
    private IWorld _world;

    public TestWorld()
    {
        _chainStates = new MockBlockChainStates();
        // Modern (non-legacy) world base — MUST be MockModernWorldState: the
        // null-root GetWorldState() on the mock chain states returns a LEGACY
        // world, and Account (non-legacy) cannot be SetAccount'ed into it
        // ("Cannot set a non-legacy account to a legacy IWorld").
        _world = new World(MockUtil.MockModernWorldState);
    }

    /// <summary>The world as an action would receive it via PreviousState.</summary>
    public IWorld World => _world;

    /// <summary>Underlying mock chain states (for advanced assertions).</summary>
    public MockBlockChainStates ChainStates => _chainStates;

    /// <summary>Raw state read at (accountSpace, address).</summary>
    public IValue? GetState(Address accountSpace, Address address) =>
        _world.GetAccount(accountSpace)?.GetState(address);

    /// <summary>Raw state write at (accountSpace, address) — test-side mutation.</summary>
    public TestWorld SetState(Address accountSpace, Address address, IValue value)
    {
        IAccount account = _world.GetAccount(accountSpace) ?? new Account(MockUtil.MockAccountState);
        _world = _world.SetAccount(accountSpace, account.SetState(address, value));
        return this;
    }

    /// <summary>
    /// Runs <paramref name="action"/> against the current world, stores the
    /// resulting world, and returns it (the post-action state).
    /// </summary>
    public IWorld Execute(IAction action, TestActionContext context)
    {
        IWorld result = action.Execute(context.WithPreviousState(_world));
        _world = result;
        return result;
    }
}

/// <summary>
/// Hand-rolled IActionContext test double (Libplanet.Mocks 5.5.3 ships no
/// MockActionContext). All 13 interface members implemented; defaults are
/// deterministic and overridable via the fluent With* methods or object
/// initializer.
/// </summary>
public sealed class TestActionContext : IActionContext
{
    public const int DefaultRandomSeed = 12345;

    public TestActionContext()
    {
        Signer = default;
        Miner = default;
        TxId = null;
        BlockIndex = 1;
        BlockProtocolVersion = 5;
        LastCommit = null;
        PreviousState = null!;
        RandomSeed = DefaultRandomSeed;
        IsPolicyAction = false;
        MaxGasPrice = null;
        Txs = ImmutableArray<ITransaction>.Empty;
        Evidence = ImmutableArray<EvidenceBase>.Empty;
    }

    /// <summary>Builds the real context for an execution over <paramref name="world"/>.</summary>
    public static TestActionContext Create(
        IWorld world,
        Address signer,
        long blockIndex,
        int randomSeed = DefaultRandomSeed)
    {
        return new TestActionContext
        {
            PreviousState = world,
            Signer = signer,
            BlockIndex = blockIndex,
            RandomSeed = randomSeed,
        };
    }

    public Address Signer { get; set; }
    public TxId? TxId { get; set; }
    public Address Miner { get; set; }
    public long BlockIndex { get; set; }
    public int BlockProtocolVersion { get; set; }
    public BlockCommit? LastCommit { get; set; }
    public IWorld PreviousState { get; set; }
    public int RandomSeed { get; set; }
    public bool IsPolicyAction { get; set; }
    public FungibleAssetValue? MaxGasPrice { get; set; }
    public IReadOnlyList<ITransaction> Txs { get; set; }
    public IReadOnlyList<EvidenceBase> Evidence { get; set; }

    /// <summary>
    /// Returns a fresh deterministic random sequence per call, seeded by
    /// RandomSeed — the contract actions can rely on (a real chain node does
    /// exactly this; the seed is derived from the block/tx pair).
    /// </summary>
    public IRandom GetRandom() => new TestRandom(RandomSeed);

    /// <summary>Clone with a different PreviousState (used by TestWorld.Execute).</summary>
    public TestActionContext WithPreviousState(IWorld world)
    {
        var clone = (TestActionContext)MemberwiseClone();
        clone.PreviousState = world;
        return clone;
    }
}
