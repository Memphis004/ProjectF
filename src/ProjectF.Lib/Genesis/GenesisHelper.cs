using System;
using Libplanet.Types.Blocks;

namespace ProjectF.Lib.Genesis;

/// <summary>
/// Genesis helpers kept thin: the actual construction lives in
/// <see cref="GenesisBuilder"/> (mirroring Libplanet 5.5.3's own test
/// bootstrap). This static class is the stable entry point required by the
/// original scaffold spec.
/// </summary>
public static class GenesisHelper
{
    /// <summary>
    /// Builds the ProjectF genesis block with the given validator set.
    /// Deterministic: identical key material produces an identical block hash.
    /// </summary>
    public static Block BuildGenesisBlock(
        Libplanet.Crypto.PrivateKey validatorKey,
        Libplanet.Types.Consensus.ValidatorSet validatorSet,
        DateTimeOffset? timestamp = null)
        => GenesisBuilder.BuildGenesisBlock(validatorKey, validatorSet, timestamp);
}
