using System.Linq;
using UnityEditor;
using UnityEngine;

// ReSharper disable CheckNamespace
namespace ProjectF.Editor
{
    /// <summary>
    /// Stage 8 one-window setup (menu: ProjectF → Setup). Every button is a
    /// thin wrapper over the generators; Full Setup runs them in dependency
    /// order. knowledge.md "Editor-generated content": nothing under
    /// Art/Placeholder, Prefabs, Scenes or Settings is hand-made.
    /// </summary>
    public sealed class ProjectFSetupWindow : EditorWindow
    {
        private bool forceRegenerate;

        [MenuItem("ProjectF/Setup")]
        public static void Open()
        {
            GetWindow<ProjectFSetupWindow>(true, "ProjectF Setup");
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "Regenerates placeholder art, settings, prefabs and scenes. " +
                "Existing files are overwritten (except settings — see the toggle).",
                MessageType.Info);

            forceRegenerate = EditorGUILayout.Toggle(
                new GUIContent("Force Regenerate",
                    "Also overwrite an existing NetworkSettings.asset with defaults. " +
                    "OFF never touches hand-edited settings (seed peer, APV token…)."),
                forceRegenerate);

            EditorGUILayout.Space(8f);

            if (GUILayout.Button("Generate Placeholder Sprites", GUILayout.Height(28f)))
            {
                Run("Generate Placeholder Sprites", PlaceholderSpriteGenerator.GenerateAll);
            }

            if (GUILayout.Button("Generate Settings Assets", GUILayout.Height(28f)))
            {
                Run("Generate Settings Assets", () => SettingsAssetGenerator.Generate(forceRegenerate));
            }

            EditorGUILayout.Space(4f);

            if (GUILayout.Button("Generate Prefabs", GUILayout.Height(28f)))
            {
                Run("Generate Prefabs", PrefabGenerator.GenerateAll);
            }

            if (GUILayout.Button("Generate Scenes", GUILayout.Height(28f)))
            {
                Run("Generate Scenes", SceneGenerator.GenerateAll);
            }

            EditorGUILayout.Space(8f);

            GUI.color = new Color(0.7f, 0.9f, 0.7f);
            if (GUILayout.Button("Full Setup (all of the above)", GUILayout.Height(36f)))
            {
                Run("Full Setup", BatchSetup.RunFullSetupSteps);
            }

            GUI.color = Color.white;

            if (GUILayout.Button("Validate Project", GUILayout.Height(28f)))
            {
                Run("Validate Project", ValidateAndLog);
            }
        }

        private static void ValidateAndLog()
        {
            ValidationReport report = ProjectValidator.Validate();
            string text = report.Render();
            if (report.Ok)
            {
                Debug.Log($"[validate] {text}");
            }
            else
            {
                Debug.LogError($"[validate] {text}");
            }
        }

        private static void Run(string label, System.Action action)
        {
            try
            {
                action();
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[{label.ToLowerInvariant().Replace(' ', '-')}] FAILED — {ex}");
            }
        }
    }
}
