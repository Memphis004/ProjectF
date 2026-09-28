using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using ProjectF.Presentation.Common;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// ReSharper disable CheckNamespace
namespace ProjectF.Editor
{
    /// <summary>
    /// Stage 8 prefab factory: every prefab the scenes' LifetimeScopes wire.
    /// Field names below MUST match the runtime components' [SerializeField]
    /// names (PlayerView, RemotePlayerView, NameTagView, HudView, FishingView,
    /// SceneTransitionTrigger) — they are wired with SerializedObject by exact
    /// name, so renaming a runtime field breaks the generator, loudly.
    ///
    /// Re-running DELETES and recreates each prefab (regenerate = regenerate;
    /// scenes re-wire from disk on the next scene pass).
    /// </summary>
    public static class PrefabGenerator
    {
        private const string Root = EditorPaths.PrefabRoot;

        public static void GenerateAll()
        {
            EnsureFolder();
            CreatePlayerAnimatorController();
            CreatePlayer();
            CreateRemotePlayer();
            CreateSceneTransitionTrigger();
            CreateFishingSpot();
            CreateFarmTile();
            CreateNpc("NpcShopkeeper", EditorPaths.NpcShopkeeper);
            CreateNpc("NpcAuntie", EditorPaths.NpcAuntie);
            CreateTaskBoard();
            CreateHud();
            CreateFishingWindow();
            AssetDatabase.SaveAssets();
            Debug.Log("[prefabs] all prefabs generated.");
        }

        public static string PrefabPath(string name) => $"{Root}/{name}.prefab";

        // ------------------------------------------------------------------
        // Player
        // ------------------------------------------------------------------

        private static void CreatePlayer()
        {
            GameObject go = new("Player");
            try
            {
                var body = go.AddComponent<Rigidbody2D>();
                body.bodyType = RigidbodyType2D.Kinematic; // spec: kinematic
                body.gravityScale = 0f;
                body.constraints = RigidbodyConstraints2D.FreezeRotation;
                body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

                var collider = go.AddComponent<BoxCollider2D>();
                collider.size = new Vector2(0.6f, 0.9f);

                var renderer = go.AddComponent<SpriteRenderer>();
                renderer.sprite = LoadPlayerSprite("Down", 0);

                var animator = go.AddComponent<Animator>();
                animator.runtimeAnimatorController =
                    AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                        $"{EditorPaths.ArtRoot}/PlayerController.controller");
                animator.applyRootMotion = false;

                PlayerView playerView = go.AddComponent<PlayerView>();
                go.AddComponent<PlayerInputController>();
                go.AddComponent<PresenceBroadcaster>();
                // Stage 10: [E] interaction — nearest interactable wins.
                go.AddComponent<Infrastructure.Interaction.InteractionDetector>();

                // PlayerView [SerializeField] wiring (exact names).
                var viewSo = new SerializedObject(playerView);
                viewSo.FindProperty("animator")!.objectReferenceValue = animator;
                viewSo.FindProperty("spriteRenderer")!.objectReferenceValue = renderer;
                viewSo.ApplyModifiedPropertiesWithoutUndo();

                SavePrefab(go, "Player");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        private static Sprite? LoadPlayerSprite(string row, int frame) =>
            AssetDatabase.LoadAssetAtPath<Sprite>(
                $"{EditorPaths.ArtRoot}/Player.png[{PlaceholderSpriteGenerator.PlayerSpriteName(row, frame)}]");

        /// <summary>
        /// Minimal placeholder controller driven by the runtime's two ints
        /// (PlayerView.SetAnimation → "State" 0=Idle/1=Walk,
        /// PlayerView.SetFacing → "Facing" 0=Down/1=Up/2=Left/3=Right).
        /// Eight one-row states (4 idle + 4 walk) fully inter-connected; the
        /// transition graph is generated so a Facing change mid-walk swaps
        /// rows immediately. Real sheets drop in over the same clip names.
        /// </summary>
        private static void CreatePlayerAnimatorController()
        {
            const string controllerPath = EditorPaths.ArtRoot + "/PlayerController.controller";
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath) is { })
            {
                return;
            }

            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            controller.AddParameter("State", AnimatorControllerParameterType.Int);
            controller.AddParameter("Facing", AnimatorControllerParameterType.Int);
            AnimatorStateMachine machine = controller.layers[0].stateMachine;

            // row order == ProjectF.Shared.Presence.Direction (Down0 Up1 Left2 Right3)
            string[] rows = { "Down", "Up", "Left", "Right" };
            var states = new Dictionary<(int state, int facing), AnimatorState>();
            for (int facing = 0; facing < rows.Length; facing++)
            {
                var idle = machine.AddState($"Idle_{rows[facing]}",
                    new Vector3(240f + (facing * 130f), 40f, 0f));
                idle.motion = CreateClip(controller, $"Idle_{rows[facing]}",
                    new[] { LoadPlayerSprite(rows[facing], 0) });
                states[(0, facing)] = idle;

                var walk = machine.AddState($"Walk_{rows[facing]}",
                    new Vector3(240f + (facing * 130f), 150f, 0f));
                var frames = new Sprite?[EditorPaths.PlayerFrames];
                for (int frame = 0; frame < frames.Length; frame++)
                {
                    frames[frame] = LoadPlayerSprite(rows[facing], frame);
                }

                walk.motion = CreateClip(controller, $"Walk_{rows[facing]}", frames);
                states[(1, facing)] = walk;
            }

            machine.defaultState = states[(0, 0)]; // Idle_Down

            // Fully-connected graph: 8 states × 7 others (self-loops excluded).
            foreach (KeyValuePair<(int state, int facing), AnimatorState> from in states)
            {
                foreach (KeyValuePair<(int state, int facing), AnimatorState> to in states)
                {
                    if (from.Key.Equals(to.Key))
                    {
                        continue;
                    }

                    AnimatorStateTransition transition = from.Value.AddTransition(to.Value);
                    transition.hasExitTime = false;
                    transition.duration = 0f;
                    transition.AddCondition(AnimatorConditionMode.Equals, to.Key.state, "State");
                    transition.AddCondition(AnimatorConditionMode.Equals, to.Key.facing, "Facing");
                }
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[prefabs] PlayerController.controller generated (8 directional states).");
        }

        /// <summary>Sprite keyframes at 8 fps, looping, embedded in the
        /// controller asset so nothing is left loose.</summary>
        private static AnimationClip CreateClip(
            AnimatorController controller, string name, Sprite?[] frames)
        {
            var clip = new AnimationClip { frameRate = 8f };
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            const float frameTime = 1f / 8f;
            var keys = new ObjectReferenceKeyframe[frames.Length + 1];
            for (int i = 0; i < frames.Length; i++)
            {
                keys[i] = new ObjectReferenceKeyframe { time = i * frameTime, value = frames[i] };
            }

            // Loop tail: repeat the first frame so every frame plays.
            keys[frames.Length] = new ObjectReferenceKeyframe
            {
                time = frames.Length * frameTime,
                value = frames[0],
            };
            // No factory exists for object-reference bindings — construct directly.
            var spriteBinding = new EditorCurveBinding
            {
                path = string.Empty,
                type = typeof(SpriteRenderer),
                propertyName = "m_Sprite",
            };
            AnimationUtility.SetObjectReferenceCurve(clip, spriteBinding, keys);

            clip.name = name;
            AssetDatabase.AddObjectToAsset(clip, controller);
            return clip;
        }

        // ------------------------------------------------------------------
        // RemotePlayer
        // ------------------------------------------------------------------

        private static void CreateRemotePlayer()
        {
            GameObject go = new("RemotePlayer");
            try
            {
                var renderer = go.AddComponent<SpriteRenderer>();
                renderer.sprite = LoadPlayerSprite("Down", 0);

                GameObject nameTagGo = new("NameTag");
                nameTagGo.transform.SetParent(go.transform, false);
                nameTagGo.transform.localPosition = new Vector3(0f, 0.9f, 0f);
                var textMesh = nameTagGo.AddComponent<TextMesh>();
                textMesh.anchor = TextAnchor.MiddleCenter;
                textMesh.alignment = TextAlignment.Center;
                textMesh.fontSize = 24;
                textMesh.characterSize = 0.05f; // ≈1.2 world units tall
                textMesh.color = Color.white;
                nameTagGo.AddComponent<NameTagView>();

                var view = go.AddComponent<RemotePlayerView>();
                var so = new SerializedObject(view);
                so.FindProperty("spriteRenderer")!.objectReferenceValue = renderer;
                so.FindProperty("nameTag")!
                    .objectReferenceValue = nameTagGo.GetComponent<NameTagView>();
                so.ApplyModifiedPropertiesWithoutUndo();

                SavePrefab(go, "RemotePlayer");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        // ------------------------------------------------------------------
        // World prefabs
        // ------------------------------------------------------------------

        private static void CreateSceneTransitionTrigger()
        {
            GameObject go = new("SceneTransitionTrigger");
            try
            {
                var renderer = go.AddComponent<SpriteRenderer>();
                renderer.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(
                    $"{EditorPaths.UiRoot}/WhiteSquare.png");
                renderer.color = new Color(1f, 1f, 0.6f, 0.35f);

                var collider = go.AddComponent<BoxCollider2D>();
                collider.isTrigger = true;
                collider.size = new Vector2(0.5f, 1f);
                collider.offset = new Vector2(0f, 0.25f);

                go.AddComponent<SceneTransitionTrigger>();
                SavePrefab(go, "SceneTransitionTrigger");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        private static void CreateFishingSpot()
        {
            GameObject go = new("FishingSpot");
            try
            {
                var renderer = go.AddComponent<SpriteRenderer>();
                renderer.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(
                    PlaceholderSpriteGenerator.TileSpritePath("Water"));
                renderer.sortingOrder = 1;

                var collider = go.AddComponent<BoxCollider2D>();
                collider.isTrigger = true;
                collider.size = new Vector2(2.5f, 2f);

                go.AddComponent<FishingSpot>();
                SavePrefab(go, "FishingSpot");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        private static void CreateFarmTile()
        {
            GameObject go = new("FarmTile");
            try
            {
                var renderer = go.AddComponent<SpriteRenderer>();
                renderer.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(
                    PlaceholderSpriteGenerator.TileSpritePath("Soil"));

                var collider = go.AddComponent<BoxCollider2D>();
                collider.size = new Vector2(1f, 1f);

                go.AddComponent<FarmTileView>();
                SavePrefab(go, "FarmTile");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        private static void CreateNpc(string name, Color color)
        {
            GameObject go = new(name);
            try
            {
                var renderer = go.AddComponent<SpriteRenderer>();
                renderer.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(
                    $"{EditorPaths.UiRoot}/WhiteSquare.png");
                renderer.color = color;

                var collider = go.AddComponent<BoxCollider2D>();
                collider.size = new Vector2(1f, 1f);

                SavePrefab(go, name);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        private static void CreateTaskBoard()
        {
            GameObject go = new("TaskBoard");
            try
            {
                var renderer = go.AddComponent<SpriteRenderer>();
                renderer.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(
                    $"{EditorPaths.UiRoot}/WhiteSquare.png");
                renderer.color = new Color(0.72f, 0.55f, 0.32f);

                var collider = go.AddComponent<BoxCollider2D>();
                collider.size = new Vector2(1f, 1f);

                SavePrefab(go, "TaskBoard");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        // ------------------------------------------------------------------
        // UI prefabs (Stage 9 wiring)
        // ------------------------------------------------------------------

        private static void CreateHud()
        {
            // Stage 9: the Hud prefab is a canvas-less GameObject tree — the
            // root carries a RectTransform that STRETCHES over whatever layer
            // it is parented to (UiSceneStartup re-parents it under the live
            // UIRoot HUD layer; scene files cannot cross-reference the
            // Persistent UIRoot). The UIRoot in Persistent owns ALL
            // canvases; each gameplay scene instantiates this tree.
            GameObject canvas = new("Hud");
            var hudRoot = canvas.AddComponent<RectTransform>();
            hudRoot.anchorMin = Vector2.zero;
            hudRoot.anchorMax = Vector2.one;
            hudRoot.offsetMin = Vector2.zero;
            hudRoot.offsetMax = Vector2.zero;
            try
            {
                // Rows anchor to the TOP-LEFT of the stretched HUD root;
                // anchoredPosition.y is NEGATIVE so y grows DOWN from the top
                // edge. Coordinates are in the 320x180 reference space.
                AddHudText(canvas, "NameLabel", 4f, -3f, 150f, 10f, TextAnchor.UpperLeft);
                AddHudText(canvas, "StaminaLabel", 4f, -13f, 150f, 9f, TextAnchor.UpperLeft);

                // Stamina bar: dark back + green fill (fillAmount driven).
                var staminaBack = AddHudBar(canvas, "StaminaBarBack", 4f, -24f, 80f, 4f,
                    new Color(0f, 0f, 0f, 0.6f));
                staminaBack.type = Image.Type.Simple; // back is NOT fill-driven
                AddHudBar(canvas, "StaminaBar", 4f, -24f, 80f, 4f,
                    new Color(0.36f, 0.78f, 0.35f));

                AddHudText(canvas, "GoldLabel", 4f, -30f, 150f, 9f, TextAnchor.UpperLeft);
                AddHudText(canvas, "LevelLabel", 4f, -39f, 150f, 9f, TextAnchor.UpperLeft);

                // Fishing + cooking exp bars under the level label.
                var fishingBack = AddHudBar(canvas, "FishingExpBack", 4f, -48f, 60f, 3f,
                    new Color(0f, 0f, 0f, 0.6f));
                fishingBack.type = Image.Type.Simple;
                AddHudBar(canvas, "FishingExpBar", 4f, -48f, 60f, 3f,
                    new Color(0.30f, 0.60f, 0.80f));

                var cookingBack = AddHudBar(canvas, "CookingExpBack", 4f, -52f, 60f, 3f,
                    new Color(0f, 0f, 0f, 0.6f));
                cookingBack.type = Image.Type.Simple;
                AddHudBar(canvas, "CookingExpBar", 4f, -52f, 60f, 3f,
                    new Color(0.85f, 0.45f, 0.40f));

                AddHudText(canvas, "SceneLabel", 166f, -3f, 150f, 10f, TextAnchor.UpperRight);
                AddHudText(canvas, "TipLabel", 166f, -13f, 150f, 9f, TextAnchor.UpperRight);

                // Status dots — 8x8 squares top-right under the tip label.
                AddHudBar(canvas, "ChainStatusDot", 296f, -24f, 8f, 8f,
                    new Color(0.85f, 0.30f, 0.25f));
                AddHudBar(canvas, "PresenceStatusDot", 286f, -24f, 8f, 8f,
                    new Color(0.85f, 0.30f, 0.25f));

                // HudView + name-based wiring (HudView fields are optional at
                // runtime, but the validator requires every scope reference —
                // and the presenter expects the full HUD).
                HudView hudView = canvas.AddComponent<HudView>();
                var so = new SerializedObject(hudView);
                so.FindProperty("nameLabel")!.objectReferenceValue =
                    canvas.transform.Find("NameLabel")!.GetComponent<Text>();
                so.FindProperty("staminaLabel")!.objectReferenceValue =
                    canvas.transform.Find("StaminaLabel")!.GetComponent<Text>();
                so.FindProperty("staminaBar")!.objectReferenceValue =
                    canvas.transform.Find("StaminaBar")!.GetComponent<Image>();
                so.FindProperty("goldLabel")!.objectReferenceValue =
                    canvas.transform.Find("GoldLabel")!.GetComponent<Text>();
                so.FindProperty("levelLabel")!.objectReferenceValue =
                    canvas.transform.Find("LevelLabel")!.GetComponent<Text>();
                so.FindProperty("fishingExpBar")!.objectReferenceValue =
                    canvas.transform.Find("FishingExpBar")!.GetComponent<Image>();
                so.FindProperty("cookingExpBar")!.objectReferenceValue =
                    canvas.transform.Find("CookingExpBar")!.GetComponent<Image>();
                so.FindProperty("sceneLabel")!.objectReferenceValue =
                    canvas.transform.Find("SceneLabel")!.GetComponent<Text>();
                so.FindProperty("tipLabel")!.objectReferenceValue =
                    canvas.transform.Find("TipLabel")!.GetComponent<Text>();
                so.FindProperty("chainStatusDot")!.objectReferenceValue =
                    canvas.transform.Find("ChainStatusDot")!.GetComponent<Image>();
                so.FindProperty("presenceStatusDot")!.objectReferenceValue =
                    canvas.transform.Find("PresenceStatusDot")!.GetComponent<Image>();
                so.ApplyModifiedPropertiesWithoutUndo();

                SavePrefab(canvas, "Hud");
            }
            finally
            {
                Object.DestroyImmediate(canvas);
            }
        }

        private static void CreateFishingWindow()
        {
            GameObject root = new("FishingWindow");
            try
            {
                // Own canvas: the window is instantiated into GAMEPLAY scenes,
                // which have no canvas of their own (the HUD root lives in
                // Persistent — scene files cannot cross-reference).
                var rt = root.AddComponent<RectTransform>();
                rt.sizeDelta = new Vector2(320f, 180f);
                Canvas canvas = root.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                root.AddComponent<GraphicRaycaster>();

                var panel = AddUiImage(root, "Panel", Vector2.zero, 64f);
                panel.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(
                    $"{EditorPaths.UiRoot}/Panel.png");
                panel.type = Image.Type.Sliced;
                var panelRect = (RectTransform)panel.transform;
                panelRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 320f);
                panelRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 180f);

                AddUiText(root, "StatusLabel", new Vector2(0f, 55f));
                var castButton = AddUiButton(root, "CastButton", new Vector2(0f, -55f), 0.6f);
                var revealPanel = AddUiImage(root, "RevealPanel", new Vector2(0f, -40f), 1.6f);
                revealPanel.gameObject.SetActive(false);

                Text statusLabel = root.transform
                    .Find("StatusLabel")!.GetComponent<Text>();
                var view = root.AddComponent<FishingView>();
                var so = new SerializedObject(view);
                so.FindProperty("statusLabel")!.objectReferenceValue = statusLabel;
                so.FindProperty("castButton")!.objectReferenceValue = castButton.gameObject;
                so.FindProperty("revealPanel")!.objectReferenceValue = revealPanel.gameObject;
                so.ApplyModifiedPropertiesWithoutUndo();

                SavePrefab(root, "FishingWindow");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // ------------------------------------------------------------------
        // uGUI helpers (exact names — HudView wires by name)
        // ------------------------------------------------------------------

        private static RectTransform CreateRectTransform(
            GameObject parent, string name, Vector2 anchor, Vector2 position, Vector2 size)
        {
            GameObject go = new(name);
            go.transform.SetParent(parent.transform, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            rect.localScale = Vector3.one;
            return rect;
        }

        /// <summary>Stage 9 HUD rows: anchors TOP-LEFT of the HUD layer
        /// (0..320 x 0..180 reference space, y measured DOWN from the top).
        /// The bare-tree Hud is re-parented under the UIRoot HUD layer at
        /// runtime, where positive anchoredPosition grows right/down — these
        /// helpers give the labels screen-space coordinates directly.</summary>
        private static RectTransform CreateHudRow(
            GameObject parent, string name, float x, float y, float w, float h)
        {
            GameObject go = new(name);
            go.transform.SetParent(parent.transform, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(w, h);
            rect.localScale = Vector3.one;
            return rect;
        }

        private static Text AddHudText(
            GameObject parent, string name, float x, float y, float w, float h,
            TextAnchor anchor, int fontSize = 8)
        {
            var rect = CreateHudRow(parent, name, x, y, w, h);
            Text text = rect.gameObject.AddComponent<Text>();
            text.text = name;
            text.alignment = anchor;
            text.raycastTarget = false;
            text.color = Color.white;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            return text;
        }

        private static Image AddHudBar(
            GameObject parent, string name, float x, float y, float w, float h, Color color)
        {
            var rect = CreateHudRow(parent, name, x, y, w, h);
            Image image = rect.gameObject.AddComponent<Image>();
            image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(
                $"{EditorPaths.UiRoot}/WhiteSquare.png");
            image.color = color;
            image.type = Image.Type.Filled;
            image.fillMethod = Image.FillMethod.Horizontal;
            return image;
        }

        private static Text AddUiText(
            GameObject parent, string name, Vector2 position,
            TextAnchor anchor = TextAnchor.UpperLeft)
        {
            var rect = CreateRectTransform(parent, name, new Vector2(0.5f, 0.5f),
                position, new Vector2(160f, 24f));
            Text text = rect.gameObject.AddComponent<Text>();
            text.text = name;
            text.alignment = anchor;
            text.raycastTarget = false;
            text.color = Color.white;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 12;
            return text;
        }

        private static Image AddUiImage(
            GameObject parent, string name, Vector2 position, float size)
        {
            var rect = CreateRectTransform(parent, name, new Vector2(0.5f, 0.5f),
                position, new Vector2(size, size));
            Image image = rect.gameObject.AddComponent<Image>();
            image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(
                $"{EditorPaths.UiRoot}/WhiteSquare.png");
            image.color = new Color(0.25f, 0.55f, 0.30f);
            return image;
        }

        private static Button AddUiButton(
            GameObject parent, string name, Vector2 position, float size)
        {
            var rect = CreateRectTransform(parent, name, new Vector2(0.5f, 0.5f),
                position, new Vector2(size * 100f, size * 100f));
            Image image = rect.gameObject.AddComponent<Image>();
            image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(
                $"{EditorPaths.UiRoot}/WhiteSquare.png");
            image.color = new Color(0.30f, 0.45f, 0.70f);
            Button button = rect.gameObject.AddComponent<Button>();
            Text label = AddUiText(rect.gameObject, "Label", Vector2.zero, TextAnchor.MiddleCenter);
            label.rectTransform.sizeDelta = Vector2.zero;
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            return button;
        }

        // ------------------------------------------------------------------
        // Asset plumbing
        // ------------------------------------------------------------------

        private static void SavePrefab(GameObject go, string name)
        {
            string path = PrefabPath(name);
            Object? existing = AssetDatabase.LoadAssetAtPath<Object>(path);
            if (existing is { })
            {
                AssetDatabase.DeleteAsset(path);
            }

            Object prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            if (prefab is null)
            {
                Debug.LogError($"[prefabs] failed to save {path}");
            }
        }

        private static void EnsureFolder()
        {
            if (AssetDatabase.IsValidFolder(Root))
            {
                return;
            }

            PlaceholderSpriteGenerator.EnsureFolders();
            Directory.CreateDirectory(Root);
            AssetDatabase.Refresh();
        }
    }
}
