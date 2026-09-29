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

    /// <summary>Seed peers as "{pubkeyHex},{host},{port}" strings. When set
    /// from the command line, '|' separates multiple peers.</summary>
    public string[] StaticPeers { get; set; } = System.Array.Empty<string>();

    /// <summary>The pre-signed AppProtocolVersion token (see
    /// AppProtocolVersion.Token) every node in the network must present.
    /// Empty = self-sign version 1 (single-node dev mode).</summary>
    public string ApvToken { get; set; } = "";

    // ---- Libplanet peer-tracking knobs (verified by reflection against the
    // restored Libplanet.Net 5.5.3, not guessed). Null = fall back to the
    // Libplanet default listed in the comment. Bound from appsettings.json
    // TimeSpan strings ("00:00:15").

    /// <summary>SwarmOptions.RefreshPeriod — how often the routing-table
    /// refresh task runs. Libplanet default: 00:00:10.</summary>
    public TimeSpan? RefreshPeriod { get; set; }

    /// <summary>SwarmOptions.RefreshLifespan — a peer not refreshed for this
    /// long gets a Ping at the next refresh pass; a failed ping removes it.
    /// THIS is the dead-peer window: kill a peer and the seed keeps it for
    /// up to RefreshLifespan + ping-fail time (60s default ≈ the observed
    /// ~70s). Shortened to 15s for the dev/test network.
    /// Libplanet default: 00:01:00.</summary>
    public TimeSpan? RefreshLifespan { get; set; }

    /// <summary>TimeoutOptions.MaxTimeout — ceiling for a message send/receive
    /// round trip (Ping/broadcast included). Libplanet default: 00:02:30.</summary>
    public TimeSpan? MaxTimeout { get; set; }

    /// <summary>TimeoutOptions.DialTimeout — per-dial timeout; also bounds how
    /// fast the removal Ping fails. Libplanet default: 00:00:01.</summary>
    public TimeSpan? DialTimeout { get; set; }
}
