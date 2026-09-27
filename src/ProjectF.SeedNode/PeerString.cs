using System;
using Libplanet.Crypto;
using Libplanet.Net;

namespace ProjectF.SeedNode;

/// <summary>
/// Helpers around the peer string format <c>{pubkeyHex},{host},{port}</c> —
/// the same format Libplanet's own <see cref="BoundPeer.ParsePeer"/> accepts,
/// and what the Unity client pastes into its NetworkSettings.
/// </summary>
public static class PeerString
{
    /// <summary>Builds the peer string for a node with the given key and endpoint.</summary>
    public static string Format(PublicKey publicKey, string host, int port) =>
        $"{publicKey.ToHex(false)},{host},{port}";

    /// <summary>Parses a peer string into a BoundPeer (validates the format).</summary>
    public static BoundPeer Parse(string peerString) => BoundPeer.ParsePeer(peerString);
}
