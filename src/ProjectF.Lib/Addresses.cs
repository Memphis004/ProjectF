using System;
using System.Security.Cryptography;
using System.Text;
using Libplanet.Crypto;

namespace ProjectF.Lib;

/// <summary>
/// Deterministic address constants and key derivation.
///
/// knowledge.md rule: all IAction.Execute() code must be deterministic.
/// Every address here is derived from a fixed UTF-8 key with SHA-256
/// (truncated to 20 bytes), so every node computes byte-identical
/// addresses without relying on Libplanet API details.
/// </summary>
public static class Addresses
{
    // Separate account spaces (one IAccount each inside the Libplanet world).
    public static readonly Address Avatar = Derive("ProjectF:space:avatar");
    public static readonly Address Inventory = Derive("ProjectF:space:inventory");
    public static readonly Address Farm = Derive("ProjectF:space:farm");
    public static readonly Address Pond = Derive("ProjectF:space:pond");
    public static readonly Address TaskBoard = Derive("ProjectF:space:taskboard");

    // Dedicated space for the pipeline-proof checkpoint action (stage-1).
    public static readonly Address Ping = Derive("ProjectF:space:ping");

    // Fixed key under the Ping space holding the shared counter.
    public static readonly Address PingCounter = Derive("ProjectF:key:ping-counter");

    /// <summary>Deterministic state key for a pond slot map.</summary>
    public static Address PondKey(int pondId) => Derive($"ProjectF:key:pond:{pondId}");

    /// <summary>Deterministic state key for one farm plot of an avatar.</summary>
    public static Address PlotKey(Address avatarAddress, int plotIndex) =>
        Derive($"ProjectF:key:plot:{avatarAddress}:{plotIndex}");

    private static Address Derive(string key)
    {
        using SHA256 sha256 = SHA256.Create();
        byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(key));
        byte[] addressBytes = new byte[Address.Size];
        Array.Copy(hash, addressBytes, Address.Size);
        return new Address(addressBytes);
    }
}
