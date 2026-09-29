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
using ItemCategory = ProjectF.Tables.ItemCategory;

// ReSharper disable CheckNamespace
namespace ProjectF.Presentation.Shop
{
    /// <summary>ShopWindow VIEW — zero logic. Buy list + Sell list + footer
    /// (total, stepper, action button). Rows are CLONED from the template by
    /// the presenter.</summary>
    public sealed class ShopWindow : UIWindow<EmptyWindowParam, NoWindowResult>
    {
        [SerializeField]
        private RectTransform buyTabButton = default!;

        [SerializeField]
        private RectTransform sellTabButton = default!;

        [SerializeField]
        private RectTransform rowTemplate = default!;

        [SerializeField]
        private RectTransform rowList = default!;

        [SerializeField]
        private Text totalLabel = default!;

        [SerializeField]
        private Text quantityLabel = default!;

        [SerializeField]
        private Button minusButton = default!;

        [SerializeField]
        private Button plusButton = default!;

        [SerializeField]
        private Button actionButton = default!;

        [SerializeField]
        private Text actionLabel = default!;

        public RectTransform BuyTabButton => buyTabButton;
        public RectTransform SellTabButton => sellTabButton;
        public RectTransform RowTemplate => rowTemplate;
        public RectTransform RowList => rowList;
        public Text TotalLabel => totalLabel;
        public Text QuantityLabel => quantityLabel;
        public Button MinusButton => minusButton;
        public Button PlusButton => plusButton;
        public Button ActionButton => actionButton;
        public Text ActionLabel => actionLabel;

        public override bool IsModal => false;

        public override UniTask OnOpenAsync(CancellationToken ct) => UniTask.CompletedTask;

        public override UniTask OnCloseAsync() => UniTask.CompletedTask;
    }

    /// <summary>Pure Stage-10 shop math — EditMode-testable without a chain.
    /// Daily stock is a CLIENT display concern (the chain enforces price,
    /// level, gold — see BuyItemAction's doc).</summary>
    public static class ShopLogic
    {
        /// <summary>GDD 2.4: NPC buys at 60% of base_price (mirrors the chain).</summary>
        public const long SellRatePermille = 600;

        /// <summary>sell price preview — floor(basePrice * rate * qty).</summary>
        public static long SellGold(int basePrice, int quantity) =>
            (long)basePrice * quantity * SellRatePermille / 1000;

        /// <summary>Whether the buy row is affordable at the given quantity.</summary>
        public static bool Affordable(long gold, int price, int quantity) =>
            (long)price * quantity <= gold;

        /// <summary>Whether the level gate is open (either skill counts — the
        /// chain checks fishing OR cooking, mirrored here).</summary>
        public static bool LevelOk(int requiredLevel, int fishingLevel, int cookingLevel) =>
            fishingLevel >= requiredLevel || cookingLevel >= requiredLevel;

        /// <summary>Remaining daily stock for display (0 = unlimited — the
        /// shop.csv convention for bait/rods/seeds).</summary>
        public static string StockLabel(int dailyStock, long boughtToday)
        {
            if (dailyStock <= 0)
            {
                return "∞";
            }

            long left = Math.Max(0, dailyStock - boughtToday);
            return left.ToString();
        }

        /// <summary>Clamps the quantity stepper to [1, maxQty] (max 99 for
        /// UX; sell rows cap at the held count).</summary>
        public static int ClampQuantity(int value, int maxQty) =>
            Mathf.Clamp(value, 1, Mathf.Max(1, Math.Min(99, maxQty)));
    }

    /// <summary>
    /// Shop PRESENTER: buy list = TbShop ⋈ TbItem (price, daily stock,
    /// level lock icon), quantity stepper + running total, Buy disabled when
    /// gold is short. Buy → pending toast → BuyItemAction via ActionQueue →
    /// refresh on confirm / mapped error toast on failure. Sell tab sells
    /// Fish/Crop/Food at the 60% rate (sell_item_v1).
    /// </summary>
    public sealed class ShopPresenter : IDisposable
    {
        private readonly ActionQueue actions;
        private readonly StateWatcher state;
        private readonly UnityTableService tables;
        private readonly LocalizationService loc;
        private readonly SpriteRegistry sprites;
        private readonly IToastService toasts;

        private ShopWindow? view;
        private StateWatcher? boundWatcher;
        private bool sellTab;
        private int quantity = 1;
        private int selectedItemId;
        private bool isBuying; // guard against double-submit

        /// <summary>Maps a selected ITEM id to its SHOP ENTRY id — the chain's
        /// buy_item_v1 takes the shop entry id, not the item id (found by the
        /// chain E2E: every UI buy failed with "Shop entry 1001 is not in the
        /// game tables" because the item id was submitted as the entry id).</summary>
        private int ShopEntryIdFor(GeneratedTables table, int itemId)
        {
            foreach (ProjectF.Tables.ShopEntry entry in table.TbShop.DataList)
            {
                if (entry.ItemId == itemId)
                {
                    return entry.Id;
                }
            }

            return 0;
        }

        public ShopPresenter(
            ActionQueue actionQueue,
            StateWatcher stateWatcher,
            UnityTableService tables,
            LocalizationService loc,
            SpriteRegistry sprites,
            IToastService toasts)
        {
            actions = actionQueue;
            state = stateWatcher;
            this.tables = tables;
            this.loc = loc;
            this.sprites = sprites;
            this.toasts = toasts;
        }

        public void Attach(ShopWindow window)
        {
            if (ReferenceEquals(view, window))
            {
                return;
            }

            Detach();
            view = window;
            boundWatcher = state;
            boundWatcher.AvatarUpdated += OnAvatarUpdated;

            window.BuyTabButton.GetComponent<Button>()?.onClick.AddListener(() =>
            {
                sellTab = false;
                Rebuild();
            });
            window.SellTabButton.GetComponent<Button>()?.onClick.AddListener(() =>
            {
                sellTab = true;
                Rebuild();
            });
            window.MinusButton.onClick.AddListener(() =>
            {
                quantity = ShopLogic.ClampQuantity(quantity - 1, MaxQuantity());
                Rebuild();
            });
            window.PlusButton.onClick.AddListener(() =>
            {
                quantity = ShopLogic.ClampQuantity(quantity + 1, MaxQuantity());
                Rebuild();
            });
            window.ActionButton.onClick.AddListener(() => SubmitAsync().Forget());

            // First paint (Attach happens on first acquisition — the window
            // opens right after; later opens re-run OnAvatarUpdated only if
            // state moved, so an initial Rebuild is required here).
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

        private int MaxQuantity()
        {
            if (!sellTab)
            {
                return 99; // buy side — stock caps visually via the total
            }

            return selectedItemId != 0
                ? (int)Math.Min(int.MaxValue, state.Current?.GetItemCount(selectedItemId) ?? 0)
                : 1;
        }

        /// <summary>Rebuilds both the row list and the footer from the
        /// CURRENT confirmed snapshot.</summary>
        public void Rebuild()
        {
            if (view is null || !tables.IsLoaded)
            {
                return;
            }

            GeneratedTables table = tables.Tables;
            AvatarSnapshot? snap = state.Current;
            ClearRows();

            if (sellTab)
            {
                BuildSellRows(table, snap);
            }
            else
            {
                BuildBuyRows(table, snap);
            }

            RefreshFooter(table, snap);
        }

        private void BuildBuyRows(GeneratedTables table, AvatarSnapshot? snap)
        {
            foreach (ProjectF.Tables.ShopEntry entry in table.TbShop.DataList)
            {
                ProjectF.Tables.Item? item = table.TbItem.GetOrDefault(entry.ItemId);
                if (item is null)
                {
                    continue;
                }

                int fishing = snap?.FishingLevel ?? 1;
                int cooking = snap?.CookingLevel ?? 1;
                bool levelOk = ShopLogic.LevelOk(entry.RequiredLevel, fishing, cooking);

                RectTransform row = NewRow();
                SetRow(row, item, $"{item.BasePrice}", levelOk, entry.RequiredLevel);
                SetRowStock(row, ShopLogic.StockLabel(entry.DailyStock, 0));

                BindRow(row, () =>
                {
                    selectedItemId = item.Id;
                    quantity = ShopLogic.ClampQuantity(quantity, 99);
                    RefreshFooter(table, state.Current);
                });

                if (selectedItemId == item.Id)
                {
                    Highlight(row);
                }
            }
        }

        private void BuildSellRows(GeneratedTables table, AvatarSnapshot? snap)
        {
            if (snap is null)
            {
                return;
            }

            foreach (KeyValuePair<int, long> kv in snap.Inventory)
            {
                if (kv.Value <= 0
                    || table.TbItem.GetOrDefault(kv.Key) is not { } item
                    || (item.Category != ItemCategory.Fish
                        && item.Category != ItemCategory.Crop
                        && item.Category != ItemCategory.Food))
                {
                    continue;
                }

                long sellGold = ShopLogic.SellGold(item.BasePrice, 1);

                RectTransform row = NewRow();
                SetRow(row, item, sellGold.ToString(), levelOk: true, requiredLevel: 0);
                SetRowStock(row, $"x{kv.Value}");

                BindRow(row, () =>
                {
                    selectedItemId = item.Id;
                    quantity = ShopLogic.ClampQuantity(quantity, MaxQuantity());
                    RefreshFooter(table, state.Current);
                });

                if (selectedItemId == item.Id)
                {
                    Highlight(row);
                }
            }
        }

        private void RefreshFooter(GeneratedTables table, AvatarSnapshot? snap)
        {
            if (view is null)
            {
                return;
            }

            view.QuantityLabel.text = $"x{quantity}";
            ProjectF.Tables.Item? item = selectedItemId != 0
                ? table.TbItem.GetOrDefault(selectedItemId)
                : null;

            long total = 0;
            bool canAct = item is not null;
            string verb;

            if (sellTab)
            {
                verb = loc.Get("SHOP_SELL");
                total = item is { } ? ShopLogic.SellGold(item.BasePrice, quantity) : 0;
            }
            else
            {
                verb = loc.Get("SHOP_BUY");
                total = item is { } ? (long)item.BasePrice * quantity : 0;
                canAct = canAct && snap is { } && ShopLogic.Affordable(snap.Gold, item.BasePrice, quantity);
            }

            view.TotalLabel.text = $"{loc.Get("SHOP_TOTAL")}: {total}";
            view.ActionLabel.text = verb;
            view.ActionButton.interactable = canAct && !isBuying;
        }

        private async UniTaskVoid SubmitAsync()
        {
            if (view is null || isBuying || selectedItemId == 0 || !tables.IsLoaded)
            {
                return;
            }

            isBuying = true;
            try
            {
                IDisposable pending = toasts.ShowPending(loc.Get("TOAST_ACTION_PENDING"));
                try
                {
                    bool ok = sellTab
                        ? await actions.SubmitAsync(new SellItemAction(selectedItemId, quantity))
                        : await actions.SubmitAsync(new BuyItemAction(
                            ShopEntryIdFor(tables.Tables, selectedItemId), quantity));

                    if (ok)
                    {
                        // Refresh = StateWatcher fires AvatarUpdated on the new
                        // tip, which re-enters Rebuild via OnAvatarUpdated.
                        toasts.Success(loc.Get(sellTab ? "SHOP_SOLD" : "SHOP_BOUGHT"));
                    }
                    else
                    {
                        // Stage 11 carries structured reasons; Stage 10 maps
                        // what ActionQueue surfaced (raw reason → ErrorMapper
                        // when the failure came from on-chain validation).
                        toasts.Error(loc.Get("ERR_UNKNOWN"));
                    }
                }
                finally
                {
                    pending.Dispose();
                }
            }
            finally
            {
                isBuying = false;
                Rebuild();
            }
        }

        // ---- row plumbing (cloned from the template child) ---------------

        private RectTransform NewRow()
        {
            RectTransform row = UnityEngine.Object.Instantiate(view!.RowTemplate, view.RowList);
            row.gameObject.SetActive(true);
            return row;
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

        private void SetRow(
            RectTransform row, ProjectF.Tables.Item item, string price,
            bool levelOk, int requiredLevel)
        {
            Text name = row.Find("Name")!.GetComponent<Text>();
            name.text = loc.Get(item.NameKey);
            name.color = levelOk ? Color.white : new Color(0.55f, 0.55f, 0.55f);

            Text priceLabel = row.Find("Price")!.GetComponent<Text>();
            priceLabel.text = price;

            Text lockLabel = row.Find("Lock")!.GetComponent<Text>();
            lockLabel.gameObject.SetActive(!levelOk);
            if (!levelOk)
            {
                // Lock icon with the required level (spec 10.2).
                lockLabel.text = $"{loc.Get("SHOP_LOCK")}{requiredLevel}";
            }

            Image icon = row.Find("Icon")!.GetComponent<Image>();
            icon.sprite = sprites.GetItemIcon(item.Id);
        }

        private void SetRowStock(RectTransform row, string stock)
        {
            row.Find("Stock")!.GetComponent<Text>().text = stock;
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
