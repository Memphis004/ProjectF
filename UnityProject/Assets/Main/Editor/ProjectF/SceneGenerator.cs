using System;
using System.IO;
using System.Linq;
using ProjectF.Infrastructure;
using ProjectF.Infrastructure.Network;
using ProjectF.Infrastructure.Scene;
using ProjectF.Presentation.AuntieHouse;
using ProjectF.Presentation.Common;
using ProjectF.Presentation.FarmPlot;
using ProjectF.Presentation.Shop;
using ProjectF.Presentation.Village;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using UnityEngine.U2D;
using UnityEngine.UI;
using VContainer.Unity;
using Object = UnityEngine.Object;

// ReSharper disable CheckNamespace
namespace ProjectF.Editor
{
    /// <summary>
    /// Stage 8 scene factory: Persistent (index 0, never unloaded) + the four
    /// gameplay scenes (knowledge.md "Unity scene model"). Camera, EventSystem
    /// and the HUD canvas root live ONLY in Persistent — it stays loaded all
    /// session, so gameplay scenes must not duplicate them.
    ///
    /// Cross-scene wiring is safe here because VContainer's parentReference is
    /// a TYPE NAME (resolved by type at runtime), so every scene file is built
    /// and saved independently in Single mode — no multi-scene editing pass.
    /// The HudView/FishingView instances live INSIDE their own gameplay scene
    /// (a scene file cannot reference another scene's objects).
    /// </summary>
    public static class SceneGenerator
    {
        private const string ScenesRoot = EditorPaths.SceneRoot;

        /// <summary>VContainer parentReference.TypeName — resolved BY TYPE at
        /// runtime (ParentReference.OnAfterDeserialize), so it cannot go stale.</summary>
        private const string ParentTypeName = "ProjectF.Infrastructure.RootLifetimeScope";

        private static readonly string[] SceneNames =
        {
            "Persistent", "Village", "Shop", "AuntieHouse", "FarmPlot",
        };

        public static void GenerateAll()
        {
            EnsureFolder();
            BuildSettings();
            BuildPersistent();
            BuildVillage();
            BuildShop();
            BuildAuntieHouse();
            BuildFarmPlot();

            // Each builder left a NEW empty scene active — reopen Persistent
            // so the editor is left in a sane state.
            EditorSceneManager.OpenScene($"{ScenesRoot}/Persistent.unity", OpenSceneMode.Single);
            Debug.Log("[scenes] generated 5 scenes; Persistent at build index 0.");
        }

        /// <summary>Stage 9 UI prefab paths (UiPrefabGenerator output).</summary>
        private static GameObject LoadUiPrefab(string name)
        {
            GameObject? prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                UiPrefabGenerator.PrefabPath(name));
            if (prefab is null)
            {
                throw new InvalidOperationException(
                    $"[scenes] UI prefab '{name}' missing — run Generate UI Prefabs first.");
            }

            return prefab;
        }

        public static void BuildSettings()
        {
            EditorBuildSettings.scenes = SceneNames
                .Select(name => new EditorBuildSettingsScene($"{ScenesRoot}/{name}.unity", true))
                .ToArray();
            Debug.Log("[scenes] build settings updated (Persistent = index 0).");
        }

        // ------------------------------------------------------------------
        // Persistent — camera + EventSystem + RootLifetimeScope + HUD root
        // ------------------------------------------------------------------

        public static void BuildPersistent()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            GameObject cameraGo = new("Main Camera", typeof(Camera));
            cameraGo.tag = "MainCamera";
            Camera camera = cameraGo.GetComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 90f;      // ~11 tiles tall at PPU 16 (320x180 ref)
            camera.nearClipPlane = -10f;
            camera.farClipPlane = 500f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.35f, 0.55f, 0.45f, 1f);

            PixelPerfectCamera ppc = cameraGo.AddComponent<PixelPerfectCamera>();
            ppc.assetsPPU = EditorPaths.PixelsPerUnit;
            ppc.refResolutionX = 320;
            ppc.refResolutionY = 180;
            ppc.upscaleRT = false;              // spec: upscale RT off
            ppc.pixelSnapping = true;
            ppc.cropFrameX = true;
            ppc.cropFrameY = true;

            new GameObject("EventSystem",
                typeof(EventSystem), typeof(StandaloneInputModule));

            // Stage 9: UIRoot prefab (Screen Space - Camera canvas on THIS
            // camera, five layers, loading overlay) — instanced in Persistent
            // so it survives all scene switches.
            GameObject uiRootGo = Object.Instantiate(LoadUiPrefab("UIRoot"));
            uiRootGo.name = "UIRoot";
            Canvas uiCanvas = uiRootGo.GetComponent<Canvas>();
            uiCanvas.renderMode = RenderMode.ScreenSpaceCamera;
            uiCanvas.worldCamera = camera;
            uiCanvas.planeDistance = 10f;

            GameObject scopeGo = new("RootLifetimeScope");
            RootLifetimeScope root = scopeGo.AddComponent<RootLifetimeScope>();
            NetworkSettings settings = SettingsAssetGenerator.Generate(false);
            if (settings is null)
            {
                Debug.LogError("[scenes] NetworkSettings asset missing — run Generate Settings first.");
            }
            else
            {
                SerializedObject so = new(root);
                so.FindProperty("networkSettings")!.objectReferenceValue = settings;
                so.FindProperty("uiRoot")!.objectReferenceValue =
                    uiRootGo.GetComponent<Infrastructure.UI.UIRoot>();
                so.FindProperty("toastPrefab")!.objectReferenceValue =
                    LoadUiPrefab("Toast").transform as RectTransform;
                so.FindProperty("inventoryWindowPrefab")!.objectReferenceValue =
                    LoadUiPrefab("InventoryWindow").transform as RectTransform;
                so.FindProperty("confirmDialogPrefab")!.objectReferenceValue =
                    LoadUiPrefab("ConfirmDialog").transform as RectTransform;
                so.FindProperty("spriteRegistry")!.objectReferenceValue =
                    AssetDatabase.LoadAssetAtPath<SpriteRegistryAsset>(
                        EditorPaths.SettingsRoot + "/SpriteRegistry.asset");
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            EditorSceneManager.SaveScene(scene, $"{ScenesRoot}/Persistent.unity");
            Debug.Log("[scenes] Persistent saved (camera PPU 16 / 320x180 / upscale RT off, UIRoot wired).");
        }

        // ------------------------------------------------------------------
        // Gameplay scenes
        // ------------------------------------------------------------------

        public static void BuildVillage() => BuildGameplay(
            "Village",
            prefabs =>
            {
                CreateTilemap(24, 14, "Grass");
                PlaceTransition(prefabs, new Vector2(-10f, 0f), SceneId.Shop, new Vector2(2f, 0f));
                PlaceTransition(prefabs, new Vector2(10f, 0f), SceneId.AuntieHouse, new Vector2(-2f, 0f));
                PlaceTransition(prefabs, new Vector2(0f, -8f), SceneId.FarmPlot, new Vector2(0f, 4f));
                Object.Instantiate(prefabs.TaskBoard, new Vector3(4f, 2f, 0f), Quaternion.identity);
            });

        public static void BuildShop() => BuildGameplay(
            "Shop",
            prefabs =>
            {
                CreateTilemap(16, 10, "WoodFloor");
                Object.Instantiate(prefabs.NpcShopkeeper, new Vector3(0f, 2f, 0f), Quaternion.identity);
                PlaceTransition(prefabs, new Vector2(0f, -4f), SceneId.Village, new Vector2(0f, -2f));
            });

        public static void BuildAuntieHouse() => BuildGameplay(
            "AuntieHouse",
            prefabs =>
            {
                CreateTilemap(16, 10, "WoodFloor");
                Object.Instantiate(prefabs.NpcAuntie, new Vector3(0f, 2f, 0f), Quaternion.identity);
                CreateInteractionPoint("Kitchen", new Vector2(2f, -2f));
                PlaceTransition(prefabs, new Vector2(0f, -4f), SceneId.Village, new Vector2(0f, -2f));
            });

        public static void BuildFarmPlot() => BuildGameplay(
            "FarmPlot",
            prefabs =>
            {
                CreateTilemap(20, 12, "Grass");
                int plotIndex = 0;
                for (int x = -1; x <= 1; x++)
                {
                    for (int y = 1; y <= 3; y++)
                    {
                        GameObject tile = Object.Instantiate(
                            prefabs.FarmTile, new Vector3(x, y, 0f), Quaternion.identity);
                        SerializedObject tileSo = new(tile.GetComponent<FarmTileView>());
                        tileSo.FindProperty("plotIndex")!.intValue = plotIndex++;
                        tileSo.ApplyModifiedPropertiesWithoutUndo();
                    }
                }

                // The village pond (data/pond.csv: pond 1, scene_id 4 = FarmPlot).
                Object.Instantiate(prefabs.FishingSpot, new Vector3(5f, 0f, 0f), Quaternion.identity);
                PlaceTransition(prefabs, new Vector2(0f, -5f), SceneId.Village, new Vector2(0f, -3f));
            });

        // ------------------------------------------------------------------
        // Shared gameplay scaffold
        // ------------------------------------------------------------------

        private sealed class PrefabSet
        {
            public GameObject Player = null!;
            public GameObject RemotePlayer = null!;
            public GameObject TransitionTrigger = null!;
            public GameObject FishingSpot = null!;
            public GameObject FarmTile = null!;
            public GameObject NpcShopkeeper = null!;
            public GameObject NpcAuntie = null!;
            public GameObject TaskBoard = null!;
            public GameObject Hud = null!;
            public GameObject FishingWindow = null!;
        }

        private static void BuildGameplay(string name, Action<PrefabSet> build)
        {
            var prefabs = LoadPrefabs();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            try
            {
                GameObject scopeGo = new(name + "LifetimeScope");

                // Scene content first, so the Spawn child (below) sits last.
                build(prefabs);

                LifetimeScope scope = name switch
                {
                    "Village" => scopeGo.AddComponent<VillageLifetimeScope>(),
                    "Shop" => scopeGo.AddComponent<ShopLifetimeScope>(),
                    "AuntieHouse" => scopeGo.AddComponent<AuntieHouseLifetimeScope>(),
                    "FarmPlot" => scopeGo.AddComponent<FarmPlotLifetimeScope>(),
                    _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown gameplay scene."),
                };

                GameObject spawn = new("Spawn");
                spawn.transform.SetParent(scopeGo.transform, false);

                // Stage 8: the Hud prefab is a bare GameObject tree (no canvas
                // — the UIRoot in Persistent owns all canvases). Instantiated
                // per scene (scene files cannot cross-reference the UIRoot);
                // UiSceneStartup re-parents it under the live HUD layer.
                GameObject hud = Object.Instantiate(prefabs.Hud);
                hud.name = "HUD";

                SerializedObject so = new(scope);
                so.FindProperty("playerPrefab")!.objectReferenceValue =
                    prefabs.Player.GetComponent<PlayerView>();
                so.FindProperty("remotePlayerPrefab")!.objectReferenceValue =
                    prefabs.RemotePlayer.GetComponent<RemotePlayerView>();
                so.FindProperty("hudView")!.objectReferenceValue = hud.GetComponent<HudView>();
                so.FindProperty("spawnPoint")!.objectReferenceValue = spawn.transform;
                if (so.FindProperty("fishingView") is { } fishingProperty)
                {
                    GameObject fishingWindow = Object.Instantiate(prefabs.FishingWindow);
                    fishingWindow.name = "FishingWindow";
                    fishingProperty.objectReferenceValue =
                        fishingWindow.GetComponent<FishingView>();
                }

                SerializedProperty? parentProperty = so.FindProperty("parentReference");
                if (parentProperty is { })
                {
                    parentProperty.FindPropertyRelative("TypeName")!.stringValue = ParentTypeName;
                }

                so.ApplyModifiedPropertiesWithoutUndo();

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene, $"{ScenesRoot}/{name}.unity");
            }
            catch (Exception)
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                throw;
            }
        }

        private static PrefabSet LoadPrefabs()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabGenerator.PrefabPath("Player")) is null)
            {
                throw new InvalidOperationException(
                    "[scenes] prefabs missing — run Generate Prefabs (or Full Setup) first.");
            }

            return new PrefabSet
            {
                Player = LoadPrefab("Player"),
                RemotePlayer = LoadPrefab("RemotePlayer"),
                TransitionTrigger = LoadPrefab("SceneTransitionTrigger"),
                FishingSpot = LoadPrefab("FishingSpot"),
                FarmTile = LoadPrefab("FarmTile"),
                NpcShopkeeper = LoadPrefab("NpcShopkeeper"),
                NpcAuntie = LoadPrefab("NpcAuntie"),
                TaskBoard = LoadPrefab("TaskBoard"),
                Hud = LoadPrefab("Hud"),
                FishingWindow = LoadPrefab("FishingWindow"),
            };
        }

        private static GameObject LoadPrefab(string name)
        {
            GameObject? prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                PrefabGenerator.PrefabPath(name));
            if (prefab is null)
            {
                throw new InvalidOperationException($"[scenes] prefab '{name}' missing.");
            }

            return prefab;
        }

        private static void PlaceTransition(
            PrefabSet prefabs, Vector2 at, SceneId target, Vector2 spawnAt)
        {
            GameObject trigger = Object.Instantiate(
                prefabs.TransitionTrigger, at, Quaternion.identity);
            SerializedObject so = new(trigger.GetComponent<SceneTransitionTrigger>());
            so.FindProperty("targetScene")!.intValue = (int)target;
            so.FindProperty("spawnAt")!.vector2Value = spawnAt;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void CreateTilemap(int width, int height, string tileName)
        {
            GameObject go = new("Ground");
            Tilemap map = go.AddComponent<Tilemap>();
            go.AddComponent<TilemapRenderer>();

            TileBase? tile = PlaceholderSpriteGenerator.GetTileAsset(tileName);
            if (tile is null)
            {
                Debug.LogError($"[scenes] Tile asset '{tileName}' missing — run Generate Sprites first.");
                return;
            }

            for (int x = -width / 2; x < width - width / 2; x++)
            {
                for (int y = -height / 2; y < height - height / 2; y++)
                {
                    map.SetTile(new Vector3Int(x, y, 0), tile);
                }
            }

            map.CompressBounds();
        }

        /// <summary>Generic interactable placeholder (Auntie's kitchen; Stage 10
        /// attaches the craft presenter).</summary>
        private static void CreateInteractionPoint(string name, Vector2 at)
        {
            GameObject go = new(name);
            go.transform.position = at;
            SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(
                $"{EditorPaths.UiRoot}/WhiteSquare.png");
            renderer.color = new Color(0.8f, 0.5f, 0.2f, 0.6f);
            BoxCollider2D collider = go.AddComponent<BoxCollider2D>();
            collider.isTrigger = true;
        }

        private static void EnsureFolder()
        {
            if (AssetDatabase.IsValidFolder(ScenesRoot))
            {
                return;
            }

            if (!AssetDatabase.IsValidFolder("Assets/Main"))
            {
                AssetDatabase.CreateFolder("Assets", "Main");
            }

            if (!AssetDatabase.IsValidFolder("Assets/Main/Scenes"))
            {
                AssetDatabase.CreateFolder("Assets/Main", "Scenes");
            }
        }
    }
}
