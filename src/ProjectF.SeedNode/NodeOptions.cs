namespace ProjectF.SeedNode;

/// <summary>
/// SeedNode configuration (bound from the "SeedNode" section of
/// appsettings.json). See appsettings.json for documented defaults.
/// </summary>
public sealed class NodeOptions
{
    public const string SectionName = "SeedNode";

    /// <summary>Directory for the Libplanet store. Empty = in-memory (testing only).</summary>
    public string StorePath { get; set; } = "";

    /// <summary>Reserved for a future on-disk genesis snapshot; the genesis is
    /// currently derived deterministically from the validator key.</summary>
    public string GenesisPath { get; set; } = "";

    /// <summary>Public host name advertised in the peer string.</summary>
    public string Host { get; set; } = "127.0.0.1";

    /// <summary>Swarm port. 0 = pick a free port.</summary>
    public int Port { get; set; } = 31236;

    /// <summary>Hex private key of the validator/miner. Empty = ephemeral key.</summary>
    public string PrivateKeyHex { get; set; } = "";

    /// <summary>Whether this node produces blocks.</summary>
    public bool IsMiner { get; set; } = true;

    /// <summary>Target interval between blocks in milliseconds.</summary>
    public int TargetBlockIntervalMs { get; set; } = 2_000;

    /// <summary>Seed peers as "{pubkeyHex},{host},{port}" strings.</summary>
    public string[] StaticPeers { get; set; } = System.Array.Empty<string>();
}
