using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// <see cref="Dialog_SellableItems"/> (the read-only "things this trader
    /// will buy" browser) as a ScreenScope — table-model T3b. The dialog's
    /// category tabs become regions (Tab/Shift+Tab cycle them, replacing the
    /// retired Left/Right tab chords; cross-region typeahead searches every
    /// category at once); each region is a one-column sortable table of the
    /// dialog's own item lists.
    ///
    /// Regions map to vanilla's own tab construction (CalculateTabs: every
    /// root ThingCategoryDef except Animals with sellable items, in
    /// DefDatabase order, plus the Pawns tab last); region labels read from
    /// the dialog's live TabRecord list, and region changes fire the
    /// TabRecord's own clickedAction so the sighted view follows.
    ///
    /// Enter (or Alt+I) on a row opens the item's info card — the pre-table
    /// scope documented Enter closing this dialog silently through vanilla's
    /// unblocked closeOnAccept as a harmless gap; the table contract gives
    /// Enter a real row action, and the base ScreenScope's OwnsAccept plus
    /// the WindowKeyRouter now block that silent close. OwnsCancel is true
    /// outright: Escape clears an active search (base claim) or closes with
    /// the closed announcement (this scope's claim) — the router blocks
    /// vanilla's own cancel close, retiring the per-dialog blocker patch
    /// SellableItemsNavigationPatch used to carry.
    /// </summary>
    public sealed class SellableItemsScope : ScreenScope
    {
        private static readonly AccessTools.FieldRef<Dialog_SellableItems, Vector2> ScrollPosition =
            AccessTools.FieldRefAccess<Dialog_SellableItems, Vector2>("scrollPosition");

        private readonly Dialog_SellableItems dialog;
        private readonly List<List<ThingDef>> regionItems = new List<List<ThingDef>>();
        private readonly List<ThingCategoryDef> regionCategories = new List<ThingCategoryDef>();
        private readonly List<List<ThingDef>> regionDefaultOrder = new List<List<ThingDef>>();
        private bool announcedOpen;

        public SellableItemsScope(Dialog_SellableItems dialog)
        {
            this.dialog = dialog;
            Claim(SharedMenuGrammar.Cancel,
                delegate { SellableItemsState.Close(); },
                when: () => !TypeaheadHasActiveSearch);
            Claim("sellableItems.inspect", delegate { InspectCurrentItem(); });
        }

        public override string Name
        {
            get { return "sellable-items"; }
        }

        public override bool IsLive
        {
            get { return SellableItemsState.IsActive; }
        }

        public override bool OwnsCancel
        {
            get { return true; }
        }

        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        // ------------------------------------------------------------------
        // Content contract.
        // ------------------------------------------------------------------

        protected override int ContentRegionCount
        {
            get { return regionItems.Count; }
        }

        protected override string ContentRegionName(int region)
        {
            List<TabRecord> tabs = SellableItemsState.GetTabs();
            if (tabs != null && region >= 0 && region < tabs.Count && !string.IsNullOrEmpty(tabs[region].label))
            {
                return tabs[region].label;
            }
            if (region >= 0 && region < regionCategories.Count && regionCategories[region] != null)
            {
                return regionCategories[region].LabelCap.ToString();
            }
            return "PawnsTabShort".Translate().ToString();
        }

        protected override int ContentItemCount(int region)
        {
            return region >= 0 && region < regionItems.Count ? regionItems[region].Count : 0;
        }

        protected override int ContentColumnCount(int region)
        {
            return 1;
        }

        protected override TableColumnInfo ContentColumnInfo(int region, int column)
        {
            return TransferableTableColumns.IdentityColumnInfo(null);
        }

        /// <summary>
        /// One region per vanilla tab: root categories (except Animals) with
        /// sellable items, in DefDatabase order — vanilla's own CalculateTabs
        /// rule — then the Pawns tab. Item lists are copies of the dialog's
        /// own (cached) lists so a header sort never mutates the dialog.
        /// </summary>
        protected override void RefreshContent()
        {
            if (regionItems.Count > 0)
            {
                return;
            }
            regionCategories.Clear();
            foreach (ThingCategoryDef category in DefDatabase<ThingCategoryDef>.AllDefsListForReading)
            {
                if (category.parent != ThingCategoryDefOf.Root || category == ThingCategoryDefOf.Animals)
                {
                    continue;
                }
                List<ThingDef> items = SellableItemsState.GetItemsInCategory(category, pawns: false);
                if (items != null && items.Count > 0)
                {
                    regionCategories.Add(category);
                    regionItems.Add(new List<ThingDef>(items));
                }
            }
            List<ThingDef> pawns = SellableItemsState.GetItemsInCategory(null, pawns: true) ?? new List<ThingDef>();
            regionCategories.Add(null);
            regionItems.Add(new List<ThingDef>(pawns));

            regionDefaultOrder.Clear();
            foreach (List<ThingDef> items in regionItems)
            {
                regionDefaultOrder.Add(new List<ThingDef>(items));
            }
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            var d = new ElementDescription();
            ThingDef item = ItemAt(region, index);
            d.Label = item != null ? item.LabelCap.ToString() : "";
            return d;
        }

        protected override string ContentCellText(int region, int row, int column)
        {
            ThingDef item = ItemAt(region, row);
            return item != null ? item.LabelCap.ToString() : "";
        }

        /// <summary>The item's description — what a sighted player reads in the info card; the old flat list spoke it per row too.</summary>
        protected override string ContentCellTip(int region, int row, int column)
        {
            ThingDef item = ItemAt(region, row);
            return item != null && !string.IsNullOrEmpty(item.description) ? item.description : null;
        }

        protected override void ActivateContentItem(int region, int index)
        {
            TypeaheadReset();
            OpenInfoCard(ItemAt(region, index));
        }

        protected override int ApplyContentSort(int region, int column, SortCycleResult cycle, int currentRow)
        {
            if (region < 0 || region >= regionItems.Count)
            {
                return -1;
            }
            List<ThingDef> items = regionItems[region];
            ThingDef current = ItemAt(region, currentRow);

            if (cycle == SortCycleResult.Cleared)
            {
                items.Clear();
                items.AddRange(regionDefaultOrder[region]);
            }
            else
            {
                bool descending = cycle == SortCycleResult.SortedDescending;
                items.Sort((a, b) => descending
                    ? string.Compare(b.label, a.label, System.StringComparison.CurrentCultureIgnoreCase)
                    : string.Compare(a.label, b.label, System.StringComparison.CurrentCultureIgnoreCase));
            }

            if (current == null)
            {
                return 0;
            }
            int idx = items.IndexOf(current);
            return idx >= 0 ? idx : 0;
        }

        /// <summary>Mirror the region cursor into the dialog's own tab so the sighted view follows.</summary>
        protected override void OnRegionChanged(MoveResult result)
        {
            SellableItemsState.ActivateTab(Model.RegionIndex);
        }

        /// <summary>
        /// Scrolls the focused row into view, one corrective write per settle. Ring identity is
        /// the row's ThingDef rather than its table ordinal since a header sort reorders our rows
        /// but never vanilla's own draw order (regionDefaultOrder mirrors that order); a stale
        /// geometry capture (no draw pass yet, e.g. right after a region switch) skips the write
        /// and self-heals on the next settle.
        /// </summary>
        protected override void OnCursorSettled(int region, int index)
        {
            if (region < 0 || region >= regionDefaultOrder.Count || index <= 0)
            {
                return;
            }
            ThingDef def = ItemAt(region, index - 1);
            if (def == null)
            {
                return;
            }
            int ord = regionDefaultOrder[region].IndexOf(def);
            float outRectHeight = SellableItemsDrawPatch.OutRectHeight();
            if (ord < 0 || outRectHeight < 0f)
            {
                return;
            }
            Vector2 scroll = ScrollPosition(dialog);
            scroll.y = Mathf.Clamp(scroll.y, ord * 24f + 24f - outRectHeight, ord * 24f);
            ScrollPosition(dialog) = scroll;
        }

        /// <summary>
        /// The focused row's absolute rect (decompiled Dialog_SellableItems.cs:86-102):
        /// SellableItemsDrawPatch captures the scroll view's outRect origin and height from the
        /// DoBottomButtons prefix that runs just before it; the row's vanilla ordinal comes from
        /// regionDefaultOrder, which mirrors vanilla's own (unsorted) item list.
        /// </summary>
        protected internal override Rect FocusedContentRect()
        {
            TableModel table = Model.CurrentTable;
            int region = Model.RegionIndex;
            if (table == null || region < 0 || region >= regionDefaultOrder.Count)
            {
                return default(Rect);
            }
            ThingDef def = ItemAt(region, table.Rows.Index - 1);
            if (def == null)
            {
                return default(Rect);
            }
            int ord = regionDefaultOrder[region].IndexOf(def);
            if (ord < 0)
            {
                return default(Rect);
            }
            return SellableItemsDrawPatch.RowRect(ord, ScrollPosition(dialog).y);
        }

        // ------------------------------------------------------------------
        // Lifecycle.
        // ------------------------------------------------------------------

        public override void OnFocus()
        {
            base.OnFocus();
            if (announcedOpen)
            {
                return;
            }
            announcedOpen = true;
            AnnounceOpening();
            AnnounceCurrentItem();
        }

        private void AnnounceOpening()
        {
            ITrader trader = SellableItemsState.GetTrader();
            string traderName = trader?.TraderName
                ?? "RimWorldAccess.Sellable.Open.TraderFallback".Translate().ToString();

            int tabCountNum = regionItems.Count;
            string tabCount = tabCountNum == 1
                ? "RimWorldAccess.Sellable.Open.CategoriesOne".Translate().ToString()
                : "RimWorldAccess.Sellable.Open.CategoriesMany".Translate(tabCountNum).ToString();

            // The restock line vanilla draws under the title.
            string restockInfo = "";
            if (trader is ITraderRestockingInfoProvider restockProvider)
            {
                int nextRestockTick = restockProvider.NextRestockTick;
                if (nextRestockTick != -1)
                {
                    float daysUntilRestock = (nextRestockTick - Find.TickManager.TicksGame).TicksToDays();
                    restockInfo = "RimWorldAccess.Sellable.Restock.NextRestock".Translate(daysUntilRestock.ToString("0.0"));
                }
                else if (!restockProvider.EverVisited)
                {
                    restockInfo = "RimWorldAccess.Sellable.Restock.NotVisited".Translate();
                }
                else if (restockProvider.RestockedSinceLastVisit)
                {
                    restockInfo = "RimWorldAccess.Sellable.Restock.Restocked".Translate();
                }
            }

            string controls = "RimWorldAccess.Sellable.Open.Controls".Translate();
            TolkHelper.SpeakData((string)"RimWorldAccess.Sellable.Open.Heading"
                .Translate(traderName, restockInfo, tabCount, controls));
        }

        // ------------------------------------------------------------------
        // Items.
        // ------------------------------------------------------------------

        private ThingDef ItemAt(int region, int index)
        {
            if (region < 0 || region >= regionItems.Count)
            {
                return null;
            }
            List<ThingDef> items = regionItems[region];
            return index >= 0 && index < items.Count ? items[index] : null;
        }

        private ThingDef CurrentItem()
        {
            RefreshModel();
            TableModel table = Model.CurrentTable;
            if (table == null)
            {
                return null;
            }
            return ItemAt(Model.RegionIndex, table.Rows.Index - 1);
        }

        private void InspectCurrentItem()
        {
            OpenInfoCard(CurrentItem());
        }

        private void OpenInfoCard(ThingDef item)
        {
            if (item == null)
            {
                TolkHelper.Speak("RimWorldAccess.Guard.NoItemSelected".Loc());
                return;
            }
            if (!InfoCardState.TryOpenInfoCardForDef(item))
            {
                InfoCardState.SpeakNoInfoCardAvailable();
            }
        }
    }
}
