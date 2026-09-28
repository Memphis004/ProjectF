using System.IO;
using ProjectF.Infrastructure.Network;
using UnityEditor;
using UnityEngine;

// ReSharper disable CheckNamespace
namespace ProjectF.Editor
{
    /// <summary>
    /// Creates Assets/Main/Settings/NetworkSettings.asset with the documented
    /// defaults (SETUP.md). Existing assets are NEVER overwritten without the
    /// window's "Force Regenerate" toggle — hand-edited values (seed peer,
    /// APV token, player name) must not be silently lost.
    /// Also flips Player Settings → "Allow downloads over HTTP" so the local
    /// h2c hub works without a manual project-settings visit.
    /// </summary>
    public static class SettingsAssetGenerator
    {
        public const string SettingsPath = EditorPaths.SettingsRoot + "/NetworkSettings.asset";

        /// <summary>Force-regenerate an existing asset (window toggle only).</summary>
        public static NetworkSettings Generate(bool forceRegenerate)
        {
            EnsureFolders();

            NetworkSettings existing =
                AssetDatabase.LoadAssetAtPath<NetworkSettings>(SettingsPath);
            if (existing is { } && !forceRegenerate)
            {
                Debug.Log($"[settings] kept existing {SettingsPath} (use Force Regenerate to reset).");
                ApplyHttpSetting();
                return existing;
            }

            NetworkSettings settings = existing ?? ScriptableObject.CreateInstance<NetworkSettings>();

            // Documented defaults — identical to the class's field initializers
            // so a regenerate never introduces a new baseline silently.
            settings.SeedPeers = System.Array.Empty<string>();
            settings.ApvToken = string.Empty;
            settings.GenesisPath = "genesis.dat";
            settings.StorePath = "{persistentDataPath}/chain-{instanceId}";
            settings.NodePort = 0;
            settings.SyncTimeoutSeconds = 120;
            settings.HubHost = "127.0.0.1";
            settings.HubPort = 5170;
            settings.InstanceId = "player1";
            settings.PlayerName = "Player";

            if (existing is null)
            {
                AssetDatabase.CreateAsset(settings, SettingsPath);
            }
            else
            {
                EditorUtility.SetDirty(settings);
            }

            AssetDatabase.SaveAssets();
            ApplyHttpSetting();
            Debug.Log($"[settings] NetworkSettings defaults written to {SettingsPath}.");
            return settings;
        }

        /// <summary>
        /// Item 1 of the old manual checklist: local dev talks plain http:// to
        /// 127.0.0.1:5170, so "Allow downloads over HTTP" must be Always
        /// Allowed. Serialized as the INT 'insecureHttpOption' in
        /// ProjectSettings.asset (0 = not allowed, 1 = dev builds only,
        /// 2 = always allowed). Idempotent; safe in batch mode.
        /// </summary>
        public static void ApplyHttpSetting()
        {
            SerializedObject playerSettings = new(
                Unsupported.GetSerializedAssetInterfaceSingleton("PlayerSettings"));
            SerializedProperty option = playerSettings.FindProperty("insecureHttpOption");
            if (option is null)
            {
                Debug.LogWarning("[settings] PlayerSettings has no insecureHttpOption — " +
                                 "set 'Allow downloads over HTTP' manually.");
                return;
            }

            if (option.intValue < 2)
            {
                option.intValue = 2; // Always allowed
                playerSettings.ApplyModifiedProperties();
                Debug.Log("[settings] Player Settings: Allow downloads over HTTP = Always allowed.");
            }
        }

        private static void EnsureFolders()
        {
            if (AssetDatabase.IsValidFolder(EditorPaths.SettingsRoot))
            {
                return;
            }

            PlaceholderSpriteGenerator.EnsureFolders();
            Directory.CreateDirectory(EditorPaths.SettingsRoot);
            AssetDatabase.Refresh();
        }
    }
}
