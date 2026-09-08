using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The keyboard focus scope for vanilla's <c>Dialog_StylingStation</c>
    /// (Ideology DLC), driving <see cref="StylingStationState"/>.
    ///
    /// Content regions are the tab list: region count tracks
    /// <see cref="StylingStationState.TabCount"/>, so tabs appearing and
    /// disappearing (CanWantBeard, "DEV: Show all") is only a region-count change.
    /// Per-region item data (<see cref="RegionData"/>) is rebuilt from static
    /// queries every <see cref="RefreshContent"/> pass rather than cached per
    /// loaded tab, which is what lets the shared typeahead engine search across
    /// every tab instead of only the focused one.
    ///
    /// The Body type and Head type regions exist only while
    /// <see cref="StylingStationState.DevEditMode"/> is true, gated exactly as
    /// vanilla gates the tabs themselves; both share vanilla's skin-color picker
    /// (<see cref="OpenSkinColorPicker"/>), opened after activating a row.
    ///
    /// <see cref="CaptureWindowButtons"/> is off because vanilla's own
    /// Cancel/Reset/Accept buttons would duplicate the declared Accept/Reset rows
    /// and add a Cancel row Escape already covers. Those declared rows ride the
    /// same <see cref="StylingStationHelper.Accept"/>/<see cref="StylingStationHelper.ResetAll"/>
    /// vehicle-B methods the chords use, so Alt+S works from any region.
    ///
    /// <c>styling.nextTab</c>/<c>styling.previousTab</c> stay registered but
    /// unclaimed: the universal region-cycle claims own those keys.
    /// </summary>
    public sealed class StylingStationScope : ScreenScope
    {
        private readonly Window dialog;
        private readonly List<ScreenAction> actions = new List<ScreenAction>(2);
        private bool announcedOpen;

        /// <summary>One content region's cached item list, rebuilt fresh every RefreshContent pass. Exactly one of the three lists is non-null, chosen by Kind.</summary>
        private sealed class RegionData
        {
            public StylingTabKind Kind;
            public List<StyleItemDef> StyleItems;
            public List<Apparel> ApparelItems;
            public List<Def> DevTypeItems;

            public int Count
            {
                get
                {
                    if (StyleItems != null) return StyleItems.Count;
                    if (ApparelItems != null) return ApparelItems.Count;
                    if (DevTypeItems != null) return DevTypeItems.Count;
                    return 0;
                }
            }
        }

        private readonly List<RegionData> regions = new List<RegionData>();

        public StylingStationScope(Window page)
        {
            this.dialog = page;

            // RightBracket is also the plain "reset all" chord below, so the
            // dev-menu claim registers first (claim order is priority for a chord)
            // and leads with Reset; non-dev RightBracket falls through.
            Claim("styling.contextMenu", delegate { OpenDevContextMenu(); }, when: () => Prefs.DevMode);
            Claim("styling.resetAll", delegate { DoResetAll(); });
            Claim("styling.apply", delegate { OnAccept(); });
            Claim(SharedMenuGrammar.Cancel, delegate { OnCancel(); });
        }

        public override string Name
        {
            get { return "styling-station"; }
        }

        public override bool IsModal
        {
            get { return !TextDialogShared.ForeignWindowAbove(dialog); }
        }

        /// <summary>Escape is always mod-owned here (search-clear, else revert-and-close) — never vanilla's, which is inert for this dialog regardless (closeOnCancel = false).</summary>
        public override bool OwnsCancel
        {
            get { return true; }
        }

        /// <summary>Cross-region search (table-model T4) over every tab's items, ranked current-tab-first.</summary>
        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        /// <summary>Vanilla's own Cancel/Reset/Accept buttons would duplicate the declared Accept/Reset rows below and add a redundant Cancel row; Escape already covers Cancel.</summary>
        protected override bool CaptureWindowButtons
        {
            get { return false; }
        }

        internal bool Owns(Window window)
        {
            return ReferenceEquals(window, dialog);
        }

        protected override int ContentRegionCount
        {
            get { return StylingStationState.TabCount; }
        }

        protected override string ContentRegionName(int region)
        {
            return TabLabel(StylingStationState.TabKind(region));
        }

        protected override void RefreshContent()
        {
            RebuildRegionData();
        }

        protected override int ContentItemCount(int region)
        {
            if (region < 0 || region >= regions.Count) return 0;
            return regions[region].Count;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            if (region < 0 || region >= regions.Count) return new ElementDescription();
            RegionData data = regions[region];
            if (data.ApparelItems != null) return DescribeApparelRow(data.ApparelItems, index);
            if (data.DevTypeItems != null) return DescribeDevTypeRow(data.Kind, data.DevTypeItems, index);
            return DescribeStyleItemRow(data.Kind, data.StyleItems, index);
        }

        protected override void ActivateContentItem(int region, int index)
        {
            if (region < 0 || region >= regions.Count) return;
            RegionData data = regions[region];
            if (data.ApparelItems != null) { ActivateApparelRow(data.ApparelItems, index); return; }
            if (data.DevTypeItems != null) { ActivateDevTypeRow(data.Kind, data.DevTypeItems, index); return; }
            ActivateStyleItemRow(data.Kind, data.StyleItems, index);
        }

        /// <summary>Shift+Enter presses Accept from anywhere on this screen: the one-chord proceed.</summary>
        protected override string DefaultAcceptActionId
        {
            get { return "styling.apply"; }
        }

        /// <summary>The two chord-driven actions, discoverable via the Buttons region; both still ride their existing vehicle-B methods, not a click-injection, so the chords keep working from any region.</summary>
        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                actions.Clear();
                actions.Add(new ScreenAction((string)"Accept".Translate(), OnAccept, "styling.apply"));
                actions.Add(new ScreenAction((string)"ResetButton".Translate(), DoResetAll, "styling.resetAll"));
                return actions;
            }
        }

        /// <summary>Rebuilds every region's item list fresh from pure static queries — no per-region "currently loaded" caching, so the typeahead engine can search every tab.</summary>
        private void RebuildRegionData()
        {
            regions.Clear();
            Pawn pawn = StylingStationState.Pawn;
            int count = StylingStationState.TabCount;
            for (int i = 0; i < count; i++)
            {
                StylingTabKind kind = StylingStationState.TabKind(i);
                var data = new RegionData { Kind = kind };
                switch (kind)
                {
                    case StylingTabKind.ApparelColor:
                        data.ApparelItems = StylingStationHelper.BuildApparelList(pawn, dialog);
                        break;
                    case StylingTabKind.BodyType:
                        data.DevTypeItems = StylingStationHelper.BuildBodyTypeItems();
                        break;
                    case StylingTabKind.HeadType:
                        data.DevTypeItems = StylingStationHelper.BuildHeadTypeItems();
                        break;
                    default:
                        data.StyleItems = StylingStationHelper.BuildItems(pawn, kind, StylingStationState.DevEditMode);
                        break;
                }
                regions.Add(data);
            }
        }

        /// <summary>
        /// Style item row: name, then — when this is the pawn's currently applied
        /// item — the color name (Hair/Beard only) and a "Selected" marker. The
        /// visual description goes in Extras, which speaks before Position.
        /// </summary>
        private static ElementDescription DescribeStyleItemRow(StylingTabKind kind, List<StyleItemDef> items, int index)
        {
            if (index < 0 || index >= items.Count) return new ElementDescription();
            StyleItemDef item = items[index];
            Pawn pawn = StylingStationState.Pawn;
            string label = item.LabelCap.ToString();

            if (item == StylingStationHelper.GetCurrentItem(pawn, kind))
            {
                if (kind == StylingTabKind.Hair || kind == StylingTabKind.Beard)
                {
                    string colorName = ColorNameHelper.NameForColor(StylingStationHelper.GetDesiredHairColor(StylingStationState.Dialog));
                    label += ", " + colorName;
                }
                label += ", " + (string)"RimWorldAccess.Styling.Selected".Translate();
            }

            string description = StyleDescriptionHelper.Describe(item);
            return new ElementDescription
            {
                Label = label,
                Extras = string.IsNullOrEmpty(description) ? null : description,
                // Enter applies the item and opens its picker on these rows, so they keep Enter (DefaultAcceptInertness).
                KeepsAccept = true,
            };
        }

        private static ElementDescription DescribeApparelRow(List<Apparel> items, int index)
        {
            if (index < 0 || index >= items.Count) return new ElementDescription();
            Apparel apparel = items[index];
            Pawn pawn = StylingStationState.Pawn;
            var colors = StylingStationHelper.GetApparelColors(StylingStationState.Dialog);
            string colorName = (colors != null && colors.TryGetValue(apparel, out Color c))
                ? ColorNameHelper.NameForColor(c) : "";
            bool locked = pawn.apparel != null && pawn.apparel.IsLocked(apparel);
            string label = (string)"RimWorldAccess.Styling.ApparelRow".Translate(apparel.LabelCap, colorName);
            if (locked)
                label += ", " + (string)"RimWorldAccess.Styling.Locked".Translate();
            return new ElementDescription { Label = label, KeepsAccept = true };
        }

        private static ElementDescription DescribeDevTypeRow(StylingTabKind kind, List<Def> items, int index)
        {
            if (index < 0 || index >= items.Count) return new ElementDescription();
            Def item = items[index];
            Pawn pawn = StylingStationState.Pawn;
            string label = item.LabelCap.ToString();
            bool isCurrent = kind == StylingTabKind.BodyType
                ? item == StylingStationHelper.GetCurrentBodyType(pawn)
                : item == (Def)StylingStationHelper.GetCurrentHeadType(pawn);
            if (isCurrent)
                label += ", " + (string)"RimWorldAccess.Styling.Selected".Translate();
            return new ElementDescription { Label = label, KeepsAccept = true };
        }

        /// <summary>
        /// Applies the selected style item live, then opens its color dropdown.
        /// Hair and Beard share the hair color; tattoos have none, so this just
        /// confirms the selection. Only reached outside an active search, since the
        /// base ActivateCurrent settles a search instead of activating through it.
        /// </summary>
        private void ActivateStyleItemRow(StylingTabKind kind, List<StyleItemDef> items, int index)
        {
            if (index < 0 || index >= items.Count) return;
            Pawn pawn = StylingStationState.Pawn;
            StyleItemDef item = items[index];
            StylingStationHelper.SelectItem(pawn, kind, item);

            if (kind == StylingTabKind.Hair || kind == StylingTabKind.Beard)
            {
                OpenHairColorPicker();
                return;
            }

            string message = (string)"RimWorldAccess.Styling.ItemSelected".Translate(item.LabelCap);
            string unwantedWarning = StylingStationHelper.GetUnwantedStyleWarning(pawn);
            if (!string.IsNullOrEmpty(unwantedWarning)) message += ". " + unwantedWarning;
            TolkHelper.SpeakData(message);
        }

        /// <summary>An apparel row's only action is recoloring it.</summary>
        private void ActivateApparelRow(List<Apparel> items, int index)
        {
            if (items.Count == 0) { TolkHelper.Speak("RimWorldAccess.Styling.NoApparel".Loc()); return; }
            if (index < 0 || index >= items.Count) return;
            OpenApparelColorPicker(items[index]);
        }

        /// <summary>Applies the selected body/head type live, then opens the shared skin-color picker (both dev tabs draw the same ColorSelector, decompiled :653-657, :708-712).</summary>
        private void ActivateDevTypeRow(StylingTabKind kind, List<Def> items, int index)
        {
            if (index < 0 || index >= items.Count) return;
            Pawn pawn = StylingStationState.Pawn;
            Def item = items[index];
            if (kind == StylingTabKind.BodyType)
                StylingStationHelper.SelectBodyType(pawn, (BodyTypeDef)item);
            else
                StylingStationHelper.SelectHeadType(pawn, (HeadTypeDef)item);
            OpenSkinColorPicker();
        }

        // Region sync mirrors the focused tab into the dialog's own curTab field and
        // lands the cursor on the pawn's currently applied style/type, not on a raw
        // remembered index.
        protected override void OnRegionChanged(MoveResult result)
        {
            SyncRegion(result.Index);
        }

        private void SyncRegion(int region)
        {
            if (region < 0 || region >= StylingStationState.TabCount) return;
            StylingStationHelper.SetVisibleTab(dialog, StylingStationState.TabKind(region));
            LandOnCurrentStyle(region);
        }

        private void LandOnCurrentStyle(int region)
        {
            if (region < 0 || region >= regions.Count) return;
            RegionData data = regions[region];
            Pawn pawn = StylingStationState.Pawn;
            int target = 0;

            if (data.StyleItems != null)
            {
                StyleItemDef current = StylingStationHelper.GetCurrentItem(pawn, data.Kind);
                int idx = current != null ? data.StyleItems.IndexOf(current) : -1;
                target = idx >= 0 ? idx : 0;
            }
            else if (data.DevTypeItems != null)
            {
                Def current = data.Kind == StylingTabKind.BodyType
                    ? (Def)StylingStationHelper.GetCurrentBodyType(pawn)
                    : StylingStationHelper.GetCurrentHeadType(pawn);
                int idx = current != null ? data.DevTypeItems.IndexOf(current) : -1;
                target = idx >= 0 ? idx : 0;
            }
            // ApparelColor: target stays 0, matching the pre-migration behavior.

            ListModel list = Model.Region(region);
            if (!list.IsEmpty && target >= 0 && target < list.Count)
            {
                list.MoveTo(target);
            }
        }

        // Vanilla's DrawBox marks the item the pawn wears, never the keyboard
        // cursor, and this screen does not auto-select on settle, so the focused
        // tile is ringed from the rects StylingGridDrawPatch records off vanilla's
        // own draw pass. Apparel rows are DrawApparelColor's per-garment bands,
        // computed closed-form below.
        private static readonly AccessTools.FieldRef<Dialog_StylingStation, Vector2> HairScrollRef =
            AccessTools.FieldRefAccess<Dialog_StylingStation, Vector2>("hairScrollPosition");
        private static readonly AccessTools.FieldRef<Dialog_StylingStation, Vector2> BeardScrollRef =
            AccessTools.FieldRefAccess<Dialog_StylingStation, Vector2>("beardScrollPosition");
        private static readonly AccessTools.FieldRef<Dialog_StylingStation, Vector2> FaceTattooScrollRef =
            AccessTools.FieldRefAccess<Dialog_StylingStation, Vector2>("faceTattooScrollPosition");
        private static readonly AccessTools.FieldRef<Dialog_StylingStation, Vector2> BodyTattooScrollRef =
            AccessTools.FieldRefAccess<Dialog_StylingStation, Vector2>("bodyTattooScrollPosition");
        private static readonly AccessTools.FieldRef<Dialog_StylingStation, Vector2> BodyTypeScrollRef =
            AccessTools.FieldRefAccess<Dialog_StylingStation, Vector2>("bodyTypeScrollPosition");
        private static readonly AccessTools.FieldRef<Dialog_StylingStation, Vector2> HeadTypeScrollRef =
            AccessTools.FieldRefAccess<Dialog_StylingStation, Vector2>("headTypeScrollPosition");
        private static readonly AccessTools.FieldRef<Dialog_StylingStation, Vector2> ApparelScrollRef =
            AccessTools.FieldRefAccess<Dialog_StylingStation, Vector2>("apparelColorScrollPosition");

        /// <summary>An apparel band and the distance to the next one (decompiled :434-484: 92f + 10f, then 34f for the row of colour buttons under it).</summary>
        private const float ApparelBandHeight = 92f;
        private const float ApparelBandStride = 136f;

        protected internal override Rect FocusedContentRect()
        {
            int region = Model.RegionIndex;
            ListModel list = Model.CurrentRegion;
            if (list == null || list.IsEmpty)
            {
                return default(Rect);
            }
            if (InApparelRegion(region))
            {
                return ApparelBandRect(region, list.Index);
            }
            Def def = GridDefAt(region, list.Index);
            if (def == null)
            {
                return default(Rect);
            }
            Rect screen, raw, outRect;
            if (!StylingGridDrawPatch.TryGetTile(regions[region].Kind, def, out screen, out raw, out outRect))
            {
                return default(Rect);
            }
            return screen;
        }

        protected override void OnCursorSettled(int region, int index)
        {
            if (InApparelRegion(region))
            {
                ScrollApparelBandIntoView(region, index);
                return;
            }
            ScrollTileIntoView(region, index);
        }

        private bool InApparelRegion(int region)
        {
            return region >= 0 && region < regions.Count && regions[region].Kind == StylingTabKind.ApparelColor;
        }

        /// <summary>
        /// The focused garment's band, on screen and clipped to the tab. No single
        /// widget draws the bands — a locked garment gets a band and a static icon,
        /// not a colour selector — so the layout is mirrored from DrawApparelColor:
        /// bands run one stride apart down a scroll view anchored on the tab rect's
        /// origin. Empty when the band is scrolled out or the tab has not drawn.
        /// </summary>
        private Rect ApparelBandRect(int region, int index)
        {
            var dialogTyped = dialog as Dialog_StylingStation;
            int ordinal = ApparelBandOrdinal(ApparelAt(region, index));
            if (dialogTyped == null || ApparelScrollRef == null || ordinal < 0)
            {
                return default(Rect);
            }
            Rect tabRect, tabScreen;
            if (!ColorSelectorDrawPatch.TryGetApparelTab(0, out tabRect, out tabScreen))
            {
                return default(Rect);
            }
            float top = tabScreen.y + ordinal * ApparelBandStride - ApparelScrollRef(dialogTyped).y;
            float yMin = Mathf.Max(top, tabScreen.yMin);
            float yMax = Mathf.Min(top + ApparelBandHeight, tabScreen.yMax);
            if (yMax <= yMin)
            {
                return default(Rect);
            }
            return new Rect(tabScreen.x, yMin, tabRect.width - 16f, yMax - yMin);
        }

        /// <summary>Scrolls the focused garment's band into the tab's visible band, one corrective write per settle.</summary>
        private void ScrollApparelBandIntoView(int region, int index)
        {
            var dialogTyped = dialog as Dialog_StylingStation;
            int ordinal = ApparelBandOrdinal(ApparelAt(region, index));
            if (dialogTyped == null || ApparelScrollRef == null || ordinal < 0)
            {
                return;
            }
            Rect tabRect, tabScreen;
            // A settle answers a key event, which can precede the frame that redraws the tab;
            // the band geometry it reads has not moved in the meantime.
            if (!ColorSelectorDrawPatch.TryGetApparelTab(1, out tabRect, out tabScreen))
            {
                return;
            }
            float top = ordinal * ApparelBandStride;
            ref Vector2 scroll = ref ApparelScrollRef(dialogTyped);
            float y = Mathf.Max(0f, Mathf.Clamp(scroll.y, top + ApparelBandHeight - tabRect.height, top));
            if (y != scroll.y)
            {
                scroll.y = y;
            }
        }

        /// <summary>The garment a row stands for, or null when the row is not an apparel row.</summary>
        private Apparel ApparelAt(int region, int index)
        {
            List<Apparel> items = InApparelRegion(region) ? regions[region].ApparelItems : null;
            return items != null && index >= 0 && index < items.Count ? items[index] : null;
        }

        /// <summary>
        /// The garment's band ordinal, walked off vanilla's own iteration rather than
        /// our row index: DrawApparelColor advances a band for every worn garment it
        /// holds a color for, locked or not. -1 when the garment is no longer among
        /// them, so a diverged list rings nothing instead of the wrong band.
        /// </summary>
        private static int ApparelBandOrdinal(Apparel apparel)
        {
            if (apparel == null)
            {
                return -1;
            }
            Pawn pawn = StylingStationState.Pawn;
            var colors = StylingStationHelper.GetApparelColors(StylingStationState.Dialog);
            if (pawn?.apparel == null || colors == null)
            {
                return -1;
            }
            List<Apparel> worn = pawn.apparel.WornApparel;
            int ordinal = 0;
            for (int i = 0; i < worn.Count; i++)
            {
                if (!colors.ContainsKey(worn[i]))
                {
                    continue;
                }
                if (ReferenceEquals(worn[i], apparel))
                {
                    return ordinal;
                }
                ordinal++;
            }
            return -1;
        }

        /// <summary>The def a grid row stands for, or null for the apparel-color region, the Buttons region, and any row that is not a tile.</summary>
        private Def GridDefAt(int region, int index)
        {
            if (region < 0 || region >= regions.Count)
            {
                return null;
            }
            RegionData data = regions[region];
            if (data.StyleItems != null)
            {
                return index >= 0 && index < data.StyleItems.Count ? data.StyleItems[index] : null;
            }
            if (data.DevTypeItems != null)
            {
                return index >= 0 && index < data.DevTypeItems.Count ? data.DevTypeItems[index] : null;
            }
            return null;
        }

        /// <summary>
        /// Scrolls the focused tile into its grid's visible band, one corrective
        /// write per settle. The grid's view space is anchored on the scroll view's
        /// own rect, so the tile's offset down the content is <c>raw.y - outRect.y</c>.
        /// Right after a region switch no pass has recorded the tab yet; skipping
        /// then is self-healing, as the next settle follows a pass that has drawn.
        /// </summary>
        private void ScrollTileIntoView(int region, int index)
        {
            Def def = GridDefAt(region, index);
            if (def == null)
            {
                return;
            }
            var dialogTyped = dialog as Dialog_StylingStation;
            AccessTools.FieldRef<Dialog_StylingStation, Vector2> scrollRef = ScrollRef(regions[region].Kind);
            if (dialogTyped == null || scrollRef == null)
            {
                return;
            }
            Rect screen, raw, outRect;
            if (!StylingGridDrawPatch.TryGetTile(regions[region].Kind, def, out screen, out raw, out outRect))
            {
                return;
            }

            Vector2 scroll = scrollRef(dialogTyped);
            float top = raw.y - outRect.y;
            float bottom = raw.yMax - outRect.y;
            float y = scroll.y;
            if (top < y)
            {
                y = top;
            }
            else if (bottom > y + outRect.height)
            {
                y = bottom - outRect.height;
            }
            y = Mathf.Max(y, 0f);
            if (y == scroll.y)
            {
                return;
            }
            scroll.y = y;
            scrollRef(dialogTyped) = scroll;
        }

        /// <summary>Each grid tab scrolls through its own field on the dialog (decompiled :322, :333, :344, :353, :363, :375).</summary>
        private static AccessTools.FieldRef<Dialog_StylingStation, Vector2> ScrollRef(StylingTabKind kind)
        {
            switch (kind)
            {
                case StylingTabKind.Hair: return HairScrollRef;
                case StylingTabKind.Beard: return BeardScrollRef;
                case StylingTabKind.FaceTattoo: return FaceTattooScrollRef;
                case StylingTabKind.BodyTattoo: return BodyTattooScrollRef;
                case StylingTabKind.BodyType: return BodyTypeScrollRef;
                case StylingTabKind.HeadType: return HeadTypeScrollRef;
                default: return null;
            }
        }

        public override void OnPush()
        {
            base.OnPush();
            ColorSelectorDrawPatch.AddInterest();
        }

        public override void OnPop()
        {
            ColorSelectorDrawPatch.RemoveInterest();
            base.OnPop();
        }

        public override void OnFocus()
        {
            base.OnFocus();
            if (announcedOpen) return;
            announcedOpen = true;

            SyncRegion(Model.RegionIndex >= 0 ? Model.RegionIndex : 0);

            Pawn pawn = StylingStationState.Pawn;
            string pawnName = pawn.Name?.ToStringShort ?? pawn.LabelShortCap;
            string openMessage = (string)"RimWorldAccess.Styling.Opened".Translate(pawnName);
            string unwantedWarning = StylingStationHelper.GetUnwantedStyleWarning(pawn);
            if (!string.IsNullOrEmpty(unwantedWarning)) openMessage += ". " + unwantedWarning;
            string tabCount = TabCountFragment();
            TolkHelper.SpeakData(string.IsNullOrEmpty(tabCount) ? openMessage : tabCount + ". " + openMessage);
            AnnounceRegion();
        }

        /// <summary>
        /// Content regions get a bespoke header: position clause, dye warning, and
        /// item count or "empty". The Buttons region falls back to the generic
        /// composer.
        /// </summary>
        protected override void AnnounceRegion()
        {
            int region = Model.RegionIndex;
            if (region < 0 || region >= StylingStationState.TabCount)
            {
                base.AnnounceRegion();
                return;
            }

            StylingTabKind kind = StylingStationState.TabKind(region);
            string tabName = TabLabel(kind);
            ListModel list = Model.CurrentRegion;
            int count = list == null ? 0 : list.Count;

            bool showPos = !string.IsNullOrEmpty(MenuHelper.FormatPosition(region, StylingStationState.TabCount));
            string posClause = showPos
                ? (string)"RimWorldAccess.Styling.TabPosition".Translate(region + 1, StylingStationState.TabCount) + ". "
                : "";

            string dyeWarning = GetTabDyeWarning(kind);
            string dyeSuffix = string.IsNullOrEmpty(dyeWarning) ? "" : ". " + dyeWarning;

            if (count == 0)
            {
                TolkHelper.SpeakData(posClause + (string)"RimWorldAccess.Styling.TabEmpty".Translate(tabName) + dyeSuffix);
                return;
            }

            TolkHelper.SpeakData(posClause + (string)"RimWorldAccess.Styling.TabHeader".Translate(tabName, count) + dyeSuffix);
            AnnounceCurrentItem();
        }

        /// <summary>
        /// The dye cost/shortage warning for a tab, matching vanilla's display scope:
        /// only Hair (DrawStylingItemType passes doColors:true there alone — Beard
        /// shares the color but never draws it) and Apparel Color show a selector.
        /// The dev tabs' skin-color picker has no dye requirement.
        /// </summary>
        private static string GetTabDyeWarning(StylingTabKind kind)
        {
            Window dialog = StylingStationState.Dialog;
            Pawn pawn = StylingStationState.Pawn;
            switch (kind)
            {
                case StylingTabKind.Hair: return StylingStationHelper.GetHairDyeWarning(dialog, pawn);
                case StylingTabKind.ApparelColor: return StylingStationHelper.GetApparelDyeWarning(dialog, pawn);
                default: return null;
            }
        }

        private void OnAccept()
        {
            StylingStationHelper.Accept(dialog);
        }

        private void DoResetAll()
        {
            StylingStationHelper.ResetAll(dialog);
            RefreshModel();
            LandOnCurrentStyle(Model.RegionIndex);
            TolkHelper.Speak("RimWorldAccess.Styling.ResetDone".Loc());
            AnnounceCurrentItem();
        }

        /// <summary>Escape: the base's own typeahead-active claim (registered ahead of this one) wins while a search is live; reaching here means no search, so revert all pending changes and close.</summary>
        private void OnCancel()
        {
            ShellFrameStamps.MarkCancelConsumed();
            StylingStationHelper.ResetAll(dialog);
            dialog.Close();
        }

        /// <summary>
        /// The RightBracket context menu for dev mode. RightBracket is also this
        /// screen's "reset all" chord, so the menu leads with Reset to keep it
        /// reachable, then adds the dialog's own "DEV: Show all" toggle.
        /// </summary>
        private void OpenDevContextMenu()
        {
            if (!Prefs.DevMode) return;

            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption((string)"ResetButton".Translate(), DoResetAll),
                new FloatMenuOption("DEV: Show all", ToggleDevEditMode),
            };
            WindowlessFloatMenuState.Open(options, colonistOrders: false);
        }

        /// <summary>
        /// Flips the "DEV: Show all" edit mode: rebuilds the tab set (Beard and the
        /// two dev tabs may appear or disappear), re-syncs the focused region, which
        /// may silently land on a different tab if one was inserted ahead of it, then
        /// announces the checkbox state and the current item.
        /// </summary>
        private void ToggleDevEditMode()
        {
            bool newValue = !StylingStationState.DevEditMode;
            StylingStationState.SetDevEditMode(newValue);
            RefreshModel();
            SyncRegion(Model.RegionIndex);

            string state = (newValue
                ? "RimWorldAccess.Shell.State.Checked"
                : "RimWorldAccess.Shell.State.Unchecked").Translate().ToString();
            TolkHelper.SpeakData("DEV: Show all. " + state + "."); // l10n-exempt: mirrors vanilla's own unlocalized "DEV: Show all" checkbox label (decompiled :233)
            AnnounceCurrentItem();
        }

        private void OpenHairColorPicker()
        {
            Pawn pawn = StylingStationState.Pawn;
            Color current = StylingStationHelper.GetDesiredHairColor(dialog);
            List<Color> colors = StylingStationHelper.GetAllHairColors(dialog);
            List<string> names = ColorNameHelper.NamesForColors(colors);

            var options = new List<FloatMenuOption>();
            int startIndex = 0;
            for (int i = 0; i < colors.Count; i++)
            {
                Color captured = colors[i];
                string name = names[i];
                string label = name;
                if (current.IndistinguishableFrom(captured))
                {
                    label += ", " + "RimWorldAccess.Styling.CurrentColor".Translate();
                    startIndex = i;
                }
                options.Add(new FloatMenuOption(label, () =>
                {
                    StylingStationHelper.SetDesiredHairColor(dialog, captured);
                    pawn.Drawer.renderer.SetAllGraphicsDirty();
                    string message = (string)"RimWorldAccess.Styling.ColorSet".Translate(name);
                    string dyeWarning = StylingStationHelper.GetHairDyeWarning(dialog, pawn);
                    if (!string.IsNullOrEmpty(dyeWarning)) message += ". " + dyeWarning;
                    TolkHelper.SpeakData(message);
                }));
            }

            if (options.Count == 0) { TolkHelper.Speak("NoneLower".Loc()); return; }
            string title = (string)"RimWorldAccess.Styling.HairColorTitle".Translate();
            string unwantedWarning = StylingStationHelper.GetUnwantedStyleWarning(pawn);
            if (!string.IsNullOrEmpty(unwantedWarning)) title += ". " + unwantedWarning;
            TolkHelper.SpeakData(title);
            WindowlessFloatMenuState.Open(options, colonistOrders: false, startIndex: startIndex);
        }

        private void OpenApparelColorPicker(Apparel apparel)
        {
            Pawn pawn = StylingStationState.Pawn;

            // Locked apparel cannot be recolored (matches vanilla's locked-row message).
            if (pawn.apparel != null && pawn.apparel.IsLocked(apparel))
            {
                TolkHelper.SpeakData(
                    (string)"ApparelLockedCannotRecolor".Translate(pawn.Named("PAWN"), apparel.Named("APPAREL")));
                return;
            }

            var colorsDict = StylingStationHelper.GetApparelColors(dialog);
            if (colorsDict == null) return;
            Color current = colorsDict.TryGetValue(apparel, out Color cc) ? cc : Color.white;

            List<Color> colors = StylingStationHelper.GetAllColors(dialog);
            List<string> names = ColorNameHelper.NamesForColors(colors);

            var options = new List<FloatMenuOption>();
            int startIndex = 0;
            for (int i = 0; i < colors.Count; i++)
            {
                Color captured = colors[i];
                string name = names[i];
                string label = name;
                if (current.IndistinguishableFrom(captured))
                {
                    label += ", " + "RimWorldAccess.Styling.CurrentColor".Translate();
                    startIndex = i;
                }
                string swatchTip = StylingStationHelper.ApparelColorSwatchTip(pawn, captured);
                if (!string.IsNullOrEmpty(swatchTip))
                    label += ". " + SpeechFlatten.ToSentences(swatchTip.StripTags());
                options.Add(new FloatMenuOption(label, () => SetApparelColor(apparel, captured, name)));
            }

            // The dialog's "Set ideoligion color" / "Set favorite color" buttons, as menu entries.
            if (pawn.Ideo != null && !Find.IdeoManager.classicMode)
            {
                Color ideoColor = pawn.Ideo.ApparelColor;
                options.Add(new FloatMenuOption((string)"SetIdeoColor".Translate(),
                    () => SetApparelColor(apparel, ideoColor, ColorNameHelper.NameForColor(ideoColor))));
            }
            if (pawn.story?.favoriteColor != null)
            {
                Color favColor = pawn.story.favoriteColor.color;
                options.Add(new FloatMenuOption((string)"SetFavoriteColor".Translate(),
                    () => SetApparelColor(apparel, favColor, ColorNameHelper.NameForColor(favColor))));
            }

            if (options.Count == 0) { TolkHelper.Speak("NoneLower".Loc()); return; }
            TolkHelper.SpeakData((string)"RimWorldAccess.Styling.ApparelColorTitle".Translate(apparel.LabelCap));
            WindowlessFloatMenuState.Open(options, colonistOrders: false, startIndex: startIndex);
        }

        private void SetApparelColor(Apparel apparel, Color color, string colorName)
        {
            var colors = StylingStationHelper.GetApparelColors(dialog);
            if (colors == null) return;
            colors[apparel] = color;
            StylingStationState.Pawn.Drawer.renderer.SetAllGraphicsDirty();
            string message = (string)"RimWorldAccess.Styling.ColorSet".Translate(colorName);
            string dyeWarning = StylingStationHelper.GetApparelDyeWarning(dialog, StylingStationState.Pawn);
            if (!string.IsNullOrEmpty(dyeWarning)) message += ". " + dyeWarning;
            TolkHelper.SpeakData(message);
        }

        /// <summary>Shared by the Body type and Head type tabs (both draw the same skin-color ColorSelector, decompiled :653-657, :708-712).</summary>
        private void OpenSkinColorPicker()
        {
            Pawn pawn = StylingStationState.Pawn;
            Color current = StylingStationHelper.GetDesiredSkinColor(dialog);
            List<Color> colors = StylingStationHelper.GetAllSkinColors(dialog);
            List<string> names = ColorNameHelper.NamesForColors(colors);

            var options = new List<FloatMenuOption>();
            int startIndex = 0;
            for (int i = 0; i < colors.Count; i++)
            {
                Color captured = colors[i];
                string name = names[i];
                string label = name;
                if (current.IndistinguishableFrom(captured))
                {
                    label += ", " + "RimWorldAccess.Styling.CurrentColor".Translate();
                    startIndex = i;
                }
                options.Add(new FloatMenuOption(label, () =>
                {
                    StylingStationHelper.SetDesiredSkinColor(dialog, pawn, captured);
                    pawn.Drawer.renderer.SetAllGraphicsDirty();
                    TolkHelper.SpeakData((string)"RimWorldAccess.Styling.ColorSet".Translate(name));
                }));
            }

            if (options.Count == 0) { TolkHelper.Speak("NoneLower".Loc()); return; }
            TolkHelper.SpeakData((string)"RimWorldAccess.Styling.SkinColorTitle".Translate());
            WindowlessFloatMenuState.Open(options, colonistOrders: false, startIndex: startIndex);
        }

        private static string TabLabel(StylingTabKind kind)
        {
            switch (kind)
            {
                case StylingTabKind.Hair: return "Hair".Translate().CapitalizeFirst();
                case StylingTabKind.Beard: return "Beard".Translate().CapitalizeFirst();
                case StylingTabKind.FaceTattoo: return "TattooFace".Translate().CapitalizeFirst();
                case StylingTabKind.BodyTattoo: return "TattooBody".Translate().CapitalizeFirst();
                case StylingTabKind.ApparelColor: return "ApparelColor".Translate().CapitalizeFirst();
                case StylingTabKind.BodyType: return "Body type"; // l10n-exempt: mirrors vanilla's own unlocalized tab label (decompiled Dialog_StylingStation.cs:306)
                case StylingTabKind.HeadType: return "Head type"; // l10n-exempt: mirrors vanilla's own unlocalized tab label (decompiled Dialog_StylingStation.cs:310)
                default: return "";
            }
        }
    }
}
