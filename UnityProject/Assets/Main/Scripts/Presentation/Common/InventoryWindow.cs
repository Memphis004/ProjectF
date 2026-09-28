using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using ProjectF.Infrastructure.Blockchain;
using ProjectF.Infrastructure.DataTables;
using ProjectF.Infrastructure.UI;
using UnityEngine;
using UnityEngine.UI;
using GeneratedTables = ProjectF.Tables.Tables;
using ItemCategory = ProjectF.Tables.ItemCategory;

// ReSharper disable CheckNamespace
namespace ProjectF.Presentation.Common
{
    /// <summary>InventoryWindow VIEW — zero logic. A tab row, a scroll grid of
    /// item slots, and a tooltip panel. Slots and tabs are CLONED from the two
    /// template children by <see cref="InventoryPresenter"/> at open time.</summary>
    public sealed class InventoryWindow : UIWindow<EmptyWindowParam, NoWindowResult>
    {
        [SerializeField]
        private RectTransform tabRow = default!;

        [SerializeField]
        private RectTransform tabTemplate = default!;

        [SerializeField]
        private RectTransform grid = default!;

        [SerializeField]
        private RectTransform slotTemplate = default!;

        [SerializeField]
        private GameObject tooltipPanel = default!;

        [SerializeField]
        private Text tooltipName = default!;

        [SerializeField]
        private Text tooltipBody = default!;

        public RectTransform TabRow => tabRow;
        public RectTransform TabTemplate => tabTemplate;
        public RectTransform Grid => grid;
        public RectTransform SlotTemplate => slotTemplate;
        public GameObject TooltipPanel => tooltipPanel;
        public Text TooltipName => tooltipName;
        public Text TooltipBody => tooltipBody;

        public override bool IsModal => false;

        public override UniTask OnOpenAsync(CancellationToken ct) => UniTask.CompletedTask;

        public override UniTask OnCloseAsync()
        {
            TooltipPanel.SetActive(false);
            return UniTask.CompletedTask;
        }
    }

    /// <summary>
    /// Inventory PRESENTER (plain C#): grid of item slots built from the
    /// confirmed inventory (StateWatcher) joined with TbItem (spec 9.4),
    /// filter tabs by category, tooltip with name/description/base price.
    /// Opened with the I key (UiInputDriver) through IWindowService.
    /// </summary>
    public sealed class InventoryPresenter : IDisposable
    {
        private readonly StateWatcher state;
        private readonly UnityTableService tables;
        private readonly LocalizationService loc;
        private readonly SpriteRegistry sprites;

        private InventoryWindow? view;
        private StateWatcher? boundWatcher;
        private ItemCategory? filter;

        public InventoryPresenter(
            StateWatcher stateWatcher,
            UnityTableService tables,
            LocalizationService loc,
            SpriteRegistry sprites)
        {
            this.state = stateWatcher;
            this.tables = tables;
            this.loc = loc;
            this.sprites = sprites;
        }

        /// <summary>Called by WindowService right before OnOpenAsync of the
        /// freshly acquired instance (the window and presenter meet here —
        /// presenters stay constructor-injectable, the pooled window is not).</summary>
        public void Attach(InventoryWindow window)
        {
            if (ReferenceEquals(view, window))
            {
                return;
            }

            Detach();
            view = window;
            boundWatcher = state;
            boundWatcher.AvatarUpdated += OnAvatarUpdated;
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
                Rebuild(snapshot.Inventory);
            }
        }

        /// <summary>Rebuilds the grid from the CURRENT confirmed inventory
        /// (called on open + on every AvatarUpdated while open).</summary>
        public void Rebuild(IReadOnlyDictionary<int, long>? inventory = null)
        {
            if (view is null)
            {
                return;
            }

            EnsureTabs();
            ClearSlots();

            GeneratedTables? table = tables.IsLoaded ? tables.Tables : null;
            if (table is null)
            {
                return; // boot not finished — window opened pre-tables (rare)
            }

            inventory ??= state.Current?.Inventory;
            if (inventory is null)
            {
                return;
            }

            foreach (KeyValuePair<int, long> kv in inventory)
            {
                if (kv.Value <= 0)
                {
                    continue;
                }

                ProjectF.Tables.Item? item = table.TbItem.GetOrDefault(kv.Key);
                if (item is null || (filter is { } f && item.Category != f))
                {
                    continue;
                }

                CreateSlot(item, kv.Value);
            }
        }

        private void CreateSlot(ProjectF.Tables.Item item, long count)
        {
            RectTransform slot = UnityEngine.Object.Instantiate(view!.SlotTemplate, view.Grid);
            slot.gameObject.SetActive(true);

            Image icon = slot.Find("Icon")!.GetComponent<Image>();
            icon.sprite = sprites.GetItemIcon(item.Id);

            Text countLabel = slot.Find("Count")!.GetComponent<Text>();
            countLabel.text = FormatCount(count);

            Button button = slot.GetComponent<Button>();
            if (button is { })
            {
                int itemId = item.Id;
                button.onClick.AddListener(() => ShowTooltip(itemId, count));
            }
        }

        private void ShowTooltip(int itemId, long count)
        {
            if (view is null || tables.Tables.TbItem.GetOrDefault(itemId) is not { } item)
            {
                return;
            }

            view.TooltipPanel.SetActive(true);
            view.TooltipName.text = loc.Get(item.NameKey);
            view.TooltipBody.text =
                $"{loc.Get("UI_TOOLTIP_PRICE").Replace("{0}", item.BasePrice.ToString())}\n" +
                loc.Get("UI_TOOLTIP_COUNT").Replace("{0}", count.ToString());
        }

        private void EnsureTabs()
        {
            if (view!.TabRow.childCount > 1)
            {
                return; // tabs already built (pooled window keeps them)
            }

            CreateTab(null, "UI_TAB_ALL");
            foreach (ItemCategory category in (ItemCategory[])Enum.GetValues(typeof(ItemCategory)))
            {
                CreateTab(category, $"UI_TAB_{category.ToString().ToUpperInvariant()}");
            }
        }

        private void CreateTab(ItemCategory? category, string locKey)
        {
            RectTransform tab = UnityEngine.Object.Instantiate(view!.TabTemplate, view.TabRow);
            tab.gameObject.SetActive(true);
            Text label = tab.GetComponentInChildren<Text>();
            if (label is { })
            {
                label.text = loc.Get(locKey);
            }

            Button button = tab.GetComponent<Button>();
            if (button is { })
            {
                button.onClick.AddListener(() =>
                {
                    filter = category;
                    Rebuild();
                });
            }
        }

        private void ClearSlots()
        {
            for (int i = view!.Grid.childCount - 1; i >= 0; i--)
            {
                Transform child = view.Grid.GetChild(i);
                if (child.gameObject != view.SlotTemplate.gameObject)
                {
                    UnityEngine.Object.Destroy(child.gameObject);
                }
            }
        }

        private static string FormatCount(long count) =>
            count >= 1000 ? $"{count / 1000d:0.#}k" : count.ToString();

        public void Dispose() => Detach();
    }
}
