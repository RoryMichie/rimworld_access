using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The trade screen (<see cref="Dialog_Trade"/>) as a ScreenScope table: the table view.
    /// <see cref="TradeClassicScope"/> shows the same dialog as three flat lists and is the default;
    /// Ctrl+Tab swaps views on the open dialog (<see cref="TradeViewOpener"/>). Row operations both
    /// views share live in <see cref="TradeRowActions"/>.
    ///
    /// Structure matches vanilla's own: ONE flat list where every row carries both sides of the deal,
    /// presented as a table whose columns mirror TradeUI.DrawTradeableRow left to right, plus Mass and
    /// Market value — vanilla draws neither as a cell, but both are trade sorters, and the header row
    /// is where sorting lives. The currency row is pinned as the first data row, exactly as vanilla
    /// pins it above the list, and never participates in sorts.
    ///
    /// Rows come from the dialog's OWN cachedTradeables/cachedCurrencyTradeable — the game's decision
    /// objects verbatim, its will-not-trade filtering included — and the default sort order IS
    /// vanilla's CacheTradeables order. Count adjustment goes through Transferable.CanAdjustTo/AdjustTo,
    /// and direction is always derived from the game's own sign semantics rather than hardcoded,
    /// because gift mode flips PositiveCountDirection.
    ///
    /// Enter on a data row opens the shared quantity chooser over the SIGNED range
    /// [GetMinimumToTransfer, GetMaximumToTransfer] with buy/sell phrasing; +/- keep their ±1
    /// adjustment with the context-aware direction, so selling-primary items count up when selling.
    ///
    /// Caravan trades add a Summary stat-table region reflected off the dialog's own cached stat
    /// properties; Enter on a stat row opens the game's pre-computed explanation.
    ///
    /// The Toolbar region declares vanilla's real actions in vanilla's draw order, including the
    /// accept button whose label becomes "Offer gifts (+N)" in gift mode exactly as the real one does.
    ///
    /// Escape: TradeNavigationPatch's Window.OnCancelKeyPressed blocker eats vanilla's own close for
    /// the whole session (load-bearing — Dialog_Trade sets closeOnCancel), so this scope owns Cancel
    /// outright. TradeNavigationState survives as the lifecycle/session backend the Harmony patches
    /// and MapNavigationState read.
    /// </summary>
    public sealed class TradeScope : ScreenScope
    {
        private const int TradesRegion = 0;
        private const int SummaryRegion = 1;

        // Column indices in vanilla's visual left-to-right reading order (DrawTradeableRow itself
        // draws right-to-left), plus the two sorter columns.
        private const int IdentityColumn = 0;
        private const int ColonyCountColumn = 1;
        private const int SellPriceColumn = 2;
        private const int TradeAmountColumn = 3;
        private const int BuyPriceColumn = 4;
        private const int TraderCountColumn = 5;
        private const int MassColumn = 6;
        private const int MarketValueColumn = 7;
        private const int ColumnCount = 8;

        private static readonly AccessTools.FieldRef<Dialog_Trade, List<Tradeable>> cachedTradeablesField =
            AccessTools.FieldRefAccess<Dialog_Trade, List<Tradeable>>("cachedTradeables");
        private static readonly AccessTools.FieldRef<Dialog_Trade, Tradeable> cachedCurrencyField =
            AccessTools.FieldRefAccess<Dialog_Trade, Tradeable>("cachedCurrencyTradeable");
        private static readonly AccessTools.FieldRef<Dialog_Trade, bool> giftsOnlyField =
            AccessTools.FieldRefAccess<Dialog_Trade, bool>("giftsOnly");
        private static readonly AccessTools.FieldRef<Dialog_Trade, bool> playerIsCaravanField =
            AccessTools.FieldRefAccess<Dialog_Trade, bool>("playerIsCaravan");

        private readonly Dialog_Trade dialog;
        private readonly bool swappedIn;
        private readonly TransferableTableColumns.WidgetView tradeView = TransferableTableColumns.TradeView();
        private readonly List<Tradeable> tradeRows = new List<Tradeable>();
        private List<Tradeable> defaultOrder;
        private Tradeable currencyRow;
        private bool rowsLoaded;
        private readonly List<string> summaryItems = new List<string>();
        private readonly List<string> summaryKinds = new List<string>();
        private readonly List<ScreenAction> actions = new List<ScreenAction>();
        private bool announcedOpen;

        public TradeScope(Dialog_Trade dialog, bool swappedIn = false)
        {
            this.dialog = dialog;
            this.swappedIn = swappedIn;

            Claim(SharedMenuGrammar.Cancel,
                delegate { TradeNavigationState.CloseAndAnnounceCancel(); },
                when: () => !TypeaheadHasActiveSearch);

            Claim("trade.quantity.increase", delegate { TradeRowActions.AdjustBy(CurrentTradeable(), 1); });
            Claim("trade.quantity.decrease", delegate { TradeRowActions.AdjustBy(CurrentTradeable(), -1); });
            Claim("trade.quantity.increaseTen", delegate { TradeRowActions.AdjustBy(CurrentTradeable(), 10); });
            Claim("trade.quantity.decreaseTen", delegate { TradeRowActions.AdjustBy(CurrentTradeable(), -10); });
            Claim("trade.quantity.increaseHundred", delegate { TradeRowActions.AdjustBy(CurrentTradeable(), 100); });
            Claim("trade.quantity.decreaseHundred", delegate { TradeRowActions.AdjustBy(CurrentTradeable(), -100); });
            Claim("trade.quantity.max", delegate { TradeRowActions.SetExtreme(CurrentTradeable(), max: true); });
            Claim("trade.quantity.min", delegate { TradeRowActions.SetExtreme(CurrentTradeable(), max: false); });
            Claim("trade.item.reset", delegate { TradeRowActions.ResetItem(CurrentTradeable()); });
            Claim("trade.accept", delegate { TradeNavigationState.AcceptTrade(); });
            Claim("trade.resetAll", delegate { ResetAll(); });
            Claim("trade.giftMode.toggle", delegate { ToggleGiftMode(); });
            Claim("trade.priceBreakdown", delegate { ShowPriceBreakdown(); });
            Claim("trade.balance", delegate { TradeNavigationState.AnnounceTradeBalance(); });
            Claim("trade.inspect", delegate { TradeRowActions.InspectItem(CurrentTradeable()); });
            Claim("trade.showSellableItems", delegate { TradeRowActions.ShowSellableItems(); });
            Claim("trade.swapView", delegate { TradeViewOpener.SwapView(dialog); });
        }

        public override string Name
        {
            get { return "trade"; }
        }

        /// <summary>
        /// Deliberately carries no <c>WindowlessInspectionState</c> term: that deadlocks the scope
        /// when the dialog is opened from the inspection tree (see LordJobDialogScope.IsLive).
        /// </summary>
        public override bool IsLive
        {
            get { return TradeNavigationState.IsActive; }
        }

        /// <summary>
        /// True: TradeNavigationPatch's blocker eats vanilla's Escape close for the whole session, so
        /// the base typeahead claim clears an active search and this scope's claim closes otherwise.
        /// </summary>
        public override bool OwnsCancel
        {
            get { return true; }
        }

        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        protected override bool ContentRegionSearchable(int region)
        {
            return region == TradesRegion;
        }

        /// <summary>False — the dialog's sort dropdowns and count-adjust widgets draw their own ButtonText calls; the real actions are declared below.</summary>
        protected override bool CaptureWindowButtons
        {
            get { return false; }
        }

        // ------------------------------------------------------------------
        // Content contract.
        // ------------------------------------------------------------------

        private bool HasSummary
        {
            get { return dialog != null && playerIsCaravanField(dialog); }
        }

        protected override int ContentRegionCount
        {
            get { return HasSummary ? 2 : 1; }
        }

        protected override string ContentRegionName(int region)
        {
            if (region == SummaryRegion)
            {
                return "RimWorldAccess.Caravan.Form.SummaryRegionName".Translate().ToString();
            }
            string traderName = TradeSession.trader?.TraderName;
            return string.IsNullOrEmpty(traderName)
                ? "RimWorldAccess.Sellable.Open.TraderFallback".Translate().ToString()
                : traderName;
        }

        protected override int ContentItemCount(int region)
        {
            if (region == SummaryRegion)
            {
                return summaryItems.Count;
            }
            return (currencyRow != null ? 1 : 0) + tradeRows.Count;
        }

        protected override int ContentColumnCount(int region)
        {
            return region == SummaryRegion ? 2 : ColumnCount;
        }

        protected override TableColumnInfo ContentColumnInfo(int region, int column)
        {
            if (region == SummaryRegion)
            {
                return column == 0
                    ? new TableColumnInfo("RimWorldAccess.Common.NameColumn".Translate().ToString(), null, false)
                    : new TableColumnInfo("Value".Translate().ToString(), null, false);
            }
            switch (column)
            {
                case IdentityColumn:
                    return TransferableTableColumns.IdentityColumnInfo(null);
                case ColonyCountColumn:
                    // Header tips are vanilla's own count tooltips (TradeUI TipRegionByKey).
                    return new TableColumnInfo("RimWorldAccess.Trade.Column.Yours".Translate().ToString(),
                        "ColonyCount".Translate().ToString(), true);
                case SellPriceColumn:
                    return new TableColumnInfo("RimWorldAccess.Trade.Column.SellPrice".Translate().ToString(), null, true);
                case TradeAmountColumn:
                    return new TableColumnInfo("RimWorldAccess.Trade.Column.TradeAmount".Translate().ToString(), null, true);
                case BuyPriceColumn:
                    return new TableColumnInfo("RimWorldAccess.Trade.Column.BuyPrice".Translate().ToString(), null, true);
                case TraderCountColumn:
                    return new TableColumnInfo("RimWorldAccess.Trade.Column.Theirs".Translate().ToString(),
                        "TraderCount".Translate().ToString(), true);
                case MassColumn:
                    return TransferableTableColumns.ColumnInfo(TransferableTableColumns.ColumnKind.Mass, isPawnColumn: false);
                case MarketValueColumn:
                    return TransferableTableColumns.ColumnInfo(TransferableTableColumns.ColumnKind.MarketValue, isPawnColumn: false);
                default:
                    return new TableColumnInfo();
            }
        }

        /// <summary>
        /// Rows load once from the dialog's cache so an active sort survives refreshes; Summary stat
        /// lines rebuild every refresh, their values changing with every count edit. Gift toggle and
        /// reset-all force a reload, since deal.Reset replaces the Tradeable objects.
        /// </summary>
        protected override void RefreshContent()
        {
            if (dialog == null)
            {
                return;
            }
            if (!rowsLoaded)
            {
                rowsLoaded = true;
                tradeRows.Clear();
                List<Tradeable> cached = cachedTradeablesField(dialog);
                if (cached != null)
                {
                    tradeRows.AddRange(cached);
                }
                currencyRow = cachedCurrencyField(dialog);
                defaultOrder = new List<Tradeable>(tradeRows);
            }

            summaryItems.Clear();
            summaryKinds.Clear();
            if (HasSummary)
            {
                BuildSummaryItems();
            }
        }

        /// <summary>Drops the row snapshot so the next refresh re-reads the dialog's cache (after gift toggle, reset, failed accept).</summary>
        private void ReloadRows()
        {
            rowsLoaded = false;
            RefreshModel();
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            var d = new ElementDescription();
            if (region == SummaryRegion)
            {
                d.Label = index >= 0 && index < summaryKinds.Count ? StatShortName(summaryKinds[index]) : "";
                return d;
            }
            d.Label = TradeRowActions.RowLabel(RowAt(index));
            return d;
        }

        protected override string ContentCellText(int region, int row, int column)
        {
            if (region == SummaryRegion)
            {
                if (row < 0 || row >= summaryItems.Count)
                    return "";
                return column == 0 ? StatShortName(summaryKinds[row]) : summaryItems[row];
            }
            Tradeable t = RowAt(row);
            if (t == null)
            {
                return "";
            }
            switch (column)
            {
                case IdentityColumn:
                    return TradeRowActions.RowLabel(t);
                case ColonyCountColumn:
                {
                    int held = t.CountHeldBy(Transactor.Colony);
                    return held != 0 || t.IsCurrency ? held.ToString() : "";
                }
                case SellPriceColumn:
                    return PriceCellText(t, TradeAction.PlayerSells, t.CountHeldBy(Transactor.Colony));
                case TradeAmountColumn:
                    return TradeAmountText(t);
                case BuyPriceColumn:
                    return PriceCellText(t, TradeAction.PlayerBuys, t.CountHeldBy(Transactor.Trader));
                case TraderCountColumn:
                {
                    int held = t.CountHeldBy(Transactor.Trader);
                    return held != 0 && t.IsThing ? held.ToString() : "";
                }
                case MassColumn:
                    return TransferableTableColumns.CellText(TransferableTableColumns.ColumnKind.Mass, t, tradeView);
                case MarketValueColumn:
                    return TransferableTableColumns.CellText(TransferableTableColumns.ColumnKind.MarketValue, t, tradeView);
                default:
                    return "";
            }
        }

        protected override string ContentCellTip(int region, int row, int column)
        {
            if (region == SummaryRegion)
            {
                return null;
            }
            Tradeable t = RowAt(row);
            if (t == null)
            {
                return null;
            }
            switch (column)
            {
                case IdentityColumn:
                    return t.HasAnyThing ? t.TipDescription : null;
                case SellPriceColumn:
                {
                    string quality = TradeNavigationState.GetPriceQuality(t, TradeAction.PlayerSells);
                    return string.IsNullOrEmpty(quality) ? null : quality;
                }
                case BuyPriceColumn:
                {
                    string quality = TradeNavigationState.GetPriceQuality(t, TradeAction.PlayerBuys);
                    return string.IsNullOrEmpty(quality) ? null : quality;
                }
                case TradeAmountColumn:
                    return t.CountToTransfer == 0 || t.IsCurrency ? null : TradeRowActions.PendingTotalLine(t);
                case MassColumn:
                    return TransferableTableColumns.CellTip(TransferableTableColumns.ColumnKind.Mass, t, tradeView);
                default:
                    return null;
            }
        }

        protected override void ActivateContentItem(int region, int index)
        {
            TypeaheadReset();
            if (region == SummaryRegion)
            {
                OpenStatBreakdown();
                return;
            }
            Tradeable t = RowAt(index);
            if (t == null)
            {
                return;
            }
            if (t.IsCurrency && !t.Interactive)
            {
                // The currency row is read-only outside gift mode, so Enter reads the balance.
                TradeNavigationState.AnnounceTradeBalance();
                return;
            }
            if (!TradeRowActions.CanAdjust(t, announceReason: true))
            {
                return;
            }
            TradeRowActions.OpenQuantityMenu(t);
        }

        protected override int ApplyContentSort(int region, int column, SortCycleResult cycle, int currentRow)
        {
            if (region == SummaryRegion)
            {
                return -1;
            }
            int offset = currencyRow != null ? 1 : 0;
            Tradeable current = RowAt(currentRow);

            List<Tradeable> reordered;
            if (cycle == SortCycleResult.Cleared)
            {
                reordered = new List<Tradeable>(defaultOrder ?? tradeRows);
            }
            else
            {
                bool descending = cycle == SortCycleResult.SortedDescending;
                switch (column)
                {
                    case IdentityColumn:
                        reordered = TransferableTableColumns.SortByIdentity(tradeRows, descending);
                        break;
                    case ColonyCountColumn:
                        reordered = SortByNumeric(t => t.CountHeldBy(Transactor.Colony), descending);
                        break;
                    case SellPriceColumn:
                        reordered = SortByNumeric(t => PriceSortValue(t, TradeAction.PlayerSells, t.CountHeldBy(Transactor.Colony)), descending);
                        break;
                    case TradeAmountColumn:
                        // Signed transfer-to-colony value: buys at one end, sells at the other,
                        // untouched rows between.
                        reordered = SortByNumeric(t => t.CountToTransferToSource, descending);
                        break;
                    case BuyPriceColumn:
                        reordered = SortByNumeric(t => PriceSortValue(t, TradeAction.PlayerBuys, t.CountHeldBy(Transactor.Trader)), descending);
                        break;
                    case TraderCountColumn:
                        reordered = SortByNumeric(t => t.CountHeldBy(Transactor.Trader), descending);
                        break;
                    case MassColumn:
                        reordered = TransferableTableColumns.SortByColumn(tradeRows, TransferableTableColumns.ColumnKind.Mass, descending, tradeView);
                        break;
                    case MarketValueColumn:
                        reordered = TransferableTableColumns.SortByColumn(tradeRows, TransferableTableColumns.ColumnKind.MarketValue, descending, tradeView);
                        break;
                    default:
                        return -1;
                }
            }

            tradeRows.Clear();
            tradeRows.AddRange(reordered);

            if (current == null)
            {
                return 0;
            }
            if (current == currencyRow)
            {
                return 0;
            }
            int idx = tradeRows.IndexOf(current);
            return idx >= 0 ? idx + offset : 0;
        }

        // ------------------------------------------------------------------
        // Toolbar: vanilla's real buttons, in vanilla's draw order.
        // ------------------------------------------------------------------

        /// <summary>Shift+Enter presses accept from anywhere; the label is vanilla's own live button text, so the prompt names whichever mode the player is in.</summary>
        protected override string DefaultAcceptActionId
        {
            get { return "trade.accept"; }
        }

        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                actions.Clear();
                TradeRowActions.AddToolbarActions(actions, dialog, dialog != null && giftsOnlyField(dialog),
                    ResetAll, ToggleGiftMode, TradeView.Table);
                return actions;
            }
        }

        // ------------------------------------------------------------------
        // Lifecycle.
        // ------------------------------------------------------------------

        public override void OnPush()
        {
            base.OnPush();
            TransferableRingRequest.CurrentProvider = () => CurrentTradeable();
        }

        public override void OnPop()
        {
            TransferableRingRequest.CurrentProvider = null;
            base.OnPop();
        }

        public override void OnFocus()
        {
            base.OnFocus();
            if (announcedOpen)
            {
                return;
            }
            announcedOpen = true;
            TolkHelper.SpeakData(swappedIn
                ? TradeViewOpener.ViewName(TradeView.Table)
                : TradeRowActions.OpeningHeading());
            AnnounceCurrentItem();
        }

        /// <summary>The row under the cursor stays inside the scroll view, so the count-field ring is always on screen.</summary>
        protected override void OnCursorSettled(int region, int index)
        {
            if (region != TradesRegion || index <= 0)
            {
                return;
            }
            TradeRowGeometry.ScrollIntoView(dialog, RowAt(index - 1));
        }

        // ------------------------------------------------------------------
        // Dialog actions.
        // ------------------------------------------------------------------

        /// <summary>Vanilla's Reset button verbatim: deal.Reset + CacheTradeables + CountToTransferChanged.</summary>
        private void ResetAll()
        {
            TradeNavigationState.ResetDeal();
            ReloadRows();
            TolkHelper.Speak("RimWorldAccess.Trade.Reset.AllTrades".Loc());
            SoundDefOf.Checkbox_TurnedOff.PlayOneShotOnCamera();
            AnnounceCurrentItem();
        }

        private void ToggleGiftMode()
        {
            if (!TradeNavigationState.ToggleGiftMode())
            {
                return;
            }
            ReloadRows();
            AnnounceCurrentItem();
        }

        private void ShowPriceBreakdown()
        {
            Tradeable t = CurrentTradeable();
            if (t == null)
            {
                TolkHelper.Speak("RimWorldAccess.Guard.NoItemSelected".Loc());
                return;
            }
            // Column-aware: the buy/sell price columns show that side, elsewhere the side the row
            // actually offers.
            TableModel table = Model.CurrentTable;
            int column = table != null ? table.ColumnIndex : IdentityColumn;
            TradeAction action;
            if (column == BuyPriceColumn)
            {
                action = TradeAction.PlayerBuys;
            }
            else if (column == SellPriceColumn)
            {
                action = TradeAction.PlayerSells;
            }
            else
            {
                action = t.CountHeldBy(Transactor.Colony) > 0
                    ? TradeAction.PlayerSells
                    : TradeAction.PlayerBuys;
            }
            TradeNavigationState.ShowPriceBreakdown(t, action);
        }

        // ------------------------------------------------------------------
        // Summary (caravan trades), off the dialog's own cached stat properties.
        // ------------------------------------------------------------------

        private void BuildSummaryItems()
        {
            try
            {
                float massUsage = (float)AccessTools.Property(typeof(Dialog_Trade), "MassUsage").GetValue(dialog);
                float massCapacity = (float)AccessTools.Property(typeof(Dialog_Trade), "MassCapacity").GetValue(dialog);
                summaryItems.Add(CaravanStatFormatter.FormatMass(massUsage, massCapacity));
                summaryKinds.Add("Mass");

                var tilesInfo = AccessTools.Property(typeof(Dialog_Trade), "TilesPerDay");
                if (tilesInfo != null)
                {
                    summaryItems.Add(CaravanStatFormatter.FormatSpeed((float)tilesInfo.GetValue(dialog), massUsage > massCapacity));
                    summaryKinds.Add("Speed");
                }
                var foodInfo = AccessTools.Property(typeof(Dialog_Trade), "DaysWorthOfFood");
                if (foodInfo != null)
                {
                    var food = (ValueTuple<float, float>)foodInfo.GetValue(dialog);
                    summaryItems.Add(CaravanStatFormatter.FormatFood(food.Item1, food.Item2));
                    summaryKinds.Add("Food");
                }
                var forageInfo = AccessTools.Property(typeof(Dialog_Trade), "ForagedFoodPerDay");
                if (forageInfo != null)
                {
                    var forage = (ValueTuple<ThingDef, float>)forageInfo.GetValue(dialog);
                    summaryItems.Add(CaravanStatFormatter.FormatForaging(forage.Item1, forage.Item2));
                    summaryKinds.Add("Foraging");
                }
                var visInfo = AccessTools.Property(typeof(Dialog_Trade), "Visibility");
                if (visInfo != null)
                {
                    summaryItems.Add(CaravanStatFormatter.FormatVisibility((float)visInfo.GetValue(dialog)));
                    summaryKinds.Add("Visibility");
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[RimWorld Access] Trade summary stats not readable: " + ex.Message);
            }
        }

        private static string StatShortName(string kind)
        {
            switch (kind)
            {
                case "Mass": return "RimWorldAccess.Caravan.Inspect.StatMass".Translate();
                case "Speed": return "RimWorldAccess.Caravan.Inspect.StatSpeed".Translate();
                case "Food": return "RimWorldAccess.Caravan.Inspect.StatFood".Translate();
                case "Foraging": return "RimWorldAccess.Caravan.Inspect.StatForaging".Translate();
                case "Visibility": return "RimWorldAccess.Caravan.Inspect.StatVisibility".Translate();
                default: return kind ?? "";
            }
        }

        private void OpenStatBreakdown()
        {
            TableModel table = Model.CurrentTable;
            int index = table == null ? -1 : table.Rows.Index - 1;
            if (index < 0 || index >= summaryKinds.Count)
            {
                return;
            }
            string kind = summaryKinds[index];
            string fieldName;
            string propertyName;
            switch (kind)
            {
                case "Mass": fieldName = "cachedMassCapacityExplanation"; propertyName = "MassCapacity"; break;
                case "Speed": fieldName = "cachedTilesPerDayExplanation"; propertyName = "TilesPerDay"; break;
                case "Foraging": fieldName = "cachedForagedFoodPerDayExplanation"; propertyName = "ForagedFoodPerDay"; break;
                case "Visibility": fieldName = "cachedVisibilityExplanation"; propertyName = "Visibility"; break;
                default:
                    TolkHelper.Speak("RimWorldAccess.Caravan.Form.NoBreakdownForItem".Loc());
                    return;
            }
            try
            {
                // Reading the property refreshes the cached explanation.
                AccessTools.Property(typeof(Dialog_Trade), propertyName)?.GetValue(dialog);
                string explanation = AccessTools.Field(typeof(Dialog_Trade), fieldName)?.GetValue(dialog) as string;
                if (!string.IsNullOrEmpty(explanation))
                {
                    StatBreakdownState.Open(StatShortName(kind), explanation);
                    return;
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[RimWorld Access] Trade stat explanation not readable: " + ex.Message);
            }
            TolkHelper.Speak("RimWorldAccess.Caravan.Form.NoBreakdownForItem".Loc());
        }

        // ------------------------------------------------------------------
        // Cells and rows.
        // ------------------------------------------------------------------

        /// <summary>The tradeable at a 0-based data-row index; the currency row is pinned at index 0 when present.</summary>
        private Tradeable RowAt(int index)
        {
            if (index < 0)
            {
                return null;
            }
            if (currencyRow != null)
            {
                if (index == 0)
                {
                    return currencyRow;
                }
                index--;
            }
            return index < tradeRows.Count ? tradeRows[index] : null;
        }

        /// <summary>The tradeable under the row cursor, or null on the header row / outside the Trades region.</summary>
        private Tradeable CurrentTradeable()
        {
            RefreshModel();
            if (Model.RegionIndex != TradesRegion)
            {
                return null;
            }
            TableModel table = Model.CurrentTable;
            if (table == null)
            {
                return null;
            }
            return RowAt(table.Rows.Index - 1);
        }

        private static string TradeAmountText(Tradeable t)
        {
            if (t.CountToTransfer == 0)
            {
                return "";
            }
            if (t.IsCurrency)
            {
                return t.CountToTransfer.ToStringWithSign();
            }
            return TradeRowActions.PendingPhrase(t);
        }

        /// <summary>A price cell, gated exactly as vanilla's DrawPrice: blank for currency, no-trade rows, and rows the side doesn't hold.</summary>
        private static string PriceCellText(Tradeable t, TradeAction action, int heldBySide)
        {
            if (t.IsCurrency || !t.TraderWillTrade || heldBySide == 0)
            {
                return "";
            }
            return TradeNavigationState.FormatPrice(t.GetPriceFor(action));
        }

        private static float PriceSortValue(Tradeable t, TradeAction action, int heldBySide)
        {
            if (t.IsCurrency || !t.TraderWillTrade || heldBySide == 0)
            {
                return float.NaN;
            }
            return t.GetPriceFor(action);
        }

        /// <summary>Numeric column sort with blank (NaN) rows at the bottom in both directions, the shared table convention.</summary>
        private List<Tradeable> SortByNumeric(Func<Tradeable, float> value, bool descending)
        {
            var sorted = new List<Tradeable>(tradeRows);
            sorted.SortStable((a, b) =>
            {
                float va = value(a);
                float vb = value(b);
                bool blankA = float.IsNaN(va);
                bool blankB = float.IsNaN(vb);
                if (blankA || blankB)
                {
                    return blankA == blankB ? 0 : (blankA ? 1 : -1);
                }
                return descending ? vb.CompareTo(va) : va.CompareTo(vb);
            });
            return sorted;
        }
    }
}
