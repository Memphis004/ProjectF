using System;
using System.IO;
using Libplanet.Crypto;
using ProjectF.Infrastructure.Network;
using UnityEngine;

// ReSharper disable CheckNamespace
namespace ProjectF.Infrastructure.Blockchain
{
    /// <summary>
    /// Persists the player's Libplanet private key. Stage 7 scope (per spec):
    /// a plain hex file under {persistentDataPath}/keys/{instance}.hex, created
    /// once and reused forever — the address IS the on-chain identity, losing
    /// the file loses the avatar. Stage 14 replaces this with encrypted
    /// keystore profiles; keep this class the single seam until then.
    /// Never log the key or the decrypted bytes.
    /// </summary>
    public sealed class KeyStore
    {
        private readonly NetworkSettings _settings;

        private PrivateKey? _playerKey;

        public KeyStore(NetworkSettings settings)
        {
            _settings = settings;
        }

        /// <summary>The player key — loaded from disk or created on first run.</summary>
        public PrivateKey LoadOrCreatePlayerKey()
        {
            if (_playerKey is { } existing)
            {
                return existing;
            }

            string dir = Path.Combine(Application.persistentDataPath, "keys");
            Directory.CreateDirectory(dir);
            string instance = string.IsNullOrWhiteSpace(_settings.InstanceId)
                ? "player1"
                : _settings.InstanceId;
            string path = Path.Combine(dir, $"{instance}.hex");

            if (File.Exists(path))
            {
                string hex = File.ReadAllText(path).Trim();
                // Convert.FromHexString is .NET 5+; Unity 2022.3 Mono only has
                // Convert.FromBase64/XmlString — parse hex manually.
                _playerKey = new PrivateKey(ParseHex(hex));
                Debug.Log($"[keys] loaded player key for instance '{instance}' " +
                          $"(address {_playerKey.Address}).");
            }
            else
            {
                _playerKey = new PrivateKey();
                // File API is fine OUTSIDE IAction.Execute (knowledge.md rule 2
                // forbids it only on-chain). Key bytes never go to the log.
                // PrivateKey.ByteArray is ImmutableArray<byte> (Libplanet 5.5.3).
                // Copy via indexer — available on every version of
                // System.Collections.Immutable, unlike ToArray()/LINQ which
                // depend on which assembly the compile resolves.
                System.Collections.Immutable.ImmutableArray<byte> raw = _playerKey.ByteArray;
                var keyBytes = new byte[raw.Length];
                for (int i = 0; i < keyBytes.Length; i++)
                {
                    keyBytes[i] = raw[i];
                }

                File.WriteAllText(path, ToHex(keyBytes));
                Debug.Log($"[keys] created new player key for instance '{instance}' " +
                          $"(address {_playerKey.Address}) at {path}.");
            }

            return _playerKey;
        }

        /// <summary>True when a key file already exists for this instance.</summary>
        public bool HasStoredKey()
        {
            string instance = string.IsNullOrWhiteSpace(_settings.InstanceId)
                ? "player1"
                : _settings.InstanceId;
            return File.Exists(Path.Combine(
                Application.persistentDataPath, "keys", $"{instance}.hex"));
        }

        private static byte[] ParseHex(string hex)
        {
            hex = hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? hex[2..] : hex;
            if (hex.Length % 2 != 0)
            {
                throw new FormatException("Hex key string must have an even length.");
            }

            byte[] bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }

            return bytes;
        }

        private static string ToHex(byte[] bytes)
        {
            var sb = new System.Text.StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes)
            {
                sb.Append(b.ToString("x2"));
            }

            return sb.ToString();
        }
    }
}
