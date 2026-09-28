using UnityEditor;
using UnityEngine;

// ReSharper disable CheckNamespace
namespace ProjectF.Editor
{
    /// <summary>
    /// Batch entry point for CI / command line:
    ///
    ///   Unity -batchmode -quit -projectPath UnityProject \
    ///     -executeMethod ProjectF.Editor.BatchSetup.RunFullSetup -logFile -
    ///
    /// Exit code 0 = everything generated + validation passed; non-zero
    /// otherwise (spec item 6: validator must be CI-usable).
    /// </summary>
    public static class BatchSetup
    {
        /// <summary>The generation pipeline, shared by the window's
        /// "Full Setup" button and batch mode.</summary>
        public static void RunFullSetupSteps()
        {
            PlaceholderSpriteGenerator.GenerateAll();
            SettingsAssetGenerator.Generate(forceRegenerate: false);
            PrefabGenerator.GenerateAll();
            SceneGenerator.GenerateAll();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        /// <summary>-executeMethod target: full setup + validation, exits
        /// non-zero on validation failure (batch mode only — interactive
        /// callers keep the editor open).</summary>
        public static void RunFullSetup()
        {
            try
            {
                RunFullSetupSteps();
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[batch] full setup FAILED — {ex}");
                Exit(1);
                return;
            }

            ValidationReport report = ProjectValidator.Validate();
            string text = report.Render();
            if (report.Ok)
            {
                Debug.Log($"[batch] {text}");
            }
            else
            {
                Debug.LogError($"[batch] {text}");
            }

            Exit(report.Ok ? 0 : 2);
        }

        /// <summary>-executeMethod target for validation-only CI runs.</summary>
        public static void RunValidation()
        {
            ValidationReport report = ProjectValidator.Validate();
            string text = report.Render();
            if (report.Ok)
            {
                Debug.Log($"[batch] {text}");
            }
            else
            {
                Debug.LogError($"[batch] {text}");
            }

            Exit(report.Ok ? 0 : 2);
        }

        /// <summary>Quits with a code ONLY in batch mode — an interactive
        /// menu/button must never kill the editor.</summary>
        private static void Exit(int exitCode)
        {
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(exitCode);
            }
        }

        [MenuItem("ProjectF/Validate Project (Batch)")]
        private static void ValidateFromMenu()
        {
            RunValidation();
        }
    }
}
