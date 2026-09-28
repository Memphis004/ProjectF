using UnityEngine;

// ReSharper disable CheckNamespace
namespace ProjectF.Infrastructure.Network
{
    /// <summary>
    /// Network + node configuration (spec section 7). Create one asset via
    /// UnityProject/SETUP.md checklist item 3 and assign it to RootLifetimeScope.
    ///
    /// Invariants baked into the defaults:
    /// - <see cref="StorePath"/> MUST contain {instanceId} and
    ///   <see cref="NodePort"/> MUST be 0 so two builds on one machine never
    ///   lock the same chain store / port.
    /// - <see cref="SeedPeers"/> entries use Libplanet's standard
    ///   {pubkeyHex},{host},{port} format — the peer string the SeedNode prints.
    /// - <see cref="ApvToken"/> must carry the EXACT pre-signed token from the
    ///   seed's apv.txt — Libplanet silently drops any message whose signed
    ///   APV differs (README "The bootstrap files").
    /// </summary>
    [CreateAssetMenu(
        fileName = "NetworkSettings",
        menuName = "ProjectF/Network Settings",
        order = 0)]
    public sealed class NetworkSettings : ScriptableObject
    {
        [Header("Libplanet seed node (chain)")]
        [Tooltip("{pubkeyHex},{host},{port} — the peer string printed by ProjectF.SeedNode.")]
        public string[] SeedPeers = System.Array.Empty<string>();

        [Tooltip("Pre-signed AppProtocolVersion token — copy the full line of the seed's apv.txt.")]
        [TextArea(2, 6)]
        public string ApvToken = string.Empty;

        [Tooltip("Path of the genesis.dat copy shipped via StreamingAssets. " +
                 "Contains {persistentDataPath} — expanded at runtime.")]
        public string GenesisPath = "genesis.dat";

        [Header("Embedded node")]
        [Tooltip("MUST contain {instanceId} so two builds never share one chain store.")]
        public string StorePath = "{persistentDataPath}/chain-{instanceId}";

        [Tooltip("0 = let the OS pick a free port (required for multi-instance).")]
        public int NodePort = 0;

        [Tooltip("Seconds budgeted for swarm bootstrap + preload before gameplay continues offline.")]
        public int SyncTimeoutSeconds = 120;

        [Header("Presence hub (cosmetic layer)")]
        public string HubHost = "127.0.0.1";

        public int HubPort = 5170;

        [Header("Identity")]
        [Tooltip("Instance discriminator used in StorePath — one per running build.")]
        public string InstanceId = "player1";

        [Tooltip("Display name broadcast over presence. Never affects chain identity.")]
        public string PlayerName = "Player";

        /// <summary>StorePath with {persistentDataPath} and {instanceId} expanded.</summary>
        public string ResolvedStorePath =>
            (StorePath ?? string.Empty)
                .Replace("{persistentDataPath}", Application.persistentDataPath)
                .Replace("{instanceId}", string.IsNullOrWhiteSpace(InstanceId) ? "player1" : InstanceId);

        /// <summary>GenesisPath with {persistentDataPath} and {streamingAssets} expanded.
        /// Absolute paths pass through untouched (editor tools / CI).</summary>
        public string ResolvedGenesisPath
        {
            get
            {
                string path = GenesisPath ?? string.Empty;
                if (System.IO.Path.IsPathRooted(path))
                {
                    return path;
                }

                return path
                    .Replace("{persistentDataPath}", Application.persistentDataPath)
                    .Replace("{streamingAssets}", Application.streamingAssetsPath);
            }
        }

        /// <summary>h2c address of the presence hub (HubServer: http://127.0.0.1:5170).</summary>
        public string HubAddress => $"http://{HubHost}:{HubPort}";

        private void OnValidate()
        {
            if (NodePort < 0)
            {
                NodePort = 0;
            }

            if (HubPort <= 0)
            {
                HubPort = 5170;
            }

            if (!string.IsNullOrEmpty(StorePath) && !StorePath.Contains("{instanceId}"))
            {
                Debug.LogWarning(
                    $"[{name}] StorePath does not contain {{instanceId}} — two builds on " +
                    "one machine will fight over the same chain store.");
            }
        }
    }
}
