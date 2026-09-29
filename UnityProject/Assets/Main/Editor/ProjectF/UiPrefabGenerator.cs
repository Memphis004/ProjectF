using System.Collections.Generic;
using System.IO;
using System.Linq;
using ProjectF.Infrastructure;
using ProjectF.Presentation.Common;
using UnityEditor;
using UnityEngine;
using TMPro;

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
    /// NOTE: all text is TextMeshProUGUI with the Sarabun dynamic SDF font
    /// asset (Thai-capable) — created by TmpFontGenerator, wired as the TMP
    /// default; the generator only needs the standard TMP components.
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
                "UI_SYNC_PROGRESS",
                "UI_CONFIRM", "UI_CANCEL", "UI_INVENTORY",
                "UI_TAB_ALL", "UI_TAB_BAIT", "UI_TAB_ROD", "UI_TAB_FISH",
                "UI_TAB_SEED", "UI_TAB_CROP", "UI_TAB_MATERIAL", "UI_TAB_FOOD",
                "UI_TOOLTIP_PRICE", "UI_TOOLTIP_COUNT", "UI_EMPTY_INVENTORY",
                "TOAST_CHAIN_OFFLINE", "TOAST_PRESENCE_OFFLINE", "TOAST_ACTION_PENDING",
                // Stage 11: stalled chain + optimistic rollback toasts.
                "TOAST_CHAIN_STALLED", "ROLLBACK_TOAST", "ROLLBACK_RECONCILED",
                // Stage 10 interaction + error mapping + shop/task/kitchen chrome.
                "ERR_NOT_ENOUGH_STAMINA", "ERR_NOT_ENOUGH_GOLD", "ERR_ITEM_NOT_FOUND",
                "ERR_POND_FULL", "ERR_PERMISSION_DENIED", "ERR_STATE_CORRUPT",
                "ERR_TIMEOUT", "ERR_CANCELLED", "ERR_UNKNOWN",
                "UI_INTERACT_KEY", "SHOP_PROMPT", "TASK_PROMPT", "KITCHEN_PROMPT",
                "SHOP_BUY", "SHOP_SELL", "SHOP_TOTAL", "SHOP_LOCK", "SHOP_BOUGHT", "SHOP_SOLD",
                "TASK_DELIVER", "TASK_DONE", "TASK_REWARD", "TASK_SUBMIT", "TASK_REROLL",
                "TASK_REWARD_TITLE", "TASK_REWARD_GOLD", "TASK_REWARD_EXP",
                "KITCHEN_LOCKED", "KITCHEN_PICK_RECIPE", "KITCHEN_DETAIL", "KITCHEN_CRAFT",
                "KITCHEN_RESULT_NORMAL", "KITCHEN_RESULT_GREAT", "EAT_DONE",
                "KITCHEN_UNLOCK", "KITCHEN_UNLOCKED",
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
            CreateShopWindow(registry);
            CreateTaskBoardWindow(registry);
            CreateCraftWindow(registry);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[ui-prefabs] UIRoot, Toast, InventoryWindow, ConfirmDialog, ShopWindow, TaskBoardWindow, CraftWindow + SpriteRegistry generated.");
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

                TMP_Text title = AddText(overlayGo, "Title", Vector2.zero,
                    new Vector2(300f, 20f), TextAnchor.MiddleCenter, 12);
                title.text = "Loading";
                TMP_Text progress = AddText(overlayGo, "Progress", new Vector2(0f, -24f),
                    new Vector2(300f, 16f), TextAnchor.MiddleCenter, 10);
                progress.text = "syncing block 0";

                var overlay = overlayGo.AddComponent<Infrastructure.UI.LoadingOverlay>();
                var so = new SerializedObject(overlay);
                so.FindProperty("dim")!.objectReferenceValue = overlayImage;
                so.FindProperty("titleLabel")!.objectReferenceValue = title;
                so.FindProperty("progressLabel")!.objectReferenceValue = progress;
                so.ApplyModifiedPropertiesWithoutUndo();
                overlayGo.AddComponent<Presentation.Common.LoadingOverlayBinder>();

                // --- sprite source + input driver + interaction prompt ---
                var source = root.AddComponent<SpriteRegistrySource>();
                var soSrc = new SerializedObject(source);
                soSrc.FindProperty("whiteSquare")!.objectReferenceValue =
                    AssetDatabase.LoadAssetAtPath<Sprite>(EditorPaths.UiRoot + "/WhiteSquare.png");
                soSrc.ApplyModifiedPropertiesWithoutUndo();
                root.AddComponent<Presentation.Common.UiInputDriver>();

                // Stage 10: interaction prompt (one shared label, World layer,
                // driven by InteractionPromptDriver next to UiInputDriver).
                GameObject promptGo = new("InteractionPrompt");
                promptGo.transform.SetParent(layerMap["World"], false);
                var promptRect = promptGo.AddComponent<RectTransform>();
                promptRect.sizeDelta = new Vector2(120f, 12f);
                var promptImage = promptGo.AddComponent<Image>();
                promptImage.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(
                    EditorPaths.UiRoot + "/WhiteSquare.png");
                promptImage.color = new Color(0f, 0f, 0f, 0.75f);
                TMP_Text promptText = AddText(promptGo, "Label", Vector2.zero,
                    new Vector2(116f, 10f), TextAnchor.MiddleCenter, 8);
                promptText.text = "[E] Talk";
                promptGo.AddComponent<Infrastructure.Interaction.InteractionPromptView>();
                var promptSo = new SerializedObject(
                    promptGo.GetComponent<Infrastructure.Interaction.InteractionPromptView>());
                promptSo.FindProperty("root")!.objectReferenceValue = promptRect;
                promptSo.FindProperty("label")!.objectReferenceValue = promptText;
                promptSo.ApplyModifiedPropertiesWithoutUndo();
                // Driver lives on the ALWAYS-ACTIVE root, not on promptGo:
                // the driver deactivates the prompt GO whenever no interactable
                // is in range — a driver on that GO would switch itself off on
                // frame one and never recover (found by the E2E T1 test).
                var promptDriver =
                    root.AddComponent<Infrastructure.Interaction.InteractionPromptDriver>();
                var driverSo = new SerializedObject(promptDriver);
                driverSo.FindProperty("view")!.objectReferenceValue =
                    promptGo.GetComponent<Infrastructure.Interaction.InteractionPromptView>();
                driverSo.ApplyModifiedPropertiesWithoutUndo();
                promptGo.SetActive(false); // shown on demand by the driver

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
                TMP_Text glyph = AddText(iconGo, "Glyph", Vector2.zero, new Vector2(12f, 12f),
                    TextAnchor.MiddleCenter, 8);
                glyph.text = "i";
                glyph.transform.SetAsLastSibling();

                GameObject labelGo = new("Message");
                labelGo.transform.SetParent(row.transform, false);
                var labelRect = labelGo.AddComponent<RectTransform>();
                labelRect.sizeDelta = new Vector2(128f, 12f);
                TMP_Text message = labelGo.AddComponent<TextMeshProUGUI>();
                message.font = TmpFontGenerator.Generate();
                message.fontSize = 14; // legacy 9 * 1.55
                message.alignment = TextAnchorMapper.Map(TextAnchor.MiddleLeft);
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

                TMP_Text title = AddText(root, "Title", new Vector2(0f, 66f),
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
                TMP_Text tabText = AddText(tabTemplate, "Label", Vector2.zero,
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
                TMP_Text count = countGo.AddComponent<TextMeshProUGUI>();
                count.font = TmpFontGenerator.Generate();
                count.fontSize = 11; // legacy 7 * 1.55
                count.alignment = TextAnchorMapper.Map(TextAnchor.LowerRight);
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

                TMP_Text tooltipName = AddText(tooltipGo, "Name", new Vector2(-110f, -6f),
                    new Vector2(220f, 10f), TextAnchor.UpperLeft, 9);
                tooltipName.text = "Name";
                tooltipName.color = new Color(0.95f, 0.85f, 0.5f);
                TMP_Text tooltipBody = AddText(tooltipGo, "Body", new Vector2(-110f, -17f),
                    new Vector2(220f, 10f), TextAnchor.UpperLeft, 8);
                tooltipBody.text = "description";

                // Stage 10: EAT affordance (enabled for Food items only).
                GameObject eatGo = new("EatButton");
                eatGo.transform.SetParent(tooltipGo.transform, false);
                var eatRect = eatGo.AddComponent<RectTransform>();
                eatRect.anchorMin = new Vector2(1f, 0.5f);
                eatRect.anchorMax = new Vector2(1f, 0.5f);
                eatRect.pivot = new Vector2(1f, 0.5f);
                eatRect.anchoredPosition = new Vector2(-4f, 0f);
                eatRect.sizeDelta = new Vector2(30f, 12f);
                var eatImage = eatGo.AddComponent<Image>();
                eatImage.sprite = registry.WhiteSquareSprite;
                eatImage.color = new Color(0.30f, 0.55f, 0.30f);
                eatGo.AddComponent<Button>();
                TMP_Text eatLabel = AddText(eatGo, "Label", Vector2.zero,
                    new Vector2(30f, 12f), TextAnchor.MiddleCenter, 8);
                eatLabel.text = "Eat";

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

                TMP_Text title = AddText(root, "Title", new Vector2(0f, 30f),
                    new Vector2(160f, 14f), TextAnchor.MiddleCenter, 11);
                title.text = "Confirm";
                title.color = new Color(0.95f, 0.85f, 0.5f);

                TMP_Text body = AddText(root, "Body", new Vector2(0f, 8f),
                    new Vector2(164f, 28f), TextAnchor.MiddleCenter, 9);
                body.text = "Are you sure?";

                Button confirm = AddButton(root, "ConfirmButton", new Vector2(-45f, -28f),
                    registry, new Color(0.30f, 0.55f, 0.30f));
                TMP_Text confirmLabel = confirm.GetComponentInChildren<TMP_Text>();
                confirmLabel.text = "OK";

                Button cancel = AddButton(root, "CancelButton", new Vector2(45f, -28f),
                    registry, new Color(0.55f, 0.30f, 0.28f));
                TMP_Text cancelLabel = cancel.GetComponentInChildren<TMP_Text>();
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
        // Stage 10 windows — Shop / TaskBoard / Craft (same recipe as
        // InventoryWindow: panel + title + row template + footer controls)
        // ------------------------------------------------------------------

        private static void CreateShopWindow(SpriteRegistryAsset registry)
        {
            GameObject root = new("ShopWindow");
            try
            {
                var rect = root.AddComponent<RectTransform>();
                rect.sizeDelta = new Vector2(260f, 150f);

                var backdrop = root.AddComponent<Image>();
                backdrop.sprite = registry.PanelSprite;
                backdrop.type = Image.Type.Sliced;
                backdrop.color = new Color(0.13f, 0.11f, 0.09f, 0.96f);

                TMP_Text title = AddText(root, "Title", new Vector2(0f, 66f),
                    new Vector2(240f, 14f), TextAnchor.MiddleCenter, 11);
                title.text = "Shop";
                title.color = new Color(0.95f, 0.9f, 0.8f);

                AddWindowTabs(root, out RectTransform buyTab, out RectTransform sellTab);
                AddRowList(root, registry, RowKind.Shop, out RectTransform rowTemplate, out RectTransform rowList);

                // Footer: stepper + total + action button.
                Button minus = AddButton(root, "MinusButton", new Vector2(-100f, -58f),
                    registry, new Color(0.35f, 0.3f, 0.24f));
                minus.GetComponentInChildren<TMP_Text>().text = "-";
                TMP_Text qty = AddText(root, "Quantity", new Vector2(-70f, -58f),
                    new Vector2(30f, 14f), TextAnchor.MiddleCenter, 10);
                qty.text = "x1";
                Button plus = AddButton(root, "PlusButton", new Vector2(-40f, -58f),
                    registry, new Color(0.35f, 0.3f, 0.24f));
                plus.GetComponentInChildren<TMP_Text>().text = "+";
                TMP_Text total = AddText(root, "Total", new Vector2(40f, -58f),
                    new Vector2(90f, 14f), TextAnchor.MiddleLeft, 9);
                total.text = "Total: 0";
                Button action = AddButton(root, "ActionButton", new Vector2(100f, -58f),
                    registry, new Color(0.30f, 0.55f, 0.30f));
                action.GetComponentInChildren<TMP_Text>().text = "Buy";

                var view = root.AddComponent<Presentation.Shop.ShopWindow>();
                var so = new SerializedObject(view);
                so.FindProperty("buyTabButton")!.objectReferenceValue = buyTab;
                so.FindProperty("sellTabButton")!.objectReferenceValue = sellTab;
                so.FindProperty("rowTemplate")!.objectReferenceValue = rowTemplate;
                so.FindProperty("rowList")!.objectReferenceValue = rowList;
                so.FindProperty("totalLabel")!.objectReferenceValue = total;
                so.FindProperty("quantityLabel")!.objectReferenceValue = qty;
                so.FindProperty("minusButton")!.objectReferenceValue = minus;
                so.FindProperty("plusButton")!.objectReferenceValue = plus;
                so.FindProperty("actionButton")!.objectReferenceValue = action;
                so.FindProperty("actionLabel")!.objectReferenceValue =
                    action.GetComponentInChildren<TMP_Text>();
                so.ApplyModifiedPropertiesWithoutUndo();

                SavePrefab(root, "ShopWindow");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void CreateTaskBoardWindow(SpriteRegistryAsset registry)
        {
            GameObject root = new("TaskBoardWindow");
            try
            {
                var rect = root.AddComponent<RectTransform>();
                rect.sizeDelta = new Vector2(260f, 150f);

                var backdrop = root.AddComponent<Image>();
                backdrop.sprite = registry.PanelSprite;
                backdrop.type = Image.Type.Sliced;
                backdrop.color = new Color(0.13f, 0.11f, 0.09f, 0.96f);

                TMP_Text title = AddText(root, "Title", new Vector2(0f, 66f),
                    new Vector2(240f, 14f), TextAnchor.MiddleCenter, 11);
                title.text = "Task Board";
                title.color = new Color(0.95f, 0.9f, 0.8f);

                AddRowList(root, registry, RowKind.Task, out RectTransform rowTemplate, out RectTransform rowList);

                TMP_Text countdown = AddText(root, "Countdown", new Vector2(0f, -58f),
                    new Vector2(240f, 14f), TextAnchor.MiddleCenter, 9);
                countdown.text = "reroll in 600 blocks (~20 min)";

                var view = root.AddComponent<Presentation.Village.TaskBoardWindow>();
                var so = new SerializedObject(view);
                so.FindProperty("rowTemplate")!.objectReferenceValue = rowTemplate;
                so.FindProperty("rowList")!.objectReferenceValue = rowList;
                so.FindProperty("countdownLabel")!.objectReferenceValue = countdown;
                so.ApplyModifiedPropertiesWithoutUndo();

                SavePrefab(root, "TaskBoardWindow");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void CreateCraftWindow(SpriteRegistryAsset registry)
        {
            GameObject root = new("CraftWindow");
            try
            {
                var rect = root.AddComponent<RectTransform>();
                rect.sizeDelta = new Vector2(260f, 150f);

                var backdrop = root.AddComponent<Image>();
                backdrop.sprite = registry.PanelSprite;
                backdrop.type = Image.Type.Sliced;
                backdrop.color = new Color(0.13f, 0.11f, 0.09f, 0.96f);

                TMP_Text title = AddText(root, "Title", new Vector2(0f, 66f),
                    new Vector2(240f, 14f), TextAnchor.MiddleCenter, 11);
                title.text = "Kitchen";
                title.color = new Color(0.95f, 0.9f, 0.8f);

                AddRowList(root, registry, RowKind.Craft, out RectTransform rowTemplate, out RectTransform rowList);

                TMP_Text detail = AddText(root, "Detail", new Vector2(-40f, -58f),
                    new Vector2(150f, 14f), TextAnchor.MiddleLeft, 8);
                detail.text = "stamina 0 · great 5%";

                Button minus = AddButton(root, "MinusButton", new Vector2(-100f, -58f),
                    registry, new Color(0.35f, 0.3f, 0.24f));
                minus.GetComponentInChildren<TMP_Text>().text = "-";
                TMP_Text qty = AddText(root, "Portions", new Vector2(-70f, -58f),
                    new Vector2(30f, 14f), TextAnchor.MiddleCenter, 10);
                qty.text = "x1";
                Button plus = AddButton(root, "PlusButton", new Vector2(-40f, -58f),
                    registry, new Color(0.35f, 0.3f, 0.24f));
                plus.GetComponentInChildren<TMP_Text>().text = "+";
                Button craft = AddButton(root, "CraftButton", new Vector2(100f, -58f),
                    registry, new Color(0.30f, 0.55f, 0.30f));
                craft.GetComponentInChildren<TMP_Text>().text = "Cook";

                // Locked overlay: covers everything, shows "Talk to Auntie first".
                GameObject locked = new("LockedOverlay");
                locked.transform.SetParent(root.transform, false);
                var lockedRect = locked.AddComponent<RectTransform>();
                lockedRect.anchorMin = Vector2.zero;
                lockedRect.anchorMax = Vector2.one;
                lockedRect.offsetMin = Vector2.zero;
                lockedRect.offsetMax = Vector2.zero;
                var lockedImage = locked.AddComponent<Image>();
                lockedImage.sprite = registry.WhiteSquareSprite;
                lockedImage.color = new Color(0f, 0f, 0f, 0.72f);
                TMP_Text lockedText = AddText(locked, "Text", Vector2.zero,
                    new Vector2(240f, 30f), TextAnchor.MiddleCenter, 10);
                lockedText.text = "Locked";

                // Stage 10.5: unlock CTA on the overlay itself — submit
                // unlock_kitchen_v1 (flat gold fee) straight from the locked
                // state; the StateWatcher re-entry clears the overlay on
                // confirm.
                Button unlock = AddButton(locked, "UnlockButton", new Vector2(0f, -28f),
                    registry, new Color(0.55f, 0.45f, 0.20f));
                unlock.GetComponentInChildren<TMP_Text>().text = "Unlock kitchen";
                locked.SetActive(false);

                var view = root.AddComponent<Presentation.AuntieHouse.CraftWindow>();
                var so = new SerializedObject(view);
                so.FindProperty("rowTemplate")!.objectReferenceValue = rowTemplate;
                so.FindProperty("rowList")!.objectReferenceValue = rowList;
                so.FindProperty("detailLabel")!.objectReferenceValue = detail;
                so.FindProperty("portionsLabel")!.objectReferenceValue = qty;
                so.FindProperty("minusButton")!.objectReferenceValue = minus;
                so.FindProperty("plusButton")!.objectReferenceValue = plus;
                so.FindProperty("craftButton")!.objectReferenceValue = craft;
                so.FindProperty("craftLabel")!.objectReferenceValue =
                    craft.GetComponentInChildren<TMP_Text>();
                so.FindProperty("lockedOverlay")!.objectReferenceValue = locked;
                so.FindProperty("unlockButton")!.objectReferenceValue = unlock;
                so.ApplyModifiedPropertiesWithoutUndo();

                SavePrefab(root, "CraftWindow");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>Shared Buy/Sell tab pair (ShopWindow).</summary>
        private static void AddWindowTabs(
            GameObject root, out RectTransform buyTab, out RectTransform sellTab)
        {
            buyTab = AddTabButton(root, "BuyTab", new Vector2(-60f, 48f), "Buy");
            sellTab = AddTabButton(root, "SellTab", new Vector2(-20f, 48f), "Sell");
        }

        private static RectTransform AddTabButton(
            GameObject parent, string name, Vector2 position, string label)
        {
            GameObject go = new(name);
            go.transform.SetParent(parent.transform, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(38f, 13f);
            var image = go.AddComponent<Image>();
            image.color = new Color(0.35f, 0.3f, 0.24f);
            go.AddComponent<Button>();
            AddText(go, "Label", Vector2.zero, new Vector2(38f, 12f),
                TextAnchor.MiddleCenter, 8).text = label;
            return rect;
        }

        /// <summary>Which children a row template carries.</summary>
        private enum RowKind { Shop, Task, Craft }

        /// <summary>Shared row list: inactive template child + layout list.
        /// Children differ per window kind — Shop: Icon/Name/Stock/Price/Lock;
        /// Task: Title/Progress/Reward/Submit; Craft: Title/Materials.</summary>
        private static void AddRowList(
            GameObject root, SpriteRegistryAsset registry, RowKind kind,
            out RectTransform rowTemplate, out RectTransform rowList)
        {
            GameObject listGo = new("RowList");
            listGo.transform.SetParent(root.transform, false);
            var listRect = listGo.AddComponent<RectTransform>();
            listRect.anchorMin = new Vector2(0.5f, 0.5f);
            listRect.anchorMax = new Vector2(0.5f, 0.5f);
            listRect.anchoredPosition = new Vector2(0f, 4f);
            listRect.sizeDelta = new Vector2(240f, 88f);
            var layout = listGo.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 2f;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            GameObject template = new("RowTemplate");
            template.transform.SetParent(listGo.transform, false);
            var templateRect = template.AddComponent<RectTransform>();
            templateRect.sizeDelta = new Vector2(238f, 20f);
            var templateImage = template.AddComponent<Image>();
            templateImage.color = new Color(0.22f, 0.2f, 0.18f);
            template.AddComponent<Button>();

            if (kind == RowKind.Shop)
            {
                GameObject iconGo = new("Icon");
                iconGo.transform.SetParent(template.transform, false);
                var iconRect = iconGo.AddComponent<RectTransform>();
                iconRect.anchorMin = new Vector2(0f, 0.5f);
                iconRect.anchorMax = new Vector2(0f, 0.5f);
                iconRect.pivot = new Vector2(0f, 0.5f);
                iconRect.anchoredPosition = new Vector2(2f, 0f);
                iconRect.sizeDelta = new Vector2(14f, 14f);
                iconGo.AddComponent<Image>();

                TMP_Text name = AddText(template, "Name", new Vector2(10f, 0f),
                    new Vector2(104f, 16f), TextAnchor.MiddleLeft, 8);
                name.text = "Item";
                TMP_Text stock = AddText(template, "Stock", new Vector2(118f, 0f),
                    new Vector2(28f, 16f), TextAnchor.MiddleLeft, 8);
                stock.text = "∞";
                TMP_Text price = AddText(template, "Price", new Vector2(150f, 0f),
                    new Vector2(30f, 16f), TextAnchor.MiddleRight, 8);
                price.text = "0";
                TMP_Text lockLabel = AddText(template, "Lock", new Vector2(184f, 0f),
                    new Vector2(50f, 16f), TextAnchor.MiddleRight, 8);
                lockLabel.text = "L3";
                lockLabel.color = new Color(0.95f, 0.7f, 0.25f);
            }
            else if (kind == RowKind.Task)
            {
                TMP_Text taskTitle = AddText(template, "Title", new Vector2(4f, 2f),
                    new Vector2(150f, 12f), TextAnchor.MiddleLeft, 8);
                taskTitle.text = "Task";
                TMP_Text progress = AddText(template, "Progress", new Vector2(160f, 2f),
                    new Vector2(40f, 12f), TextAnchor.MiddleRight, 8);
                progress.text = "0/3";
                TMP_Text reward = AddText(template, "Reward", new Vector2(4f, -6f),
                    new Vector2(190f, 10f), TextAnchor.MiddleLeft, 7);
                reward.text = "reward";
                reward.color = new Color(0.7f, 0.9f, 0.55f);
                Button submit = AddButton(template, "Submit", new Vector2(196f, 0f),
                    registry, new Color(0.30f, 0.55f, 0.30f));
                submit.GetComponent<RectTransform>().sizeDelta = new Vector2(36f, 12f);
                submit.GetComponentInChildren<TMP_Text>().fontSize = 11; // legacy 7 * 1.55
                submit.GetComponentInChildren<TMP_Text>().text = "Send";
            }
            else
            {
                TMP_Text craftTitle = AddText(template, "Title", new Vector2(4f, 2f),
                    new Vector2(200f, 12f), TextAnchor.MiddleLeft, 8);
                craftTitle.text = "Recipe";
                TMP_Text materials = AddText(template, "Materials", new Vector2(4f, -6f),
                    new Vector2(220f, 10f), TextAnchor.MiddleLeft, 7);
                materials.text = "materials";
                materials.color = new Color(0.75f, 0.75f, 0.75f);
            }

            template.SetActive(false);
            rowTemplate = templateRect;
            rowList = listRect;
        }

        // ------------------------------------------------------------------
        // Shared uGUI helpers (AddText/AddButton; Stage 8 PrefabGenerator has
        // its own — these carry size/anchor params the windows need)
        // ------------------------------------------------------------------

        private static TMP_Text AddText(
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
            TMP_Text text = go.AddComponent<TextMeshProUGUI>();
            text.font = TmpFontGenerator.Generate();
            // TMP point sizes ≈ 1.55x legacy font sizes at the same rect.
            text.fontSize = Mathf.RoundToInt(fontSize * 1.55f);
            text.alignment = TextAnchorMapper.Map(anchor);
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
            TMP_Text label = AddText(go, "Label", Vector2.zero, new Vector2(56f, 14f),
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
