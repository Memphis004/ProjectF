using System;
using System.Linq;
using Libplanet.Action.State;
using Libplanet.Store.Trie;
using Libplanet.Types.Blocks;
using Libplanet.Types.Tx;
using ProjectF.Lib;
using ProjectF.Lib.Genesis;
using Xunit;

namespace ProjectF.Lib.Tests;

public class GenesisHelperTests : IDisposable
{
    private readonly ChainFixture _fixture = new();

    [Fact]
    public void BuildGenesis_IsDeterministic()
    {
        Block a = _fixture.Genesis.GenesisBlock;
        Block b = GenesisHelper.BuildGenesisBlock(
            _fixture.ValidatorKey,
            _fixture.ValidatorSet);

        Assert.Equal(a.Hash, b.Hash);
    }

    [Fact]
    public void GenesisCarriesInitializeTxWithValidatorSet()
    {
        Transaction[] txs = _fixture.Genesis.GenesisBlock.Transactions.ToArray();
        Transaction tx = Assert.Single(txs);

        // Libplanet 5.x requires the genesis tx signer to be a validator;
        // here that is the validator key itself.
        Assert.Equal(_fixture.ValidatorKey.Address, tx.Signer);

        // The only user action in the genesis tx is the Initialize system
        // action; ProjectF seeds no extra genesis state.
        Assert.Single(tx.Actions);
    }

    [Fact]
    public void GenesisValidatorSet_IsInstalledInState()
    {
        var validatorSet = _fixture.Chain
            .GetNextWorldState()!
            .GetValidatorSet();

        Assert.Single(validatorSet.Validators);
    }

    [Fact]
    public void GenesisSignsEvaluatedRootHash_ForProtocol8()
    {
        // Protocol 8 (Libplanet 5.5.3) genesis blocks carry the EVALUATED
        // state root hash — non-empty because Initialize wrote the validator
        // set into state.
        Assert.NotEqual(
            MerkleTrie.EmptyRootHash,
            _fixture.Genesis.GenesisBlock.StateRootHash);
        Assert.Equal(
            Block.CurrentProtocolVersion,
            _fixture.Genesis.GenesisBlock.ProtocolVersion);
    }

    public void Dispose() => _fixture.Dispose();
}
