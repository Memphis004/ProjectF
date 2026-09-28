using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ProjectF.Infrastructure;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.U2D;
using VContainer.Unity;

// ReSharper disable CheckNamespace
namespace ProjectF.Editor
{
    /// <summary>Result of a validation pass — readable report + pass/fail.</summary>
    public sealed class ValidationReport
    {
        public List<string> Errors { get; } = new();
        public List<string> Warnings { get; } = new();

        public bool Ok => Errors.Count == 0;

        public void Error(string message) => Errors.Add(message);

        public void Warn(string message) => Warnings.Add(message);

        public string Render()
        {
            var builder = new StringBuilder();
            builder.AppendLine($"ProjectF validation: {(Ok ? "PASS" : "FAIL")} " +
                               $"({Errors.Count} error(s), {Warnings.Count} warning(s))");
            foreach (string error in Errors)
            {
                builder.AppendLine($"  ERROR  {error}");
            }

            foreach (string warning in Warnings)
            {
                builder.AppendLine($"  warn   {warning}");
            }

            return builder.ToString().TrimEnd();
        }
    }

    /// <summary>
    /// Stage 8 static checks (spec item 6) — callable from the setup window
    /// AND from CI via BatchSetup.RunFullSetup/RunValidation (batch mode exits
    /// non-zero on failure). knowledge.md: generated content must be
    /// verifiable without a human in the loop.
    /// </summary>
    public static class ProjectValidator
    {
        private static readonly string[] GameplayScenes =
        {
            "Village", "Shop", "AuntieHouse", "FarmPlot",
        };

        private static readonly string RootScopeTypeName =
            typeof(RootLifetimeScope).FullName!;

        private static readonly Dictionary<string, string> ExpectedScopeTypes = new()
        {
            ["Village"] = "ProjectF.Presentation.Village.VillageLifetimeScope",
            ["Shop"] = "ProjectF.Presentation.Shop.ShopLifetimeScope",
            ["AuntieHouse"] = "ProjectF.Presentation.AuntieHouse.AuntieHouseLifetimeScope",
            ["FarmPlot"] = "ProjectF.Presentation.FarmPlot.FarmPlotLifetimeScope",
        };

        public static ValidationReport Validate()
        {
            var report = new ValidationReport();
            ValidateBuildSettings(report);
            ValidateScenes(report);
            ValidateItemIcons(report);
            return report;
        }

        // ------------------------------------------------------------------
        // 1) Build settings: 5 scenes, Persistent at index 0, files exist
        // ------------------------------------------------------------------

        private static void ValidateBuildSettings(ValidationReport report)
        {
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            if (scenes.Length == 0)
            {
                report.Error("Build settings contain no scenes (expected 5, Persistent at index 0).");
                return;
            }

            if (scenes[0].path != EditorPaths.SceneRoot + "/Persistent.unity")
            {
                report.Error($"Build settings index 0 is '{scenes[0].path}' — expected " +
                             $"{EditorPaths.SceneRoot}/Persistent.unity.");
            }

            var expected = new List<string> { EditorPaths.SceneRoot + "/Persistent.unity" };
            expected.AddRange(GameplayScenes.Select(name => $"{EditorPaths.SceneRoot}/{name}.unity"));

            foreach (string path in expected)
            {
                int index = expected.IndexOf(path);
                if (index >= scenes.Length)
                {
                    report.Error($"Build settings missing scene at index {index}: {path}.");
                    continue;
                }

                if (scenes[index].path != path)
                {
                    report.Error($"Build settings index {index} is '{scenes[index].path}' — expected {path}.");
                }
                else if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) is null && !File.Exists(path))
                {
                    report.Error($"Build settings scene file does not exist: {path}.");
                }
                else if (!scenes[index].enabled)
                {
                    report.Warn($"Build settings scene is disabled: {path}.");
                }
            }

            for (int i = expected.Count; i < scenes.Length; i++)
            {
                report.Warn($"Extra scene in build settings at index {i}: {scenes[i].path}.");
            }
        }

        // ------------------------------------------------------------------
        // 2) Per-scene: exactly one LifetimeScope, correct type, wired refs
        // ------------------------------------------------------------------

        private static void ValidateScenes(ValidationReport report)
        {
            Scene previouslyActive = SceneManager.GetActiveScene();
            var opened = new List<Scene>();

            try
            {
                foreach (string name in new[] { "Persistent" }.Concat(GameplayScenes))
                {
                    string path = $"{EditorPaths.SceneRoot}/{name}.unity";
                    if (!File.Exists(path))
                    {
                        report.Error($"Scene file missing: {path} — run Full Setup.");
                        continue;
                    }

                    Scene scene = FindOrOpen(path, opened);
                    if (!scene.IsValid())
                    {
                        report.Error($"Could not open scene: {path}.");
                        continue;
                    }

                    if (name == "Persistent")
                    {
                        ValidatePersistent(scene, report);
                    }
                    else
                    {
                        ValidateGameplayScene(scene, name, report);
                    }
                }
            }
            finally
            {
                // Restore the active scene FIRST (CloseScene refuses to close
                // the active one), then close what we opened.
                if (previouslyActive.IsValid() && previouslyActive.isLoaded)
                {
                    EditorSceneManager.SetActiveScene(previouslyActive);
                }

                foreach (Scene scene in opened)
                {
                    if (scene.IsValid() && scene.isLoaded)
                    {
                        EditorSceneManager.CloseScene(scene, true);
                    }
                }
            }
        }

        private static Scene FindOrOpen(string path, List<Scene> opened)
        {
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
            {
                Scene scene = EditorSceneManager.GetSceneAt(i);
                if (scene.path == path)
                {
                    return scene; // already open — do not close it later
                }
            }

            Scene openedScene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            opened.Add(openedScene);
            return openedScene;
        }

        private static void ValidatePersistent(Scene scene, ValidationReport report)
        {
            List<LifetimeScope> scopes = FindScopes(scene);
            if (scopes.Count != 1)
            {
                report.Error($"Persistent: expected exactly 1 LifetimeScope, found {scopes.Count}.");
                return;
            }

            if (scopes[0].GetType().FullName != RootScopeTypeName)
            {
                report.Error($"Persistent: scope is {scopes[0].GetType().FullName}, " +
                             $"expected {RootScopeTypeName}.");
            }

            CheckScopeReferences(scopes[0], report);

            Camera? camera = scene.GetRootGameObjects()
                .Select(go => go.GetComponent<Camera>())
                .FirstOrDefault(c => c is { });
            if (camera is null)
            {
                report.Error("Persistent: no Camera found.");
                return;
            }

            PixelPerfectCamera? ppc = camera.GetComponent<PixelPerfectCamera>();
            if (ppc is null)
            {
                report.Warn("Persistent: Main Camera has no PixelPerfectCamera (PPU 16 / 320x180).");
            }
            else
            {
                if (ppc.assetsPPU != EditorPaths.PixelsPerUnit)
                {
                    report.Error($"Persistent: PixelPerfectCamera PPU is {ppc.assetsPPU}, " +
                                 $"expected {EditorPaths.PixelsPerUnit}.");
                }

                if (ppc.refResolutionX != 320 || ppc.refResolutionY != 180)
                {
                    report.Error($"Persistent: PixelPerfectCamera reference resolution is " +
                                 $"{ppc.refResolutionX}x{ppc.refResolutionY}, expected 320x180.");
                }

                if (ppc.upscaleRT)
                {
                    report.Warn("Persistent: PixelPerfectCamera upscaleRT is ON (spec: off).");
                }
            }
        }

        private static void ValidateGameplayScene(Scene scene, string name, ValidationReport report)
        {
            List<LifetimeScope> scopes = FindScopes(scene);
            if (scopes.Count != 1)
            {
                report.Error($"{name}: expected exactly 1 LifetimeScope, found {scopes.Count}.");
                return;
            }

            LifetimeScope scope = scopes[0];
            string actualType = scope.GetType().FullName ?? string.Empty;
            if (ExpectedScopeTypes.TryGetValue(name, out string? expectedType) &&
                actualType != expectedType)
            {
                report.Error($"{name}: scope is {actualType}, expected {expectedType}.");
            }

            SerializedObject so = new(scope);
            SerializedProperty? typeName = so.FindProperty("parentReference")?
                .FindPropertyRelative("TypeName");
            string parentName = typeName?.stringValue ?? string.Empty;
            if (parentName != RootScopeTypeName)
            {
                report.Error($"{name}: parentReference.TypeName is '{parentName}', " +
                             $"expected '{RootScopeTypeName}' (Persistent never unloads).");
            }

            CheckScopeReferences(scope, report);

            // Spawn point must exist and be positioned INSIDE the scene's own file.
            SerializedProperty? spawn = so.FindProperty("spawnPoint");
            if (spawn is { objectReferenceValue: Transform spawnTransform } &&
                spawnTransform.gameObject.scene != scene)
            {
                report.Error($"{name}: spawnPoint points into another scene " +
                             $"({spawnTransform.gameObject.scene.path}).");
            }
        }

        /// <summary>Every serialized object reference on a scope must be
        /// assigned — the generator wires them all; a null here means a
        /// generator/runtime field-name drift (or a deleted prefab).</summary>
        private static void CheckScopeReferences(LifetimeScope scope, ValidationReport report)
        {
            var so = new SerializedObject(scope);
            SerializedProperty iterator = so.GetIterator();
            bool entered = iterator.NextVisible(true);
            while (entered)
            {
                if (iterator.propertyType == SerializedPropertyType.ObjectReference &&
                    iterator.objectReferenceValue is null)
                {
                    report.Error($"{scope.GetType().Name}: '{iterator.propertyPath}' is not assigned.");
                }

                entered = iterator.NextVisible(iterator.propertyType switch
                {
                    SerializedPropertyType.Generic or SerializedPropertyType.ManagedReference => true,
                    _ => false,
                });
            }
        }

        private static List<LifetimeScope> FindScopes(Scene scene)
        {
            var scopes = new List<LifetimeScope>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                scopes.AddRange(root.GetComponentsInChildren<LifetimeScope>(true));
            }

            return scopes;
        }

        // ------------------------------------------------------------------
        // 3) Luban item table ↔ placeholder icons
        // ------------------------------------------------------------------

        private static void ValidateItemIcons(ValidationReport report)
        {
            List<(int Id, string Category)> items = PlaceholderSpriteGenerator.ReadItemTable();
            if (items.Count == 0)
            {
                report.Error("Item table empty or unreadable (data/item.csv) — cannot validate icons.");
                return;
            }

            foreach ((int id, _) in items)
            {
                string path = PlaceholderSpriteGenerator.ItemIconPath(id);
                if (AssetDatabase.LoadAssetAtPath<Sprite>(path) is null)
                {
                    report.Error($"Item {id}: placeholder icon missing at {path} — run Generate Sprites.");
                }
            }
        }
    }
}
