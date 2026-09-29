using System.Collections.Generic;
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
        /// <summary>Actions queued while play mode was active; replayed once
        /// the editor is back in edit mode (see <see cref="RunGuarded"/>).</summary>
        private static readonly List<System.Action> DeferredRuns =
            new List<System.Action>();

        /// <summary>The generation pipeline, shared by the window's
        /// "Full Setup" button and batch mode. Stage 9: UI prefabs generate
        /// between the world prefabs and the scenes (scenes wire UIRoot).</summary>
        public static void RunFullSetupSteps()
        {
            RunGuarded("full-setup", () =>
            {
                // Thai TMP font FIRST — every later generator assigns it to
                // the text components it creates.
                TmpFontGenerator.Generate();
                PlaceholderSpriteGenerator.GenerateAll();
                SettingsAssetGenerator.Generate(forceRegenerate: false);
                PrefabGenerator.GenerateAll();
                UiPrefabGenerator.GenerateAll();
                SceneGenerator.GenerateAll();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            });
        }

        /// <summary>-executeMethod target: full setup + validation, exits
        /// non-zero on validation failure (batch mode only — interactive
        /// callers keep the editor open).</summary>
        public static void RunFullSetup()
        {
            RunGuarded("batch", RunFullSetupNow);
        }

        private static void RunFullSetupNow()
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
            RunGuarded("validate", ValidateNow);
        }

        private static void ValidateNow()
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

        /// <summary>
        /// Stage 9 hardening (see stage-9 lessons): running the generators or
        /// the validator while the editor plays corrupted the project —
        /// prefabs were delete+recreated with fresh GUIDs and SceneGenerator
        /// aborted at EditorSceneManager.NewScene, leaving on-disk scenes
        /// pointing at dead references. If play mode is active this stops it
        /// first and replays the action once edit mode is restored.
        /// </summary>
        public static void RunGuarded(string tag, System.Action run)
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
            {
                run();
                return;
            }

            Debug.LogWarning(
                $"[{tag}] play mode is active — stopping play first; the " +
                "action will rerun automatically once edit mode is restored " +
                "(generating/validating during play corrupts prefab GUIDs).");

            DeferredRuns.Add(run);
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.ExitPlaymode();
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            switch (state)
            {
                case PlayModeStateChange.EnteredEditMode:
                    // One more editor tick so teardown fully settles.
                    EditorApplication.delayCall += ReplayDeferredRuns;
                    break;

                case PlayModeStateChange.EnteredPlayMode:
                    // Exiting play was aborted (or Play was pressed again):
                    // drop anything queued so generation never fires mid-play.
                    DeferredRuns.Clear();
                    EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
                    break;
            }
        }

        private static void ReplayDeferredRuns()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            System.Action[] runs = DeferredRuns.ToArray();
            DeferredRuns.Clear();

            foreach (System.Action run in runs)
            {
                run();
            }
        }

        [MenuItem("ProjectF/Validate Project (Batch)")]
        private static void ValidateFromMenu()
        {
            RunValidation();
        }
    }
}
