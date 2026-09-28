using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ProjectF.Infrastructure;
using ProjectF.Infrastructure.UI;
using ProjectF.Presentation.Common;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
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
    /// Stage 8/9 static checks (spec items 6) — callable from the setup window
    /// AND from CI via BatchSetup.RunFullSetup/RunValidation (batch mode exits
    /// non-zero on failure). knowledge.md: generated content must be
    /// verifiable without a human in the loop.
    /// Stage 9 additions: UIRoot in Persistent with the five layers + orders,
    /// UI prefabs exist, localization CSVs cover every name_key (th/en).
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

        /// <summary>UILayer enum value → expected canvas sorting order.</summary>
        private static readonly Dictionary<string, int> ExpectedLayerOrders = new()
        {
            ["World"] = (int)UILayer.World,
            ["Hud"] = (int)UILayer.Hud,
            ["Window"] = (int)UILayer.Window,
            ["Modal"] = (int)UILayer.Modal,
            ["Toast"] = (int)UILayer.Toast,
        };

        /// <summary>Mirrors HudPresenter.LevelExpThresholds — catches drift
        /// between the client copy and data/level_exp.csv.</summary>
        private static readonly int[] ExpectedLevelThresholds =
            { 0, 50, 140, 300, 560, 950, 1500, 2300, 3400, 5000 };

        public static ValidationReport Validate()
        {
            var report = new ValidationReport();
            ValidateBuildSettings(report);
            ValidateScenes(report);
            ValidateItemIcons(report);
            ValidateUiPrefabs(report);
            ValidateLocalization(report);
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

            // --- Stage 9: UIRoot instance + layers ---
            UIRoot? uiRoot = scene.GetRootGameObjects()
                .Select(go => go.GetComponent<UIRoot>())
                .FirstOrDefault(u => u is { });
            if (uiRoot is null)
            {
                report.Error("Persistent: no UIRoot found — run Generate UI Prefabs + Generate Scenes.");
                return;
            }

            foreach (KeyValuePair<string, int> layer in ExpectedLayerOrders)
            {
                Transform child = uiRoot.transform.Find(layer.Key);
                if (child is null)
                {
                    report.Error($"UIRoot: missing layer '{layer.Key}'.");
                    continue;
                }

                Canvas? layerCanvas = child.GetComponent<Canvas>();
                if (layerCanvas is null)
                {
                    report.Error($"UIRoot: layer '{layer.Key}' has no Canvas.");
                }
                else if (!layerCanvas.overrideSorting)
                {
                    report.Error($"UIRoot: layer '{layer.Key}' Canvas lacks overrideSorting.");
                }
                else if (layerCanvas.sortingOrder != layer.Value)
                {
                    report.Error($"UIRoot: layer '{layer.Key}' sorting order is " +
                                 $"{layerCanvas.sortingOrder}, expected {layer.Value}.");
                }
            }

            Canvas rootCanvas = uiRoot.GetComponent<Canvas>();
            if (rootCanvas.renderMode != RenderMode.ScreenSpaceCamera)
            {
                report.Error($"UIRoot: render mode is {rootCanvas.renderMode}, " +
                             "expected ScreenSpaceCamera (spec 9.1).");
            }

            CanvasScaler? scaler = uiRoot.GetComponent<CanvasScaler>();
            if (scaler is null || scaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize ||
                scaler.referenceResolution != new Vector2(320f, 180f))
            {
                report.Error("UIRoot: CanvasScaler must be ScaleWithScreenSize with a 320x180 " +
                             "reference resolution (spec 9.1 pixel-perfect contract).");
            }

            if (uiRoot.GetComponentInChildren<Presentation.Common.UiInputDriver>(true) is null)
            {
                report.Error("UIRoot: UiInputDriver component missing (Escape / I / click-outside).");
            }

            if (uiRoot.GetComponentInChildren<Presentation.Common.LoadingOverlayBinder>(true) is null)
            {
                report.Error("UIRoot: LoadingOverlayBinder component missing (loading overlay).");
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

            // Stage 9: the per-scene HUD must be a bare tree (NO Canvas of its
            // own — the Persistent UIRoot owns all canvases).
            HudView? hud = scene.GetRootGameObjects()
                .SelectMany(go => go.GetComponentsInChildren<HudView>(true))
                .FirstOrDefault();
            if (hud is null)
            {
                report.Error($"{name}: no HudView instance found — run Generate Scenes.");
            }
            else if (hud.GetComponentInParent<Canvas>(true) is { })
            {
                report.Warn($"{name}: HudView sits under a Canvas in the SCENE file — the " +
                            "runtime re-parents it under UIRoot's HUD layer, but the saved " +
                            "state should be a bare tree (run Generate Scenes).");
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

        // ------------------------------------------------------------------
        // 4) Stage 9: UI prefabs + sprite registry + localization coverage
        // ------------------------------------------------------------------

        private static void ValidateUiPrefabs(ValidationReport report)
        {
            foreach (string name in new[] { "UIRoot", "Toast", "InventoryWindow", "ConfirmDialog" })
            {
                string path = UiPrefabGenerator.PrefabPath(name);
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) is null)
                {
                    report.Error($"UI prefab missing: {path} — run Generate UI Prefabs.");
                }
            }

            SpriteRegistryAsset? registry = AssetDatabase.LoadAssetAtPath<SpriteRegistryAsset>(
                EditorPaths.SettingsRoot + "/SpriteRegistry.asset");
            if (registry is null)
            {
                report.Error("SpriteRegistry.asset missing — run Generate UI Prefabs.");
            }
            else
            {
                if (registry.WhiteSquareSprite is null)
                {
                    report.Error("SpriteRegistry: WhiteSquareSprite not baked.");
                }

                if (registry.PanelSprite is null)
                {
                    report.Error("SpriteRegistry: PanelSprite not baked.");
                }

                List<(int Id, string Category)> items = PlaceholderSpriteGenerator.ReadItemTable();
                int missing = items.Count(id => id.Id > 0 &&
                    Array.IndexOf(registry.ItemIconIds, id.Id) < 0);
                if (missing > 0)
                {
                    report.Error($"SpriteRegistry: {missing} item icon(s) not baked — " +
                                 "run Generate UI Prefabs.");
                }
            }

            // HUD drift check: HudPresenter's local threshold copy must match
            // the shipped level_exp data (10 ints, cheap insurance).
            int[] actual = HudPresenter.LevelExpThresholds;
            if (!actual.SequenceEqual(ExpectedLevelThresholds))
            {
                report.Error("HudPresenter.LevelExpThresholds drifted from data/level_exp.csv " +
                             "— sync the mirror or the exp bars lie.");
            }
        }

        private static void ValidateLocalization(ValidationReport report)
        {
            List<string> required = UiPrefabGenerator.CollectRequiredKeys();
            if (required.Count == 0)
            {
                report.Error("Localization: required key set is empty (data/item.csv unreadable?).");
                return;
            }

            foreach (string lang in new[] { "th", "en" })
            {
                string path = $"{EditorPaths.LocalizationRoot}/{lang}.csv";
                if (!File.Exists(path))
                {
                    report.Error($"Localization CSV missing: {path}.");
                    continue;
                }

                var present = new HashSet<string>(StringComparer.Ordinal);
                foreach (string raw in File.ReadAllLines(path))
                {
                    string line = raw.TrimStart('\uFEFF');
                    if (line.Length == 0 || line.StartsWith("#"))
                    {
                        continue;
                    }

                    int comma = line.IndexOf(',');
                    if (comma > 0)
                    {
                        present.Add(line[..comma].Trim());
                    }
                }

                List<string> missing = required.Where(k => !present.Contains(k)).ToList();
                if (missing.Count > 0)
                {
                    report.Error($"Localization {lang}.csv missing {missing.Count} key(s): " +
                                 string.Join(", ", missing.Take(8)) +
                                 (missing.Count > 8 ? "…" : string.Empty));
                }
            }
        }
    }
}
