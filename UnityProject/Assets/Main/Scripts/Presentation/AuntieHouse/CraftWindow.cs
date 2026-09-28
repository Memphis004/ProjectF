using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using ProjectF.Infrastructure;
using ProjectF.Infrastructure.Blockchain;
using ProjectF.Infrastructure.DataTables;
using ProjectF.Infrastructure.UI;
using ProjectF.Lib.Actions;
using ProjectF.Presentation.Common;
using UnityEngine;
using UnityEngine.UI;
using GeneratedTables = ProjectF.Tables.Tables;

// ReSharper disable CheckNamespace
namespace ProjectF.Presentation.AuntieHouse
{
    /// <summary>CraftWindow VIEW — zero logic (rows cloned by presenter).</summary>
    public sealed class CraftWindow : UIWindow<EmptyWindowParam, NoWindowResult>
    {
        [SerializeField]
        private RectTransform rowTemplate = default!;

        [SerializeField]
        private RectTransform rowList = default!;

        [SerializeField]
        private Text detailLabel = default!;

        [SerializeField]
        private Text portionsLabel = default!;

        [SerializeField]
        private Button minusButton = default!;

        [SerializeField]
        private Button plusButton = default!;

        [SerializeField]
        private Button craftButton = default!;

        [SerializeField]
        private Text craftLabel = default!;

        [SerializeField]
        private GameObject lockedOverlay = default!;

        public RectTransform RowTemplate => rowTemplate;
        public RectTransform RowList => rowList;
        public Text DetailLabel => detailLabel;
        public Text PortionsLabel => portionsLabel;
        public Button MinusButton => minusButton;
        public Button PlusButton => plusButton;
        public Button CraftButton => craftButton;
        public Text CraftLabel => craftLabel;
        public GameObject LockedOverlay => lockedOverlay;

        public override bool IsModal => false;

        public override UniTask OnOpenAsync(CancellationToken ct) => UniTask.CompletedTask;

        public override UniTask OnCloseAsync() => UniTask.CompletedTask;
    }

    /// <summary>Pure Stage-10 kitchen math — EditMode-testable.</summary>
    public static class CraftLogic
    {
        public const int MinPortions = 1;
        public const int MaxPortions = 10;

        /// <summary>Clamps portions to [1, 10] — mirrors the chain's guard
        /// (CraftFoodAction throws outside that range; the UI never submits
        /// an invalid value).</summary>
        public static int ClampPortions(int value) => Mathf.Clamp(value, MinPortions, MaxPortions);

        /// <summary>Total stamina cost for the portion count.</summary>
        public static long StaminaCost(int staminaCostPerPortion, int portions) =>
            (long)staminaCostPerPortion * portions;

        /// <summary>"Great dish" chance, min(40, 5 + cookingLevel*2)% — the
        /// exact chain formula (CraftFoodAction).</summary>
        public static int GreatChance(int cookingLevel) => Math.Min(40, 5 + cookingLevel * 2);

        /// <summary>Whether every material is held in the required count ×
        /// portions (per-recipe checklist from TbRecipeMaterial).</summary>
        public static bool HasMaterials(
            IReadOnlyDictionary<int, long> inventory,
            IEnumerable<(int ItemId, int Count)> materials,
            int portions)
        {
            foreach ((int itemId, int count) in materials)
            {
                if (!inventory.TryGetValue(itemId, out long held) || held < (long)count * portions)
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>
    /// Kitchen PRESENTER (spec 10.4): recipe list from TbRecipe, per-recipe
    /// material checklist from TbRecipeMaterial with owned/required counts,
    /// portions stepper 1-10, total stamina cost, great-dish chance from the
    /// cooking level. Locked state (greyed + "Talk to Auntie first") when
    /// KitchenUnlocked is false. Craft → CraftFoodAction; the result popup
    /// distinguishes normal vs great dishes by diffing the inventory
    /// before/after (the client never rolls outcomes).
    /// </summary>
    public sealed class KitchenPresenter : IDisposable
    {
        private readonly ActionQueue actions;
        private readonly StateWatcher state;
        private readonly UnityTableService tables;
        private readonly LocalizationService loc;
        private readonly IWindowService windows;
        private readonly IToastService toasts;

        private CraftWindow? view;
        private StateWatcher? boundWatcher;
        private int selectedRecipeId;
        private int portions = 1;
        private bool isCrafting;

        public KitchenPresenter(
            ActionQueue actionQueue,
            StateWatcher stateWatcher,
            UnityTableService tables,
            LocalizationService loc,
            IWindowService windowService,
            IToastService toasts)
        {
            actions = actionQueue;
            state = stateWatcher;
            this.tables = tables;
            this.loc = loc;
            windows = windowService;
            this.toasts = toasts;
        }

        public void Attach(CraftWindow window)
        {
            if (ReferenceEquals(view, window))
            {
                return;
            }

            Detach();
            view = window;
            boundWatcher = state;
            boundWatcher.AvatarUpdated += OnAvatarUpdated;

            window.MinusButton.onClick.AddListener(() =>
            {
                portions = CraftLogic.ClampPortions(portions - 1);
                Rebuild();
            });
            window.PlusButton.onClick.AddListener(() =>
            {
                portions = CraftLogic.ClampPortions(portions + 1);
                Rebuild();
            });
            window.CraftButton.onClick.AddListener(() => CraftAsync().Forget());

            // First paint (see ShopPresenter/TaskBoardPresenter — the pooled
            // window needs an initial rebuild at acquisition).
            Rebuild();
        }

        public void Detach()
        {
            if (boundWatcher is { })
            {
                boundWatcher.AvatarUpdated -= OnAvatarUpdated;
                boundWatcher = null;
            }

            view = null;
        }

        private void OnAvatarUpdated(AvatarSnapshot snapshot)
        {
            if (view is { IsOpen: true })
            {
                Rebuild();
            }
        }

        private List<(int ItemId, int Count)> MaterialsFor(GeneratedTables table, int recipeId)
        {
            var materials = new List<(int, int)>();
            foreach (ProjectF.Tables.RecipeMaterial material in table.TbRecipeMaterial.DataList)
            {
                if (material.RecipeId == recipeId)
                {
                    materials.Add((material.ItemId, material.Count));
                }
            }

            return materials;
        }

        public void Rebuild()
        {
            if (view is null || !tables.IsLoaded)
            {
                return;
            }

            GeneratedTables table = tables.Tables;
            AvatarSnapshot? snap = state.Current;
            bool unlocked = snap?.KitchenUnlocked ?? false;

            view.LockedOverlay.SetActive(!unlocked);
            if (!unlocked)
            {
                // Spec: greyed out with "Talk to Auntie first".
                view.DetailLabel.text = loc.Get("KITCHEN_LOCKED");
                view.CraftButton.interactable = false;
                ClearRows();
                return;
            }

            ClearRows();
            foreach (ProjectF.Tables.Recipe recipe in table.TbRecipe.DataList)
            {
                bool levelOk = (snap?.CookingLevel ?? 1) >= recipe.RequiredLevel;

                RectTransform row = UnityEngine.Object.Instantiate(view.RowTemplate, view.RowList);
                row.gameObject.SetActive(true);

                // Material checklist with owned/required counts.
                var checklist = new List<string>();
                foreach ((int itemId, int count) in MaterialsFor(table, recipe.Id))
                {
                    long owned = snap?.GetItemCount(itemId) ?? 0;
                    string name = loc.Get(table.TbItem.GetOrDefault(itemId)?.NameKey
                        ?? $"ITEM_{itemId}");
                    checklist.Add($"{name} {owned}/{count}");
                }

                Text title = row.Find("Title")!.GetComponent<Text>();
                title.text = loc.Get(recipe.NameKey);
                title.color = levelOk ? Color.white : new Color(0.55f, 0.55f, 0.55f);
                row.Find("Materials")!.GetComponent<Text>().text = string.Join(", ", checklist);

                BindRow(row, () =>
                {
                    selectedRecipeId = recipe.Id;
                    Rebuild();
                });

                if (selectedRecipeId == recipe.Id)
                {
                    Highlight(row);
                }
            }

            RefreshDetail(table, snap);
        }

        private void RefreshDetail(GeneratedTables table, AvatarSnapshot? snap)
        {
            if (view is null)
            {
                return;
            }

            view.PortionsLabel.text = $"x{portions}";

            if (selectedRecipeId == 0 || table.TbRecipe.GetOrDefault(selectedRecipeId) is not { } recipe)
            {
                view.DetailLabel.text = loc.Get("KITCHEN_PICK_RECIPE");
                view.CraftButton.interactable = false;
                return;
            }

            int great = CraftLogic.GreatChance(snap?.CookingLevel ?? 1);
            long cost = CraftLogic.StaminaCost(recipe.StaminaCost, portions);
            bool materialsOk = snap is { }
                && CraftLogic.HasMaterials(snap.Inventory, MaterialsFor(table, recipe.Id), portions);
            bool staminaOk = (snap?.Stamina ?? 0) >= cost;
            bool levelOk = (snap?.CookingLevel ?? 1) >= recipe.RequiredLevel;

            view.DetailLabel.text = loc.Get("KITCHEN_DETAIL")
                .Replace("{0}", cost.ToString())
                .Replace("{1}", great.ToString());

            view.CraftLabel.text = loc.Get("KITCHEN_CRAFT");
            view.CraftButton.interactable = materialsOk && staminaOk && levelOk && !isCrafting;
        }

        private async UniTaskVoid CraftAsync()
        {
            if (view is null || isCrafting || selectedRecipeId == 0)
            {
                return;
            }

            isCrafting = true;
            GeneratedTables table = tables.Tables;
            AvatarSnapshot? before = state.Current;
            try
            {
                IDisposable pending = toasts.ShowPending(loc.Get("TOAST_ACTION_PENDING"));
                bool ok;
                try
                {
                    ok = await actions.SubmitAsync(new CraftFoodAction(selectedRecipeId, portions));
                }
                finally
                {
                    pending.Dispose();
                }

                if (ok)
                {
                    await ShowResultPopupAsync(table, selectedRecipeId, before);
                }
                else
                {
                    toasts.Error(loc.Get("ERR_UNKNOWN"));
                }
            }
            finally
            {
                isCrafting = false;
                Rebuild();
            }
        }

        /// <summary>Result popup: diff inventory before/after — items the
        /// great variant added vs the normal one decide the message. The
        /// client NEVER rolls the outcome (knowledge.md rule 1).</summary>
        private async UniTask ShowResultPopupAsync(
            GeneratedTables table, int recipeId, AvatarSnapshot? before)
        {
            ProjectF.Tables.Recipe? recipe = table.TbRecipe.GetOrDefault(recipeId);
            AvatarSnapshot? after = state.Current;
            if (recipe is null || before is null || after is null)
            {
                return;
            }

            long normalDelta = after.GetItemCount(recipe.ResultItemId)
                - before.GetItemCount(recipe.ResultItemId);
            long greatDelta = after.GetItemCount(recipe.GreatResultItemId)
                - before.GetItemCount(recipe.GreatResultItemId);

            string title = greatDelta > 0
                ? loc.Get("KITCHEN_RESULT_GREAT")
                : loc.Get("KITCHEN_RESULT_NORMAL");
            var lines = new List<string>();
            if (normalDelta > 0)
            {
                lines.Add($"+{normalDelta} {loc.Get(table.TbItem.GetOrDefault(recipe.ResultItemId)?.NameKey ?? string.Empty)}");
            }

            if (greatDelta > 0)
            {
                lines.Add($"+{greatDelta} {loc.Get(table.TbItem.GetOrDefault(recipe.GreatResultItemId)?.NameKey ?? string.Empty)}");
            }

            await windows.OpenAsync<ConfirmDialog, ConfirmDialogParam, bool>(
                new ConfirmDialogParam(title, string.Join("\n", lines)),
                CancellationToken.None);
        }

        private void BindRow(RectTransform row, Action onSelect) =>
            row.GetComponent<Button>()?.onClick.AddListener(() => onSelect());

        private void Highlight(RectTransform row)
        {
            UnityEngine.UI.Image? bg = row.GetComponent<UnityEngine.UI.Image>();
            if (bg is { })
            {
                bg.color = new Color(0.32f, 0.30f, 0.20f);
            }
        }

        private void ClearRows()
        {
            for (int i = view!.RowList.childCount - 1; i >= 0; i--)
            {
                Transform child = view.RowList.GetChild(i);
                if (child.gameObject != view.RowTemplate.gameObject)
                {
                    UnityEngine.Object.Destroy(child.gameObject);
                }
            }
        }

        public void Dispose() => Detach();
    }
}
