using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using ExpandedInfo = RimWorld.Dialog_EditIdeoStyleItems.ExpandedInfo;
using ItemType = RimWorld.Dialog_EditIdeoStyleItems.ItemType;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Keyboard focus scope for vanilla's <see cref="Dialog_EditIdeoStyleItems"/>.
    ///
    /// <b>Four tree regions over vanilla's two tabs</b>, one panel per <see cref="ItemType"/>, with
    /// Tab/Shift+Tab standing in for the tab strip. Landing in a region whose tab is not current runs
    /// vanilla's own tab-click delegate body (decompiled :227-236). Each region is a
    /// <see cref="TreeRegionScope"/> tree: category nodes at the top level, the styles they list as
    /// children, so expand/collapse, collapse-from-child, submenu mode and sibling jumps all ride the
    /// shared engine. Rows, values and edits go through the dialog's own private
    /// <c>GetFrequency</c>/<c>GetGender</c>/<c>TryGet*</c>/<c>Change*</c>; nothing here touches
    /// <c>ideo.style</c>, which the dialog commits only in <c>Done</c> (:701-723), so Escape, Cancel
    /// and Reset discard what vanilla discards.
    ///
    /// A category node is a TreeItem (its Value is the aggregate frequency across its listed items, or
    /// a "mixed" word mirroring vanilla's <c>RadioButSemiOn</c>); a style node is a ComboBox whose
    /// Enter opens the frequency picker. Only categories expand, so the "collapsed"/"expanded" state
    /// is the ear's cue that tells the two apart. A gender fragment rides both wherever vanilla returns
    /// a gender (:514-517); <c>TryGetGender</c> reads <see cref="Gender.None"/> both for "uniformly
    /// any" and for "mixed".
    ///
    /// Expansion is stored in the dialog's own <c>ExpandedInfo</c> flags, and each tree node mirrors
    /// its category's flag. <see cref="ReconcileExpansionWithVanilla"/> keeps the two in step every
    /// draw: normally it writes the tree's state onto vanilla so the visuals match what the cursor
    /// walks, but when vanilla's own Expand all / Collapse all buttons (:161-176) moved a flag it
    /// adopts that instead and reflattens on the next refresh.
    ///
    /// Enter opens a value picker; a category's options bulk-apply through vanilla's own per-category
    /// delegate body (:314-332). Left/Right expand/collapse and never change a frequency — a combo box
    /// changes only through its picker (mod-wide ruling). Page Up/Down jump between category headers.
    ///
    /// The Buttons region is vanilla's own five buttons, captured from its draw: Expand all, Collapse
    /// all, then either Back (read-only hosts, :182) or Cancel/Reset/Done (:188-201).
    ///
    /// Escape is left to vanilla, whose <c>closeOnCancel</c> discards exactly what Cancel does. The
    /// inherited <c>OwnsAccept</c> stays true because the dialog's focus-blind <c>OnAcceptKeyPressed</c>
    /// (:204-208) would Done-and-close from under any row; it overrides without calling base, so the
    /// window-stack accept router gates vanilla's sole entry point into it.
    ///
    /// The focus ring rides a recording postfix on the dialog's own two row methods rather than
    /// recomputed geometry; the scroll-follow measures the focused row's Y from vanilla's own full
    /// draw layout, which is the same in submenu mode (vanilla always draws every expanded row).
    /// Vanilla's per-item hover preview is an <c>ImmediateWindow</c> — visual only, nothing to mirror.
    /// </summary>
    public sealed class StyleItemsDialogScope : TreeRegionScope
    {
        /// <summary>Vanilla's own row height for both row kinds (decompiled :80, :288, :351).</summary>
        internal const float RowHeight = 28f;

        /// <summary>The gap vanilla leaves after a category's whole block (decompiled :346).</summary>
        private const float CategoryGap = 4f;

        /// <summary>An item row's left inset inside the section's scroll view (decompiled :351).</summary>
        internal const float ItemRowInset = 17f;

        /// <summary>What one tree node stands for, plus the last expansion value synced to vanilla.</summary>
        private sealed class StyleNode
        {
            public StyleItemCategoryDef Category;
            public StyleItemDef Item;
            public ItemType ItemType;
            public ExpandedInfo Info;
            public bool LastSynced;

            public bool IsCategory
            {
                get { return Item == null; }
            }
        }

        private static readonly ItemType[] ItemTypes = (ItemType[])Enum.GetValues(typeof(ItemType));
        private static readonly StyleItemFrequency[] FrequencyValues =
            (StyleItemFrequency[])Enum.GetValues(typeof(StyleItemFrequency));

        private static readonly AccessTools.FieldRef<Dialog_EditIdeoStyleItems, Ideo> IdeoField =
            AccessTools.FieldRefAccess<Dialog_EditIdeoStyleItems, Ideo>("ideo");
        private static readonly AccessTools.FieldRef<Dialog_EditIdeoStyleItems, StyleItemTab> CurTab =
            AccessTools.FieldRefAccess<Dialog_EditIdeoStyleItems, StyleItemTab>("curTab");
        private static readonly AccessTools.FieldRef<Dialog_EditIdeoStyleItems, IdeoEditMode> EditModeField =
            AccessTools.FieldRefAccess<Dialog_EditIdeoStyleItems, IdeoEditMode>("editMode");
        private static readonly AccessTools.FieldRef<Dialog_EditIdeoStyleItems, List<ExpandedInfo>> ExpandedInfos =
            AccessTools.FieldRefAccess<Dialog_EditIdeoStyleItems, List<ExpandedInfo>>("expandedInfos");
        private static readonly AccessTools.FieldRef<Dialog_EditIdeoStyleItems, Vector2> ScrollLeft =
            AccessTools.FieldRefAccess<Dialog_EditIdeoStyleItems, Vector2>("scrollPositionLeft");
        private static readonly AccessTools.FieldRef<Dialog_EditIdeoStyleItems, Vector2> ScrollRight =
            AccessTools.FieldRefAccess<Dialog_EditIdeoStyleItems, Vector2>("scrollPositionRight");
        private static readonly AccessTools.FieldRef<Dialog_EditIdeoStyleItems, float> ScrollHeightLeft =
            AccessTools.FieldRefAccess<Dialog_EditIdeoStyleItems, float>("scrollViewHeightLeft");
        private static readonly AccessTools.FieldRef<Dialog_EditIdeoStyleItems, float> ScrollHeightRight =
            AccessTools.FieldRefAccess<Dialog_EditIdeoStyleItems, float>("scrollViewHeightRight");

        private static readonly MethodInfo CanListMethod = AccessTools.Method(
            typeof(Dialog_EditIdeoStyleItems), "CanList", new[] { typeof(StyleItemDef), typeof(ItemType) });
        private static readonly MethodInfo SectionLabelMethod = AccessTools.Method(
            typeof(Dialog_EditIdeoStyleItems), "GetSectionLabel");
        private static readonly MethodInfo GetFrequencyMethod = AccessTools.Method(
            typeof(Dialog_EditIdeoStyleItems), "GetFrequency");
        private static readonly MethodInfo GetGenderMethod = AccessTools.Method(
            typeof(Dialog_EditIdeoStyleItems), "GetGender");
        private static readonly MethodInfo TryGetFrequencyMethod = AccessTools.Method(
            typeof(Dialog_EditIdeoStyleItems), "TryGetFrequency");
        private static readonly MethodInfo TryGetGenderMethod = AccessTools.Method(
            typeof(Dialog_EditIdeoStyleItems), "TryGetGender");
        private static readonly MethodInfo ChangeFrequencyMethod = AccessTools.Method(
            typeof(Dialog_EditIdeoStyleItems), "ChangeFrequency");
        private static readonly MethodInfo ChangeGenderMethod = AccessTools.Method(
            typeof(Dialog_EditIdeoStyleItems), "ChangeGender", new[] { typeof(StyleItemDef), typeof(Gender) });

        private readonly Dialog_EditIdeoStyleItems dialog;

        // Bound to this scope's own dialog: every read is vanilla's own answer about its own
        // scratch state.
        private readonly Func<StyleItemDef, ItemType, bool> canList;
        private readonly Func<ItemType, string> sectionLabel;
        private readonly Func<StyleItemDef, StyleItemFrequency> getFrequency;
        private readonly Func<StyleItemDef, Gender?> getGender;
        private readonly Func<StyleItemCategoryDef, ItemType, StyleItemFrequency?> tryGetFrequency;
        private readonly Func<StyleItemCategoryDef, ItemType, Gender> tryGetGender;
        private readonly Action<StyleItemDef, StyleItemFrequency> changeFrequency;
        private readonly Action<StyleItemDef, Gender> changeGender;

        /// <summary>
        /// The dialog's own <c>ExpandedInfo</c> objects by category and item type. It builds them once
        /// in its constructor and never rebuilds the list (decompiled :103-111), so this index and the
        /// tree nodes that reference the same objects stay valid for the scope's life.
        /// </summary>
        private readonly Dictionary<StyleItemCategoryDef, ExpandedInfo[]> infoIndex =
            new Dictionary<StyleItemCategoryDef, ExpandedInfo[]>();

        private readonly TreePanel[] regionPanels;

        /// <summary>Each section's scroll viewport height, read off the live clip stack as its rows draw.</summary>
        private readonly float[] viewportHeights;

        /// <summary>Set when vanilla's own Expand/Collapse all button moved a flag; the next refresh reflattens.</summary>
        private bool pendingExternalReflatten;

        /// <summary>A fresh scope instance per window open, so this lands exactly once.</summary>
        private bool pendingInitialRegion = true;

        public StyleItemsDialogScope(Dialog_EditIdeoStyleItems dialog)
        {
            this.dialog = dialog;
            canList = AccessTools.MethodDelegate<Func<StyleItemDef, ItemType, bool>>(CanListMethod, dialog);
            sectionLabel = AccessTools.MethodDelegate<Func<ItemType, string>>(SectionLabelMethod, dialog);
            getFrequency = AccessTools.MethodDelegate<Func<StyleItemDef, StyleItemFrequency>>(GetFrequencyMethod, dialog);
            getGender = AccessTools.MethodDelegate<Func<StyleItemDef, Gender?>>(GetGenderMethod, dialog);
            tryGetFrequency = AccessTools.MethodDelegate<Func<StyleItemCategoryDef, ItemType, StyleItemFrequency?>>(
                TryGetFrequencyMethod, dialog);
            tryGetGender = AccessTools.MethodDelegate<Func<StyleItemCategoryDef, ItemType, Gender>>(TryGetGenderMethod, dialog);
            changeFrequency = AccessTools.MethodDelegate<Action<StyleItemDef, StyleItemFrequency>>(ChangeFrequencyMethod, dialog);
            changeGender = AccessTools.MethodDelegate<Action<StyleItemDef, Gender>>(ChangeGenderMethod, dialog);

            foreach (ExpandedInfo info in ExpandedInfos(dialog))
            {
                ExpandedInfo[] perType;
                if (!infoIndex.TryGetValue(info.categoryDef, out perType))
                {
                    perType = new ExpandedInfo[ItemTypes.Length];
                    infoIndex[info.categoryDef] = perType;
                }
                perType[(int)info.itemType] = info;
            }

            regionPanels = new TreePanel[ItemTypes.Length];
            viewportHeights = new float[ItemTypes.Length];
            for (int r = 0; r < regionPanels.Length; r++)
            {
                regionPanels[r] = CreatePanel();
                SetTreeRoot(regionPanels[r], BuildRegionTree(ItemTypes[r]));
            }

            Claim(SharedMenuGrammar.Info, OnInfo);
            // Page Up/Down between category headers: the base leaves this pair unclaimed. The '*'
            // toggle-all and Ctrl+Home/End edges ride the base tree grammar.
            Claim("ideoOverlayEditor.jumpToPreviousSection", e => PerformJumpToAdjacentSection(false));
            Claim("ideoOverlayEditor.jumpToNextSection", e => PerformJumpToAdjacentSection(true));
        }

        public override string Name
        {
            get { return "style-items-dialog"; }
        }

        protected internal override Window OwnedWindow
        {
            get { return dialog; }
        }

        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        /// <summary>
        /// Off: the typed regions present every row this window draws, and an extras net over its
        /// hundreds of per-row labels and invisible buttons would bury the content it duplicates.
        /// </summary>
        protected override bool IncludeCapturedExtrasRegion
        {
            get { return false; }
        }

        internal bool Owns(Window window)
        {
            return ReferenceEquals(window, dialog);
        }

        /// <summary>
        /// Reset is the one captured button carrying a tooltip of its own (<c>ResetButtonDesc</c>,
        /// decompiled :192), and a captured row has no tooltip slot, so it folds into the label.
        /// Identified by draw position, never by translated text: the window draws Expand all,
        /// Collapse all, then Cancel/Reset/Done in edit mode (:161-201).
        /// </summary>
        protected override string CapturedButtonLabel(int captureIndex, string rawLabel)
        {
            if (captureIndex != 3 || EditModeField(dialog) == IdeoEditMode.None)
            {
                return rawLabel;
            }
            return rawLabel + ". " + (string)"ResetButtonDesc".Translate();
        }

        // --- Tree construction: categories at the top level, their listed styles as children ---

        private InspectionTreeItem BuildRegionTree(ItemType itemType)
        {
            var root = new InspectionTreeItem();
            foreach (StyleItemCategoryDef category in DefDatabase<StyleItemCategoryDef>.AllDefs)
            {
                ExpandedInfo info = InfoFor(category, itemType);
                if (info == null || !info.any)
                {
                    continue;
                }
                var categoryNode = new InspectionTreeItem
                {
                    Label = category.LabelCap,
                    IndentLevel = 0,
                    IsExpandable = true,
                    IsExpanded = info.expanded,
                    Parent = root,
                    Data = new StyleNode
                    {
                        Category = category,
                        ItemType = itemType,
                        Info = info,
                        LastSynced = info.expanded,
                    },
                };
                root.Children.Add(categoryNode);
                foreach (StyleItemDef item in category.ItemsInCategory)
                {
                    if (!canList(item, itemType))
                    {
                        continue;
                    }
                    categoryNode.Children.Add(new InspectionTreeItem
                    {
                        Label = item.LabelCap,
                        IndentLevel = 1,
                        Parent = categoryNode,
                        Data = new StyleNode { Category = category, Item = item, ItemType = itemType },
                    });
                }
            }
            return root;
        }

        private ExpandedInfo InfoFor(StyleItemCategoryDef category, ItemType itemType)
        {
            ExpandedInfo[] perType;
            return infoIndex.TryGetValue(category, out perType) ? perType[(int)itemType] : null;
        }

        // --- TreeRegionScope wiring: one panel per item-type region ---

        protected override int ContentRegionCount
        {
            get { return ItemTypes.Length; }
        }

        protected override string TreeRegionLabel
        {
            get { return sectionLabel(ItemType.Hair).CapitalizeFirst(); }
        }

        protected override string ContentRegionName(int region)
        {
            return sectionLabel(ItemTypes[region]).CapitalizeFirst();
        }

        protected override TreePanel PanelFor(int region)
        {
            return region >= 0 && region < regionPanels.Length ? regionPanels[region] : null;
        }

        /// <summary>Rebuilding is unnecessary — the row set is fixed and every value is read live — but a vanilla-button expansion change queues a reflatten here.</summary>
        protected override void RefreshContent()
        {
            base.RefreshContent();
            if (pendingExternalReflatten)
            {
                pendingExternalReflatten = false;
                for (int r = 0; r < regionPanels.Length; r++)
                {
                    regionPanels[r].Tree.Reflatten();
                }
            }
        }

        protected override ElementDescription DescribeTreeNode(InspectionTreeItem item)
        {
            var node = item.Data as StyleNode;
            if (node == null)
            {
                return new ElementDescription();
            }
            ItemType itemType = node.ItemType;

            if (node.IsCategory)
            {
                var d = new ElementDescription
                {
                    Label = node.Category.LabelCap,
                    Role = ElementRole.TreeItem,
                    Expanded = item.IsExpanded,
                };
                StyleItemFrequency? freq = tryGetFrequency(node.Category, itemType);
                d.Value = freq.HasValue
                    ? freq.Value.GetLabel().CapitalizeFirst()
                    : (string)"RimWorldAccess.Ideology.Builder.Appearance.MixedFrequency".Translate();
                if (itemType != ItemType.Beard)
                {
                    d.Extras = (string)"RimWorldAccess.Ideology.Builder.Appearance.GenderFragment".Translate(
                        GenderLabel(tryGetGender(node.Category, itemType)));
                }
                return d;
            }

            var itemDesc = new ElementDescription
            {
                Label = node.Item.LabelCap,
                Role = ElementRole.ComboBox,
                Value = getFrequency(node.Item).GetLabel().CapitalizeFirst(),
            };
            var extras = new List<string>();
            Gender? gender = getGender(node.Item);
            if (gender.HasValue)
            {
                extras.Add((string)"RimWorldAccess.Ideology.Builder.Appearance.GenderFragment".Translate(
                    GenderLabel(gender.Value)));
            }
            string description = StyleDescriptionHelper.Describe(node.Item);
            if (!string.IsNullOrEmpty(description))
            {
                extras.Add(description);
            }
            itemDesc.Extras = string.Join(". ", extras);
            return itemDesc;
        }

        protected override void ActivateTreeNode(InspectionTreeItem item)
        {
            var node = item.Data as StyleNode;
            if (node == null)
            {
                return;
            }
            if (node.IsCategory)
            {
                OpenCategoryValuePicker(node.Category, node.ItemType);
            }
            else
            {
                OpenItemValuePicker(node.Item);
            }
        }

        /// <summary>Page Up/Down jump between category headers.</summary>
        protected override bool IsSectionBoundary(InspectionTreeItem item)
        {
            return item.IndentLevel == 0;
        }

        /// <summary>Vanilla's own label for a gender column (its <c>DrawConfigInfo</c> local function, decompiled :478-485).</summary>
        private static string GenderLabel(Gender gender)
        {
            string label = gender == Gender.None ? (string)"MaleAndFemale".Translate() : gender.GetLabel();
            return label.CapitalizeFirst();
        }

        // --- Value pickers: frequency options plus an optional gender submenu. Every mutation
        // invokes the dialog's own private mutator, so the edit lands in the same scratch DefMap
        // vanilla's radio button writes and commits only through Done. ---

        private void OpenItemValuePicker(StyleItemDef item)
        {
            StyleItemFrequency current = getFrequency(item);
            var options = new List<FloatMenuOption>();
            foreach (StyleItemFrequency freq in FrequencyValues)
            {
                StyleItemFrequency captured = freq;
                string label = freq.GetLabel().CapitalizeFirst();
                if (freq == current) label += ". " + "RimWorldAccess.Ideology.Builder.PreceptCurrent".Translate();
                // MUTATION-C: invokes Dialog_EditIdeoStyleItems.ChangeFrequency — private, no public vehicle.
                options.Add(new FloatMenuOption(label, () => changeFrequency(item, captured)));
            }
            if (getGender(item).HasValue)
            {
                options.Add(new FloatMenuOption((string)"Gender".Translate() + "...", () => OpenItemGenderPicker(item)));
            }
            TolkHelper.SpeakData(item.LabelCap);
            WindowlessFloatMenuState.Open(options, colonistOrders: false, announceSelection: false);
        }

        private void OpenItemGenderPicker(StyleItemDef item)
        {
            Gender current = getGender(item).GetValueOrDefault();
            var options = new List<FloatMenuOption>
            {
                // MUTATION-C: invokes Dialog_EditIdeoStyleItems.ChangeGender — private, no public vehicle.
                GenderOption(Gender.Male, current, g => changeGender(item, g)),
                GenderOption(Gender.Female, current, g => changeGender(item, g)),
                GenderOption(Gender.None, current, g => changeGender(item, g)),
            };
            WindowlessFloatMenuState.Open(options, colonistOrders: false, titleText: (string)"Gender".Translate(), announceSelection: false);
        }

        private void OpenCategoryValuePicker(StyleItemCategoryDef category, ItemType itemType)
        {
            StyleItemFrequency? current = tryGetFrequency(category, itemType);
            var options = new List<FloatMenuOption>();
            foreach (StyleItemFrequency freq in FrequencyValues)
            {
                StyleItemFrequency captured = freq;
                string label = freq.GetLabel().CapitalizeFirst();
                if (current.HasValue && freq == current.Value) label += ". " + "RimWorldAccess.Ideology.Builder.PreceptCurrent".Translate();
                options.Add(new FloatMenuOption(label, () => SetCategoryFrequency(category, itemType, captured)));
            }
            if (itemType != ItemType.Beard)
            {
                options.Add(new FloatMenuOption((string)"Gender".Translate() + "...", () => OpenCategoryGenderPicker(category, itemType)));
            }
            TolkHelper.SpeakData(category.LabelCap);
            WindowlessFloatMenuState.Open(options, colonistOrders: false, announceSelection: false);
        }

        private void OpenCategoryGenderPicker(StyleItemCategoryDef category, ItemType itemType)
        {
            Gender current = tryGetGender(category, itemType);
            var options = new List<FloatMenuOption>
            {
                GenderOption(Gender.Male, current, g => SetCategoryGender(category, itemType, g)),
                GenderOption(Gender.Female, current, g => SetCategoryGender(category, itemType, g)),
                GenderOption(Gender.None, current, g => SetCategoryGender(category, itemType, g)),
            };
            WindowlessFloatMenuState.Open(options, colonistOrders: false, titleText: (string)"Gender".Translate(), announceSelection: false);
        }

        private static FloatMenuOption GenderOption(Gender gender, Gender current, Action<Gender> apply)
        {
            string label = GenderLabel(gender);
            if (gender == current)
            {
                label += ". " + "RimWorldAccess.Ideology.Builder.PreceptCurrent".Translate();
            }
            return new FloatMenuOption(label, () => apply(gender));
        }

        /// <summary>The body of vanilla's own bulk-frequency delegate for a category row (decompiled :323-332).</summary>
        private void SetCategoryFrequency(StyleItemCategoryDef category, ItemType itemType, StyleItemFrequency freq)
        {
            foreach (StyleItemDef item in category.ItemsInCategory)
            {
                if (canList(item, itemType))
                {
                    // MUTATION-C: invokes Dialog_EditIdeoStyleItems.ChangeFrequency — private, no public vehicle.
                    changeFrequency(item, freq);
                }
            }
        }

        /// <summary>The body of vanilla's own bulk-gender delegate for a category row (decompiled :314-322).</summary>
        private void SetCategoryGender(StyleItemCategoryDef category, ItemType itemType, Gender gender)
        {
            foreach (StyleItemDef item in category.ItemsInCategory)
            {
                if (canList(item, itemType))
                {
                    // MUTATION-C: invokes Dialog_EditIdeoStyleItems.ChangeGender — private, no public vehicle.
                    changeGender(item, gender);
                }
            }
        }

        // --- Info card drill-in. Vanilla never opens Dialog_InfoCard for a StyleItemDef, so
        // Alt+I refuses rather than fabricate a card; the row already reads the full
        // description. ---

        private void OnInfo(KeyEventSnapshot e)
        {
            RefreshModel();
            InfoCardState.SpeakNoInfoCardAvailable();
        }

        // --- Expansion sync, tab sync, scroll follow and the focus ring ---

        /// <summary>
        /// Reconciles each category node with the dialog's own <c>ExpandedInfo</c> flag once per draw.
        /// A flag vanilla itself moved (its Expand all / Collapse all buttons) is adopted and queues a
        /// reflatten; otherwise the tree's state is written onto the flag so the visuals match.
        /// </summary>
        internal void ReconcileExpansionWithVanilla()
        {
            for (int r = 0; r < regionPanels.Length; r++)
            {
                InspectionTreeItem root = regionPanels[r].Tree.Root;
                if (root == null)
                {
                    continue;
                }
                foreach (InspectionTreeItem categoryNode in root.Children)
                {
                    var node = categoryNode.Data as StyleNode;
                    if (node == null || node.Info == null)
                    {
                        continue;
                    }
                    if (node.Info.expanded != node.LastSynced)
                    {
                        categoryNode.IsExpanded = node.Info.expanded;
                        pendingExternalReflatten = true;
                    }
                    else
                    {
                        node.Info.expanded = categoryNode.IsExpanded;
                    }
                    node.LastSynced = categoryNode.IsExpanded;
                }
            }
        }

        /// <summary>
        /// Keeps the VISIBLE tab on the cursor's region by running vanilla's own tab-click delegate
        /// body (decompiled :227-236). Silent and idempotent, as this hook requires.
        /// </summary>
        protected override void OnCursorSettled(int region, int index)
        {
            base.OnCursorSettled(region, index);
            if (region < 0 || region >= regionPanels.Length)
            {
                return;
            }
            StyleItemTab tab = TabFor(ItemTypes[region]);
            if (CurTab(dialog) == tab)
            {
                return;
            }
            CurTab(dialog) = tab;
            ScrollLeft(dialog) = ScrollRight(dialog) = Vector2.zero;
        }

        private static StyleItemTab TabFor(ItemType itemType)
        {
            return itemType == ItemType.Hair || itemType == ItemType.Beard
                ? StyleItemTab.HairAndBeard
                : StyleItemTab.Tattoo;
        }

        /// <summary>Hair and face tattoos draw in the left section, beards and body tattoos in the right (decompiled :215-217).</summary>
        private static bool IsLeftSection(ItemType itemType)
        {
            return itemType == ItemType.Hair || itemType == ItemType.FaceTattoo;
        }

        /// <summary>
        /// The focused node's Y over vanilla's own full draw layout (header, a row per listed item
        /// while expanded, then a gap; decompiled :286-347) — the same layout submenu mode draws.
        /// </summary>
        private float FocusedRowY(int region)
        {
            var target = CurrentTreeItem()?.Data as StyleNode;
            if (target == null)
            {
                return 0f;
            }
            ItemType itemType = ItemTypes[region];
            float y = 0f;
            foreach (StyleItemCategoryDef category in DefDatabase<StyleItemCategoryDef>.AllDefs)
            {
                ExpandedInfo info = InfoFor(category, itemType);
                if (info == null || !info.any)
                {
                    continue;
                }
                if (target.IsCategory && target.Category == category)
                {
                    return y;
                }
                y += RowHeight;
                if (info.expanded)
                {
                    foreach (StyleItemDef item in category.ItemsInCategory)
                    {
                        if (!canList(item, itemType))
                        {
                            continue;
                        }
                        if (!target.IsCategory && target.Item == item)
                        {
                            return y;
                        }
                        y += RowHeight;
                    }
                }
                y += CategoryGap;
            }
            return y;
        }

        internal void NoteSectionViewport(ItemType itemType, float height)
        {
            viewportHeights[(int)itemType] = height;
        }

        /// <summary>
        /// Scrolls the focused row into its section's viewport before the dialog draws it, through the
        /// same fields vanilla's own scroll bars write. No-op until that section has drawn once.
        /// </summary>
        internal void ScrollFocusedRowIntoView()
        {
            int region = Model.RegionIndex;
            if (region < 0 || region >= regionPanels.Length || CurrentTreeItem() == null)
            {
                return;
            }
            float viewport = viewportHeights[region];
            if (viewport <= 0f)
            {
                return;
            }
            bool left = IsLeftSection(ItemTypes[region]);
            Vector2 scroll = left ? ScrollLeft(dialog) : ScrollRight(dialog);
            float y = FocusedRowY(region);
            float top = scroll.y;
            if (y < top)
            {
                top = y;
            }
            else if (y + RowHeight > top + viewport)
            {
                top = y + RowHeight - viewport;
            }
            float content = left ? ScrollHeightLeft(dialog) : ScrollHeightRight(dialog);
            top = Mathf.Clamp(top, 0f, Mathf.Max(0f, content - viewport));
            if (top == scroll.y)
            {
                return;
            }
            if (left)
            {
                ScrollLeft(dialog) = new Vector2(scroll.x, top);
            }
            else
            {
                ScrollRight(dialog) = new Vector2(scroll.x, top);
            }
        }

        protected internal override Rect FocusedContentRect()
        {
            int region = Model.RegionIndex;
            if (region < 0 || region >= regionPanels.Length)
            {
                return default(Rect);
            }
            var node = CurrentTreeItem()?.Data as StyleNode;
            if (node == null)
            {
                return default(Rect);
            }
            // A category's row identity is its ExpandedInfo, a style's is its def — what vanilla's own
            // row postfixes record, and what tells one category apart in the two side-by-side sections.
            object identity = node.IsCategory ? (object)node.Info : node.Item;
            return StyleItemRowDrawPatch.Rows.FindLast(identity);
        }

        // --- Lifecycle ---

        public override void OnPush()
        {
            base.OnPush();
            StyleItemRowDrawPatch.Live = this;
        }

        public override void OnPop()
        {
            if (ReferenceEquals(StyleItemRowDrawPatch.Live, this))
            {
                StyleItemRowDrawPatch.Live = null;
            }
            base.OnPop();
        }

        /// <summary>Land in the tab the window opened on; the "Tattoos" box must not flip back to hair.</summary>
        public override void OnFocus()
        {
            if (pendingInitialRegion)
            {
                pendingInitialRegion = false;
                RefreshModel();
                Model.MoveToRegion(CurTab(dialog) == StyleItemTab.HairAndBeard ? (int)ItemType.Hair : (int)ItemType.FaceTattoo);
                ListModel region = Model.CurrentRegion;
                if (region != null && !region.IsEmpty)
                {
                    region.MoveFirst();
                }
            }
            base.OnFocus();
        }

        protected override string ComposeOpenAnnouncement()
        {
            return (string)"EditAppearanceItems".Translate() + ". "
                + IdeoBuilderHelper.AppearanceSummary(IdeoField(dialog));
        }
    }

    /// <summary>
    /// Records (row identity, rect) for every row <see cref="Dialog_EditIdeoStyleItems"/> draws, so
    /// the focus ring rides vanilla's own geometry rather than a recomputed copy.
    /// A category row's identity is its <see cref="ExpandedInfo"/> — the dialog builds exactly one
    /// per (category, item type) pair (decompiled :104-111), which is what tells the same category
    /// apart in the two sections drawn side by side; an item row's identity is its own def.
    /// Both postfixes are gated on the one live scope. The rows draw inside the section's scroll
    /// view, so their clip band is the viewport the scroll-follow measures against.
    /// </summary>
    internal static class StyleItemRowDrawPatch
    {
        internal static readonly RowDrawCapture Rows = new RowDrawCapture();

        /// <summary>The scope driving the dialog now, or null. Vanilla opens this window one at a time.</summary>
        internal static StyleItemsDialogScope Live;

        internal static void Record(Dialog_EditIdeoStyleItems dialog, object identity, Rect rowRect)
        {
            try
            {
                StyleItemsDialogScope scope = Live;
                if (scope == null || !scope.Owns(dialog))
                {
                    return;
                }
                Rows.Record(identity, rowRect);
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Style item row capture error", ex);
            }
        }
    }

    /// <summary>Records each category header row, and the section viewport its rows are clipped to.</summary>
    [HarmonyPatch(typeof(Dialog_EditIdeoStyleItems), "ListStyleItemCategory")]
    internal static class StyleItemCategoryRowPatch
    {
        [HarmonyPrefix]
        public static void Prefix(ref float curY, out float __state)
        {
            __state = curY;
        }

        [HarmonyPostfix]
        public static void Postfix(Dialog_EditIdeoStyleItems __instance, Rect viewRect, ExpandedInfo expandedInfo,
            ItemType itemType, float __state)
        {
            try
            {
                StyleItemsDialogScope scope = StyleItemRowDrawPatch.Live;
                if (scope != null && scope.Owns(__instance))
                {
                    scope.NoteSectionViewport(itemType, GuiSpace.CurrentClip().VisibleRect.height);
                }
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Style item category row capture error", ex);
            }
            StyleItemRowDrawPatch.Record(__instance, expandedInfo,
                new Rect(viewRect.x, viewRect.y + __state, viewRect.width, StyleItemsDialogScope.RowHeight));
        }
    }

    /// <summary>Records each style-item row.</summary>
    [HarmonyPatch(typeof(Dialog_EditIdeoStyleItems), "ListStyleItem")]
    internal static class StyleItemRowPatch
    {
        [HarmonyPrefix]
        public static void Prefix(ref float curY, out float __state)
        {
            __state = curY;
        }

        [HarmonyPostfix]
        public static void Postfix(Dialog_EditIdeoStyleItems __instance, StyleItemDef styleItem, Rect viewRect, float __state)
        {
            StyleItemRowDrawPatch.Record(__instance, styleItem, new Rect(
                viewRect.x + StyleItemsDialogScope.ItemRowInset,
                viewRect.y + __state,
                viewRect.width - StyleItemsDialogScope.ItemRowInset,
                StyleItemsDialogScope.RowHeight));
        }
    }

    /// <summary>
    /// Syncs tree expansion onto vanilla's flags and applies the scroll follow before the sections
    /// draw. No accept-poll mask is needed: this window polls no raw key, and its
    /// <c>OnAcceptKeyPressed</c> override is already gated by the shell's window-stack accept router.
    /// </summary>
    [HarmonyPatch(typeof(Dialog_EditIdeoStyleItems), "DoWindowContents")]
    internal static class StyleItemsDialogDrawPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Dialog_EditIdeoStyleItems __instance)
        {
            try
            {
                StyleItemsDialogScope scope = StyleItemRowDrawPatch.Live;
                if (scope != null && scope.Owns(__instance))
                {
                    scope.ReconcileExpansionWithVanilla();
                    scope.ScrollFocusedRowIntoView();
                }
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Style items dialog draw pass error", ex);
            }
        }
    }
}
