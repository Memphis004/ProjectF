using System.Collections.Generic;
using System.IO;
using System.Linq;
using ProjectF.Infrastructure;
using ProjectF.Presentation.Common;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// ReSharper disable CheckNamespace
namespace ProjectF.Editor
{
    /// <summary>
    /// Stage 9 UI prefab factory (menu ProjectF/Setup/Generate UI Prefabs).
    /// Builds EVERY UI prefab from placeholder sprites so nothing is
    /// hand-assembled (knowledge.md "Editor-generated content"):
    ///
    /// - UIRoot        — 320x180 Screen Space - Camera canvas, Pixel Perfect,
    ///                   five child canvases World/HUD/Window/Modal/Toast
    ///                   (sorting 0/10/20/30/40), LoadingOverlay + binder,
    ///                   SpriteRegistrySource. Saved to Prefabs/UI/UIRoot.
    /// - Toast         — icon + message + spinner row.
    /// - InventoryWindow — tab row, scroll grid, tooltip.
    /// - ConfirmDialog — modal with title/body/confirm/cancel.
    /// - SpriteRegistry.asset — baked WhiteSquare/Panel/item-icon references.
    ///
    /// Field wiring is SerializedObject-by-exact-name like Stage 8's
    /// PrefabGenerator: renaming a runtime [SerializeField] breaks this
    /// generator loudly (by design).
    ///
    /// NOTE: uGUI's default "UI/Default" material handles Thai glyphs through
    /// LegacyRuntime.ttf fallbacks at runtime; the generator only needs the
    /// standard Text components.
    /// </summary>
    public static class UiPrefabGenerator
    {
        private const string Root = EditorPaths.PrefabRoot + "/UI";
        private const string SettingsRoot = EditorPaths.SettingsRoot;

        /// <summary>Layer name → sorting order (UILayer enum values).</summary>
        private static readonly (string Name, int Order, string ColorHex)[] Layers =
        {
            ("World", 0, "#3A3A3A"),
            ("Hud", 10, "#2E2E2E"),
            ("Window", 20, "#262626"),
            ("Modal", 30, "#1E1E1E"),
            ("Toast", 40, "#161616"),
        };

        [MenuItem("ProjectF/Setup/Generate UI Prefabs", priority = 31)]
        public static void GenerateFromMenu()
        {
            try
            {
                GenerateAll();
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[ui-prefabs] FAILED — {ex}");
            }
        }

        /// <summary>Every name_key a localization CSV must cover (the validator
        /// compares this against th.csv/en.csv). UI keys + the data tables'
        /// name_key columns (item/pond/recipe) — data/item.csv is parsed
        /// directly (single source of truth, same as the icon generator).</summary>
        public static List<string> CollectRequiredKeys()
        {
            var keys = new List<string>();
            // UI chrome + toasts (keep in sync with Resources/Localization).
            keys.AddRange(new[]
            {
                "UI_STAMINA", "UI_GOLD", "UI_LEVEL", "UI_SCENE",
                "UI_CHAIN_SYNCED", "UI_CHAIN_SYNCING", "UI_CHAIN_BOOTSTRAPPING",
                "UI_CHAIN_OFFLINE", "UI_PRESENCE_ONLINE", "UI_PRESENCE_OFFLINE",
                "UI_LOADING", "UI_LOADING_SYNCING_BLOCK", "UI_LOADING_SYNCING_BLOCK_TOTAL",
                "UI_CONFIRM", "UI_CANCEL", "UI_INVENTORY",
                "UI_TAB_ALL", "UI_TAB_BAIT", "UI_TAB_ROD", "UI_TAB_FISH",
                "UI_TAB_SEED", "UI_TAB_CROP", "UI_TAB_MATERIAL", "UI_TAB_FOOD",
                "UI_TOOLTIP_PRICE", "UI_TOOLTIP_COUNT", "UI_EMPTY_INVENTORY",
                "TOAST_CHAIN_OFFLINE", "TOAST_PRESENCE_OFFLINE", "TOAST_ACTION_PENDING",
            });

            // Items (from data/item.csv — the Luban source of truth).
            foreach (string key in ReadNameKeysFromItemCsv())
            {
                keys.Add(key);
            }

            // Ponds + recipes (tiny tables — keep in sync with data/*.csv).
            keys.AddRange(new[]
            {
                "POND_NAME_VILLAGE_SMALL",
                "RECIPE_NAME_GRILLED_FISH", "RECIPE_NAME_FISH_SOUP", "RECIPE_NAME_SPICY_FISH",
            });

            return keys.Distinct().ToList();
        }

        /// <summary>Reads the name_key column (index 2) out of data/item.csv.</summary>
        private static List<string> ReadNameKeysFromItemCsv()
        {
            var keys = new List<string>();

            string csvPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(
                UnityEngine.Application.dataPath, "..", "..", "data", "item.csv"));
            if (!System.IO.File.Exists(csvPath))
            {
                return keys;
            }

            foreach (string raw in System.IO.File.ReadAllLines(csvPath))
            {
                string line = raw.TrimStart('\uFEFF');
                if (line.Length == 0 || line.StartsWith("##"))
                {
                    continue;
                }

                string[] fields = line.Split(',');
                if (fields.Length >= 3 && fields[2].StartsWith("ITEM_NAME_"))
                {
                    keys.Add(fields[2].Trim());
                }
            }

            return keys;
        }

        public static void GenerateAll()
        {
            EnsureFolder();
            PlaceholderSpriteGenerator.GenerateUiSprites();

            var registry = BuildSpriteRegistryAsset();

            CreateUiRoot(registry);
            CreateToast();
            CreateInventoryWindow(registry);
            CreateConfirmDialog(registry);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[ui-prefabs] UIRoot, Toast, InventoryWindow, ConfirmDialog + SpriteRegistry generated.");
        }

        public static string PrefabPath(string name) => $"{Root}/{name}.prefab";

        // ------------------------------------------------------------------
        // Sprite registry (baked AssetDatabase references → runtime lookup)
        // ------------------------------------------------------------------

        private static SpriteRegistryAsset BuildSpriteRegistryAsset()
        {
            string path = $"{SettingsRoot}/SpriteRegistry.asset";
            SpriteRegistryAsset asset = AssetDatabase.LoadAssetAtPath<SpriteRegistryAsset>(path);
            if (asset is null)
            {
                asset = ScriptableObject.CreateInstance<SpriteRegistryAsset>();
                AssetDatabase.CreateAsset(asset, path);
            }

            asset.WhiteSquareSprite = AssetDatabase.LoadAssetAtPath<Sprite>(
                EditorPaths.UiRoot + "/WhiteSquare.png");
            asset.PanelSprite = AssetDatabase.LoadAssetAtPath<Sprite>(
                EditorPaths.UiRoot + "/Panel.png");

            List<(int Id, string Category)> items = PlaceholderSpriteGenerator.ReadItemTable();
            var ids = new List<int>(items.Count);
            var sprites = new List<Sprite>(items.Count);
            foreach ((int id, _) in items)
            {
                Sprite? icon = AssetDatabase.LoadAssetAtPath<Sprite>(
                    PlaceholderSpriteGenerator.ItemIconPath(id));
                if (icon is { })
                {
                    ids.Add(id);
                    sprites.Add(icon);
                }
            }

            asset.ItemIconIds = ids.ToArray();
            asset.ItemIconSprites = sprites.ToArray();
            EditorUtility.SetDirty(asset);
            return asset;
        }

        // ------------------------------------------------------------------
        // UIRoot — layers + loading overlay + input driver
        // ------------------------------------------------------------------

        private static void CreateUiRoot(SpriteRegistryAsset registry)
        {
            GameObject root = new("UIRoot");
            try
            {
                var rect = root.AddComponent<RectTransform>();
                rect.sizeDelta = new Vector2(320f, 180f);

                var canvas = root.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = Camera.main; // generator-time best effort; SceneGenerator rewires
                canvas.planeDistance = 10f;
                canvas.sortingOrder = 0;
                root.AddComponent<CanvasScaler>().uiScaleMode =
                    CanvasScaler.ScaleMode.ScaleWithScreenSize;
                CanvasScaler scaler = root.GetComponent<CanvasScaler>();
                scaler.referenceResolution = new Vector2(320f, 180f);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
                scaler.matchWidthOrHeight = 0.5f;
                root.AddComponent<GraphicRaycaster>();

                // Pixel-perfect note: the WORLD camera carries the builtin
                // PixelPerfectCamera (Stage 8); for the UI canvas the
                // reference-resolution scaler + Point-filtered sprites give
                // the crisp result without a second PPC component (the
                // builtin package's PPC has no canvas interaction).
                _ = registry; // registry is referenced via SpriteRegistrySource below

                // --- five named layers (order == UILayer enum values) ---
                var layerMap = new Dictionary<string, RectTransform>();
                foreach ((string name, int order, string hex) in Layers)
                {
                    GameObject layerGo = new(name);
                    layerGo.transform.SetParent(root.transform, false);
                    var layerRect = layerGo.AddComponent<RectTransform>();
                    Stretch(layerRect);
                    var layerCanvas = layerGo.AddComponent<Canvas>();
                    layerCanvas.overrideSorting = true;
                    layerCanvas.sortingOrder = order;
                    layerGo.AddComponent<CanvasRenderer>();
                    ColorUtility.TryParseHtmlString(hex, out Color tint);
                    Debug.Log($"[ui-prefabs] layer {name} sorting {order} (tint {ColorUtility.ToHtmlStringRGB(tint)} reserved for placeholder art).");
                    layerMap[name] = layerRect;
                }

                // Wire the layer fields on the UIRoot component — the prefab
                // MUST be self-contained (a missing assignment made GetLayer
                // throw at runtime, Stage 9 lesson).
                var rootComponent = root.AddComponent<Infrastructure.UI.UIRoot>();
                var rootSo = new SerializedObject(rootComponent);
                rootSo.FindProperty("worldLayer")!.objectReferenceValue = layerMap["World"];
                rootSo.FindProperty("hudLayer")!.objectReferenceValue = layerMap["Hud"];
                rootSo.FindProperty("windowLayer")!.objectReferenceValue = layerMap["Window"];
                rootSo.FindProperty("modalLayer")!.objectReferenceValue = layerMap["Modal"];
                rootSo.FindProperty("toastLayer")!.objectReferenceValue = layerMap["Toast"];
                rootSo.ApplyModifiedPropertiesWithoutUndo();

                var rootComponentCheck = root.GetComponent<Infrastructure.UI.UIRoot>();
                if (rootComponentCheck == null)
                {
                    throw new IOException("UIRoot component missing after wiring (generator bug).");
                }

                // --- loading overlay on the Toast (top) layer ---
                GameObject overlayGo = new("LoadingOverlay");
                overlayGo.transform.SetParent(layerMap["Toast"], false);
                var overlayRect = overlayGo.AddComponent<RectTransform>();
                Stretch(overlayRect);
                var overlayImage = overlayGo.AddComponent<Image>();
                overlayImage.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(
                    EditorPaths.UiRoot + "/WhiteSquare.png");
                overlayImage.color = new Color(0f, 0f, 0f, 0.85f);
                overlayImage.raycastTarget = true;

                Text title = AddText(overlayGo, "Title", Vector2.zero,
                    new Vector2(300f, 20f), TextAnchor.MiddleCenter, 12);
                title.text = "Loading";
                Text progress = AddText(overlayGo, "Progress", new Vector2(0f, -24f),
                    new Vector2(300f, 16f), TextAnchor.MiddleCenter, 10);
                progress.text = "syncing block 0";

                var overlay = overlayGo.AddComponent<Infrastructure.UI.LoadingOverlay>();
                var so = new SerializedObject(overlay);
                so.FindProperty("dim")!.objectReferenceValue = overlayImage;
                so.FindProperty("titleLabel")!.objectReferenceValue = title;
                so.FindProperty("progressLabel")!.objectReferenceValue = progress;
                so.ApplyModifiedPropertiesWithoutUndo();
                overlayGo.AddComponent<Presentation.Common.LoadingOverlayBinder>();

                // --- sprite source + input driver ---
                var source = root.AddComponent<SpriteRegistrySource>();
                var soSrc = new SerializedObject(source);
                soSrc.FindProperty("whiteSquare")!.objectReferenceValue =
                    AssetDatabase.LoadAssetAtPath<Sprite>(EditorPaths.UiRoot + "/WhiteSquare.png");
                soSrc.ApplyModifiedPropertiesWithoutUndo();
                root.AddComponent<Presentation.Common.UiInputDriver>();

                SavePrefab(root, "UIRoot");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        // ------------------------------------------------------------------
        // Toast prefab (pooled N times by ToastService)
        // ------------------------------------------------------------------

        private static void CreateToast()
        {
            GameObject row = new("Toast");
            try
            {
                var rect = row.AddComponent<RectTransform>();
                rect.sizeDelta = new Vector2(150f, 16f);

                var panel = row.AddComponent<Image>();
                panel.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(
                    EditorPaths.UiRoot + "/Panel.png");
                panel.type = Image.Type.Sliced;
                panel.color = new Color(0f, 0f, 0f, 0.8f);

                var layout = row.AddComponent<HorizontalLayoutGroup>();
                layout.padding = new RectOffset(4, 4, 2, 2);
                layout.spacing = 4f;
                layout.childAlignment = TextAnchor.MiddleLeft;
                layout.childControlWidth = false;
                layout.childControlHeight = false;
                layout.childForceExpandWidth = false;
                layout.childForceExpandHeight = false;

                // Icon: small image + glyph text on top (Stage 13 swaps art).
                GameObject iconGo = new("Icon");
                iconGo.transform.SetParent(row.transform, false);
                var iconRect = iconGo.AddComponent<RectTransform>();
                iconRect.sizeDelta = new Vector2(12f, 12f);
                var icon = iconGo.AddComponent<Image>();
                icon.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(
                    EditorPaths.UiRoot + "/WhiteSquare.png");
                Text glyph = AddText(iconGo, "Glyph", Vector2.zero, new Vector2(12f, 12f),
                    TextAnchor.MiddleCenter, 8);
                glyph.text = "i";
                glyph.transform.SetAsLastSibling();

                GameObject labelGo = new("Message");
                labelGo.transform.SetParent(row.transform, false);
                var labelRect = labelGo.AddComponent<RectTransform>();
                labelRect.sizeDelta = new Vector2(128f, 12f);
                Text message = labelGo.AddComponent<Text>();
                message.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                message.fontSize = 9;
                message.alignment = TextAnchor.MiddleLeft;
                message.color = Color.white;
                message.raycastTarget = false;

                // Spinner: white square wedge that ToastView rotates while pending.
                GameObject spinnerGo = new("Spinner");
                spinnerGo.transform.SetParent(row.transform, false);
                var spinnerRect = spinnerGo.AddComponent<RectTransform>();
                spinnerRect.sizeDelta = new Vector2(8f, 8f);
                var spinner = spinnerGo.AddComponent<Image>();
                spinner.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(
                    EditorPaths.UiRoot + "/WhiteSquare.png");
                spinner.color = new Color(1f, 1f, 1f, 0.9f);
                // Half-transparent overlay look: thin vertical bar pivoted low
                // so the rotation reads as a clock hand at 320x180.
                spinnerRect.pivot = new Vector2(0.5f, 0.1f);
                spinnerRect.sizeDelta = new Vector2(2f, 8f);

                var view = row.AddComponent<Infrastructure.UI.ToastView>();
                var so = new SerializedObject(view);
                so.FindProperty("messageLabel")!.objectReferenceValue = message;
                so.FindProperty("icon")!.objectReferenceValue = icon;
                so.FindProperty("spinner")!.objectReferenceValue = spinner;
                so.ApplyModifiedPropertiesWithoutUndo();

                SavePrefab(row, "Toast");
            }
            finally
            {
                Object.DestroyImmediate(row);
            }
        }

        // ------------------------------------------------------------------
        // InventoryWindow — tabs + grid + tooltip
        // ------------------------------------------------------------------

        private static void CreateInventoryWindow(SpriteRegistryAsset registry)
        {
            GameObject root = new("InventoryWindow");
            try
            {
                var rect = root.AddComponent<RectTransform>();
                rect.sizeDelta = new Vector2(260f, 150f);

                var backdrop = root.AddComponent<Image>();
                backdrop.sprite = registry.PanelSprite;
                backdrop.type = Image.Type.Sliced;
                backdrop.color = new Color(0.13f, 0.11f, 0.09f, 0.96f);

                Text title = AddText(root, "Title", new Vector2(0f, 66f),
                    new Vector2(240f, 14f), TextAnchor.MiddleCenter, 11);
                title.text = "Inventory";
                title.color = new Color(0.95f, 0.9f, 0.8f);

                // --- tab row (template is the first inactive child) ---
                GameObject tabRowGo = new("TabRow");
                tabRowGo.transform.SetParent(root.transform, false);
                var tabRowRect = tabRowGo.AddComponent<RectTransform>();
                tabRowRect.anchorMin = new Vector2(0.5f, 1f);
                tabRowRect.anchorMax = new Vector2(0.5f, 1f);
                tabRowRect.anchoredPosition = new Vector2(0f, -30f);
                tabRowRect.sizeDelta = new Vector2(240f, 14f);
                var tabLayout = tabRowGo.AddComponent<HorizontalLayoutGroup>();
                tabLayout.spacing = 2f;
                tabLayout.childControlWidth = false;
                tabLayout.childControlHeight = false;
                tabLayout.childForceExpandWidth = false;
                tabLayout.childForceExpandHeight = false;

                GameObject tabTemplate = new("TabTemplate");
                tabTemplate.transform.SetParent(tabRowGo.transform, false);
                var tabRect = tabTemplate.AddComponent<RectTransform>();
                tabRect.sizeDelta = new Vector2(30f, 13f);
                var tabImage = tabTemplate.AddComponent<Image>();
                tabImage.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(
                    EditorPaths.UiRoot + "/WhiteSquare.png");
                tabImage.color = new Color(0.35f, 0.3f, 0.24f);
                tabTemplate.AddComponent<Button>();
                Text tabText = AddText(tabTemplate, "Label", Vector2.zero,
                    new Vector2(30f, 13f), TextAnchor.MiddleCenter, 7);
                tabText.text = "All";
                tabTemplate.SetActive(false);

                // --- grid (inside a viewport-less simple layout; 29 items max) ---
                GameObject gridGo = new("Grid");
                gridGo.transform.SetParent(root.transform, false);
                var gridRect = gridGo.AddComponent<RectTransform>();
                gridRect.anchorMin = new Vector2(0.5f, 0.5f);
                gridRect.anchorMax = new Vector2(0.5f, 0.5f);
                gridRect.anchoredPosition = new Vector2(0f, -6f);
                gridRect.sizeDelta = new Vector2(240f, 84f);
                var gridLayout = gridGo.AddComponent<GridLayoutGroup>();
                gridLayout.cellSize = new Vector2(20f, 20f);
                gridLayout.spacing = new Vector2(2f, 2f);
                gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                gridLayout.constraintCount = 10;

                GameObject slotTemplate = new("SlotTemplate");
                slotTemplate.transform.SetParent(gridGo.transform, false);
                var slotRect = slotTemplate.AddComponent<RectTransform>();
                slotRect.sizeDelta = new Vector2(20f, 20f);
                var slotImage = slotTemplate.AddComponent<Image>();
                slotImage.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(
                    EditorPaths.UiRoot + "/WhiteSquare.png");
                slotImage.color = new Color(0.22f, 0.2f, 0.18f);
                slotTemplate.AddComponent<Button>();

                GameObject iconSlotGo = new("Icon");
                iconSlotGo.transform.SetParent(slotTemplate.transform, false);
                var iconSlotRect = iconSlotGo.AddComponent<RectTransform>();
                Stretch(iconSlotRect);
                iconSlotRect.offsetMin = new Vector2(2f, 2f);
                iconSlotRect.offsetMax = new Vector2(-2f, -2f);
                var slotIcon = iconSlotGo.AddComponent<Image>();
                slotIcon.sprite = registry.WhiteSquareSprite;
                slotIcon.raycastTarget = false;

                GameObject countGo = new("Count");
                countGo.transform.SetParent(slotTemplate.transform, false);
                var countRect = countGo.AddComponent<RectTransform>();
                countRect.anchorMin = new Vector2(1f, 0f);
                countRect.anchorMax = new Vector2(1f, 0f);
                countRect.pivot = new Vector2(1f, 0f);
                countRect.anchoredPosition = Vector2.zero;
                countRect.sizeDelta = new Vector2(18f, 8f);
                Text count = countGo.AddComponent<Text>();
                count.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                count.fontSize = 7;
                count.alignment = TextAnchor.LowerRight;
                count.color = Color.white;
                count.raycastTarget = false;
                slotTemplate.SetActive(false);

                // --- tooltip ---
                GameObject tooltipGo = new("TooltipPanel");
                tooltipGo.transform.SetParent(root.transform, false);
                var tooltipRect = tooltipGo.AddComponent<RectTransform>();
                tooltipRect.anchorMin = new Vector2(0.5f, 0f);
                tooltipRect.anchorMax = new Vector2(0.5f, 0f);
                tooltipRect.pivot = new Vector2(0.5f, 0f);
                tooltipRect.anchoredPosition = new Vector2(0f, 2f);
                tooltipRect.sizeDelta = new Vector2(240f, 26f);
                var tooltipImage = tooltipGo.AddComponent<Image>();
                tooltipImage.sprite = registry.PanelSprite;
                tooltipImage.type = Image.Type.Sliced;
                tooltipImage.color = new Color(0.05f, 0.05f, 0.08f, 0.95f);

                Text tooltipName = AddText(tooltipGo, "Name", new Vector2(-110f, -6f),
                    new Vector2(220f, 10f), TextAnchor.UpperLeft, 9);
                tooltipName.text = "Name";
                tooltipName.color = new Color(0.95f, 0.85f, 0.5f);
                Text tooltipBody = AddText(tooltipGo, "Body", new Vector2(-110f, -17f),
                    new Vector2(220f, 10f), TextAnchor.UpperLeft, 8);
                tooltipBody.text = "description";

                var view = root.AddComponent<Presentation.Common.InventoryWindow>();
                var so = new SerializedObject(view);
                so.FindProperty("tabRow")!.objectReferenceValue = tabRowRect;
                so.FindProperty("tabTemplate")!.objectReferenceValue =
                    (RectTransform)tabTemplate.transform;
                so.FindProperty("grid")!.objectReferenceValue = gridRect;
                so.FindProperty("slotTemplate")!.objectReferenceValue =
                    (RectTransform)slotTemplate.transform;
                so.FindProperty("tooltipPanel")!.objectReferenceValue = tooltipGo;
                so.FindProperty("tooltipName")!.objectReferenceValue = tooltipName;
                so.FindProperty("tooltipBody")!.objectReferenceValue = tooltipBody;
                so.ApplyModifiedPropertiesWithoutUndo();

                SavePrefab(root, "InventoryWindow");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // ------------------------------------------------------------------
        // ConfirmDialog — modal, returns bool
        // ------------------------------------------------------------------

        private static void CreateConfirmDialog(SpriteRegistryAsset registry)
        {
            GameObject root = new("ConfirmDialog");
            try
            {
                var rect = root.AddComponent<RectTransform>();
                rect.sizeDelta = new Vector2(180f, 90f);

                var backdrop = root.AddComponent<Image>();
                backdrop.sprite = registry.PanelSprite;
                backdrop.type = Image.Type.Sliced;
                backdrop.color = new Color(0.15f, 0.12f, 0.10f, 0.98f);

                Text title = AddText(root, "Title", new Vector2(0f, 30f),
                    new Vector2(160f, 14f), TextAnchor.MiddleCenter, 11);
                title.text = "Confirm";
                title.color = new Color(0.95f, 0.85f, 0.5f);

                Text body = AddText(root, "Body", new Vector2(0f, 8f),
                    new Vector2(164f, 28f), TextAnchor.MiddleCenter, 9);
                body.text = "Are you sure?";

                Button confirm = AddButton(root, "ConfirmButton", new Vector2(-45f, -28f),
                    registry, new Color(0.30f, 0.55f, 0.30f));
                Text confirmLabel = confirm.GetComponentInChildren<Text>();
                confirmLabel.text = "OK";

                Button cancel = AddButton(root, "CancelButton", new Vector2(45f, -28f),
                    registry, new Color(0.55f, 0.30f, 0.28f));
                Text cancelLabel = cancel.GetComponentInChildren<Text>();
                cancelLabel.text = "Cancel";

                var view = root.AddComponent<Presentation.Common.ConfirmDialog>();
                var so = new SerializedObject(view);
                so.FindProperty("titleLabel")!.objectReferenceValue = title;
                so.FindProperty("bodyLabel")!.objectReferenceValue = body;
                so.FindProperty("confirmButton")!.objectReferenceValue = confirm;
                so.FindProperty("cancelButton")!.objectReferenceValue = cancel;
                so.FindProperty("confirmLabel")!.objectReferenceValue = confirmLabel;
                so.FindProperty("cancelLabel")!.objectReferenceValue = cancelLabel;
                so.ApplyModifiedPropertiesWithoutUndo();

                SavePrefab(root, "ConfirmDialog");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // ------------------------------------------------------------------
        // Shared uGUI helpers (AddText/AddButton; Stage 8 PrefabGenerator has
        // its own — these carry size/anchor params the windows need)
        // ------------------------------------------------------------------

        private static Text AddText(
            GameObject parent, string name, Vector2 position, Vector2 size,
            TextAnchor anchor, int fontSize)
        {
            GameObject go = new(name);
            go.transform.SetParent(parent.transform, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            Text text = go.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.alignment = anchor;
            text.color = Color.white;
            text.raycastTarget = false;
            return text;
        }

        private static Button AddButton(
            GameObject parent, string name, Vector2 position,
            SpriteRegistryAsset registry, Color color)
        {
            GameObject go = new(name);
            go.transform.SetParent(parent.transform, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(56f, 16f);
            var image = go.AddComponent<Image>();
            image.sprite = registry.WhiteSquareSprite;
            image.color = color;
            Button button = go.AddComponent<Button>();
            Text label = AddText(go, "Label", Vector2.zero, new Vector2(56f, 14f),
                TextAnchor.MiddleCenter, 9);
            return button;
        }

        // ------------------------------------------------------------------
        // Asset plumbing (same contract as PrefabGenerator)
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
                Debug.LogError($"[ui-prefabs] failed to save {path}");
            }
        }

        private static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder(EditorPaths.PrefabRoot))
            {
                PlaceholderSpriteGenerator.EnsureFolders();
                Directory.CreateDirectory(EditorPaths.PrefabRoot);
                AssetDatabase.Refresh();
            }

            if (!AssetDatabase.IsValidFolder(Root))
            {
                AssetDatabase.CreateFolder(EditorPaths.PrefabRoot, "UI");
            }

            if (!AssetDatabase.IsValidFolder(EditorPaths.ResourcesRoot))
            {
                AssetDatabase.CreateFolder("Assets/Main", "Resources");
            }

            if (!AssetDatabase.IsValidFolder(EditorPaths.LocalizationRoot))
            {
                AssetDatabase.CreateFolder("Assets/Main/Resources", "Localization");
            }
        }
    }
}
