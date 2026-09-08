using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The keyboard focus scope for the Character Editor mod's <c>DialogCapsuleUI</c>, registered
    /// through <see cref="ScopeForWindow.Register"/> in <see cref="CharEditorDialogCompat"/> and
    /// gated on <see cref="RimWorldAccess.CharEditorCapsuleCompat.Ready"/>.
    /// All reflection lives in that facade; this scope only calls its typed methods.
    ///
    /// CAPSULE VIEW ONLY: a compact <see cref="TreeRegionScope"/> presenting the three scenario-part
    /// containers as tree sections (own things, scattered map things, animals). The Interstellar
    /// view is out of scope, and the one optional prefix row says so rather than leaving a silent
    /// dead end; it appears in-game only, because the mod itself draws the toggle button nowhere in
    /// world generation.
    ///
    /// Each thing row gets Delete, widening the existing <c>charEditor.removeTrait</c> claim rather
    /// than minting an id, and — for the Own and Scattered containers only, Animals having no shift
    /// in the mod's own UI — Alt+I opens a one-option menu shifting the thing to the other
    /// container, mirroring the mod's own per-row move buttons.
    ///
    /// Add needs no wiring beyond opening the mod's own DialogObjects: <see cref="CharEditorBrowserScope"/>
    /// already routes Confirm to <c>mCapsuleUI.ExternalAddThing</c> when the dialog was opened with
    /// a capsule target, so the return only needs a silent tree rebuild.
    ///
    /// Escape already works: the dialog sets <c>closeOnCancel = true</c> and never overrides
    /// <c>OnCancelKeyPressed</c>, so vanilla closes it and no
    /// <see cref="ScreenScope.OwnsCancel"/> override is needed.
    /// </summary>
    internal sealed class CharEditorCapsuleScope : TreeRegionScope
    {
        private enum SectionKind
        {
            Taken,
            Scatter,
            Animal,
        }

        private sealed class CapsuleThingRow
        {
            public readonly ScenPart Part;
            public readonly SectionKind Section;

            public CapsuleThingRow(ScenPart part, SectionKind section)
            {
                Part = part;
                Section = section;
            }
        }

        private const int InterstellarWarningRow = 0;

        private readonly Window dialog;
        private readonly List<ScreenAction> actions = new List<ScreenAction>();

        private bool announcedOpen;

        public CharEditorCapsuleScope(Window dialog)
        {
            this.dialog = dialog;

            // Delete widens the same claim CharacterEditorScope's own list rows share: one
            // Delete-key removal action for a focused row.
            Claim("charEditor.removeTrait", e => PerformDeleteFocusedThing(), when: OnCapsuleThingRow);
            // Alt+I: the single "Shift to..." option for Own/Scattered rows.
            Claim(SharedMenuGrammar.Info, e => PerformShiftDrillIn(), when: OnShiftableThingRow);
            // The shared tree.* Page Up/Down ids: three sections are worth jumping between directly.
            Claim("tree.jumpToPreviousSection", e => PerformJumpToAdjacentSection(false));
            Claim("tree.jumpToNextSection", e => PerformJumpToAdjacentSection(true));

            Tree.SkipRoot = true;

            RebuildTree();
            RefreshModel();
        }

        public override string Name => "char-editor-capsule";

        protected internal override Window OwnedWindow => dialog;

        /// <summary>Part labels are worth searching by name, matching every sibling tree screen.</summary>
        protected override bool EnableTypeahead => true;

        /// <summary>The dialog's icon buttons are not Widgets.ButtonText, so blanket capture would only duplicate the declared actions below.</summary>
        protected override bool CaptureWindowButtons => false;

        protected override string TreeRegionLabel => "RimWorldAccess.CharEd.Capsule.Title".Translate().ToString();

        public override void OnFocus()
        {
            base.OnFocus();
            if (announcedOpen)
            {
                // Returning from Add or a Shift/Save/Load picker: the container lists may have
                // changed under the tree, so rebuild silently.
                RebuildTree();
                RefreshModel();
                return;
            }
            announcedOpen = true;
            TolkHelper.SpeakData("RimWorldAccess.CharEd.Capsule.Title".Translate().ToString());
            AnnounceCurrentItem();
        }

        // Prefix row: the Interstellar-view warning, in-game only.

        protected override int PrefixRowCount =>
            CharEditorCompat.InStartingScreen ? 0 : 1;

        protected override ElementDescription DescribePrefixRow(int index)
        {
            var d = new ElementDescription();
            if (index != InterstellarWarningRow)
            {
                return d;
            }
            d.Label = "RimWorldAccess.CharEd.Capsule.InterstellarView".Translate();
            d.Role = ElementRole.Button;
            d.Disabled = true;
            d.Extras = "RimWorldAccess.CharEd.Capsule.InterstellarNotSupported".Translate();
            return d;
        }

        protected override bool CanAdjustPrefixRow(int index) => false;

        protected override void AdjustPrefixRow(int index, int direction)
        {
        }

        protected override void ActivatePrefixRow(int index)
        {
            SoundDefOf.ClickReject.PlayOneShotOnCamera();
        }

        // Tree: three container sections, rebuilt whole — the lists are small enough that an eager
        // rebuild beats the main editor screen's lazy-per-section machinery.

        private void RebuildTree()
        {
            var root = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Category,
                Label = TreeRegionLabel,
                IsExpandable = true,
                IndentLevel = 0,
            };

            AddContainerSection(root, SectionKind.Taken,
                "RimWorldAccess.CharEd.Capsule.OwnThingsSection".Translate(),
                CharEditorCapsuleCompat.Container(dialog, CharEditorCapsuleCompat.ContainerKind.Taken));
            AddContainerSection(root, SectionKind.Scatter,
                "RimWorldAccess.CharEd.Capsule.ScatteredSection".Translate(),
                CharEditorCapsuleCompat.Container(dialog, CharEditorCapsuleCompat.ContainerKind.Scatter));
            AddContainerSection(root, SectionKind.Animal,
                "RimWorldAccess.CharEd.Capsule.AnimalsSection".Translate(),
                CharEditorCapsuleCompat.Container(dialog, CharEditorCapsuleCompat.ContainerKind.Animal));

            SetTreeRoot(root);
        }

        private void AddContainerSection(InspectionTreeItem root, SectionKind kind, string label, List<ScenPart> parts)
        {
            var section = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.SubCategory,
                Label = label,
                IndentLevel = root.IndentLevel + 1,
                IsExpandable = true,
                IsExpanded = false,
                IsSectionHeader = true,
                AutoExpandForSearch = true,
                Parent = root,
            };
            root.Children.Add(section);

            if (parts == null || parts.Count == 0)
            {
                InspectNodeFactory.DetailLine(section, "RimWorldAccess.CharEd.Capsule.ContainerEmpty".Translate());
            }
            else
            {
                foreach (ScenPart part in parts)
                {
                    if (part == null)
                    {
                        continue;
                    }
                    var item = new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.Item,
                        Label = CharEditorCapsuleCompat.PartLabel(part),
                        IndentLevel = section.IndentLevel + 1,
                        IsExpandable = false,
                        Data = new CapsuleThingRow(part, kind),
                        Parent = section,
                    };
                    section.Children.Add(item);
                }
            }

            // No count fold: the label stays the bare name in both states, and DescribeTreeNode
            // speaks a content summary instead, computed fresh off Children so it cannot go stale.
        }

        protected override bool ShouldAutoExpandForSearch(InspectionTreeItem item) =>
            item != null && item.AutoExpandForSearch;

        /// <summary>Same three clauses as <see cref="CharacterEditorScope.IsSectionBoundary"/>; the SubmenuMode clause is needed because an expanded section's header can vanish from the visible list under that setting.</summary>
        protected override bool IsSectionBoundary(InspectionTreeItem item)
        {
            if (item == null)
            {
                return false;
            }
            if (item.IsSectionHeader || item.Type == InspectionTreeItem.ItemType.SubCategory)
            {
                return true;
            }
            if (!Tree.SubmenuMode)
            {
                return false;
            }
            InspectionTreeItem parent = item.Parent;
            return parent != null
                && (parent.IsSectionHeader || parent.Type == InspectionTreeItem.ItemType.SubCategory)
                && parent.IsExpanded
                && parent.Children.Count > 0
                && ReferenceEquals(parent.Children[0], item);
        }

        // Tree rows.

        protected override ElementDescription DescribeTreeNode(InspectionTreeItem item)
        {
            var d = new ElementDescription();
            if (item == null)
            {
                return d;
            }
            if (item.Data is CapsuleThingRow)
            {
                // Clicking a row in the mod's own list only toggles a visual selection; Delete and
                // Alt+I carry the real actions, so Enter re-confirms what is focused. A plain Button
                // role, not the ReadOnly leaf fallback: this row does support mutation.
                d.Label = item.Label;
                d.Role = ElementRole.Button;
                return d;
            }
            d.Label = item.IsExpanded && !item.ExpandedLabel.NullOrEmpty() ? item.ExpandedLabel : item.Label;
            if (item.IsExpandable)
            {
                d.Role = ElementRole.TreeItem;
                d.Expanded = item.IsExpanded;
                if (!item.IsExpanded)
                {
                    d.Extras = SummarizeContainerSection(item);
                }
            }
            else
            {
                d.ReadOnly = true;
            }
            return d;
        }

        /// <summary>
        /// The collapsed container's thing names, up to five with "and N more" beyond, computed
        /// fresh off <paramref name="section"/>'s live children every call.
        /// </summary>
        private static string SummarizeContainerSection(InspectionTreeItem section)
        {
            var labels = new List<string>();
            foreach (InspectionTreeItem child in section.Children)
            {
                if (child.Data is CapsuleThingRow)
                {
                    labels.Add(child.Label);
                }
            }
            if (labels.Count == 0)
            {
                return "RimWorldAccess.CharEd.SummaryEmpty".Translate();
            }
            const int cap = 5;
            if (labels.Count <= cap)
            {
                return string.Join(", ", labels);
            }
            var shown = new List<string>();
            for (int i = 0; i < cap; i++)
            {
                shown.Add(labels[i]);
            }
            return string.Join(", ", shown) + ", "
                + "RimWorldAccess.CharEd.SummaryMore".Translate(labels.Count - cap).ToString();
        }

        protected override void OnBeforeExpandNode(InspectionTreeItem item)
        {
            // RebuildTree builds every section eagerly, so there is nothing to populate lazily.
        }

        protected override void ActivateTreeNode(InspectionTreeItem item)
        {
            if (item == null)
            {
                return;
            }
            if (item.Data is CapsuleThingRow)
            {
                AnnounceCurrentItem();
                return;
            }
            if (item.IsExpandable)
            {
                PerformActivateExpand();
                return;
            }
            AnnounceCurrentItem();
        }

        /// <summary>The compact-scope expand-on-Enter shape; the base's PerformExpand is private.</summary>
        private void PerformActivateExpand()
        {
            TreeActionResult<InspectionTreeItem> result = Tree.ExpandOrDrillDown();
            switch (result.Kind)
            {
                case TreeActionKind.Rejected:
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    break;
                case TreeActionKind.Expanded:
                case TreeActionKind.ExpandedSubmenu:
                    SoundDefOf.FloatMenu_Open.PlayOneShotOnCamera();
                    SyncRegionFromCurrentTree();
                    AnnounceCurrentItem();
                    break;
                case TreeActionKind.DrilledToChild:
                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    SyncRegionFromCurrentTree();
                    AnnounceCurrentItem();
                    break;
            }
        }

        // Delete: remove the focused thing (MUTATION-C -- see CharEditorCapsuleCompat's remarks).

        private bool OnCapsuleThingRow()
        {
            RefreshModel();
            return CurrentTreeItem()?.Data is CapsuleThingRow;
        }

        private bool OnShiftableThingRow()
        {
            RefreshModel();
            return CurrentTreeItem()?.Data is CapsuleThingRow row
                && (row.Section == SectionKind.Taken || row.Section == SectionKind.Scatter);
        }

        private void PerformDeleteFocusedThing()
        {
            InspectionTreeItem item = CurrentTreeItem();
            if (!(item?.Data is CapsuleThingRow row))
            {
                return;
            }
            switch (row.Section)
            {
                case SectionKind.Taken:
                    CharEditorCapsuleCompat.RemoveFromTaken(row.Part);
                    break;
                case SectionKind.Scatter:
                    CharEditorCapsuleCompat.RemoveFromScatter(row.Part);
                    break;
                case SectionKind.Animal:
                    CharEditorCapsuleCompat.RemoveFromAnimals(row.Part);
                    break;
            }
            SoundDefOf.Click.PlayOneShotOnCamera();
            RebuildTree();
            RefreshModel();
            TolkHelper.SpeakData("RimWorldAccess.CharEd.Removed".Translate().ToString());
        }

        /// <summary>Alt+I: the ONE shift option for the row's current container, mirroring the mod's own single-direction per-row button.</summary>
        private void PerformShiftDrillIn()
        {
            InspectionTreeItem item = CurrentTreeItem();
            if (!(item?.Data is CapsuleThingRow row)
                || (row.Section != SectionKind.Taken && row.Section != SectionKind.Scatter))
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }
            bool toScatter = row.Section == SectionKind.Taken;
            string optionLabel = (toScatter
                ? "RimWorldAccess.CharEd.Capsule.ShiftToScattered"
                : "RimWorldAccess.CharEd.Capsule.ShiftToOwnThings").Translate().ToString();
            ScenPart part = row.Part;
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption(optionLabel, delegate
                {
                    if (toScatter)
                    {
                        CharEditorCapsuleCompat.ShiftToScatter(dialog, part);
                    }
                    else
                    {
                        CharEditorCapsuleCompat.ShiftToTaken(dialog, part);
                    }
                    RebuildTree();
                    RefreshModel();
                    TolkHelper.SpeakData("RimWorldAccess.CharEd.Capsule.ThingShifted".Translate().ToString());
                }),
            };
            WindowlessFloatMenuState.OpenTitled(item.Label, options);
        }

        // Buttons region: Add (per container), capsule slots, Close.

        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                actions.Clear();
                actions.Add(new ScreenAction("RimWorldAccess.CharEd.Capsule.AddOwnThing".Translate(), PerformAddOwnThing));
                actions.Add(new ScreenAction("RimWorldAccess.CharEd.Capsule.AddScatteredThing".Translate(), PerformAddScatteredThing));
                actions.Add(new ScreenAction("RimWorldAccess.CharEd.Capsule.AddAnimal".Translate(), PerformAddAnimal));
                actions.Add(new ScreenAction("RimWorldAccess.CharEd.Actions.SaveToSlot".Translate(), OpenSaveSlotPicker));
                actions.Add(new ScreenAction("RimWorldAccess.CharEd.Actions.LoadFromSlot".Translate(), OpenLoadSlotPicker));
                actions.Add(new ScreenAction("CloseButton".Translate(), () => dialog.Close()));
                return actions;
            }
        }

        private void PerformAddOwnThing() => CharEditorCapsuleCompat.AddOwnThing(dialog);
        private void PerformAddScatteredThing() => CharEditorCapsuleCompat.AddScatteredThing(dialog);
        private void PerformAddAnimal() => CharEditorCapsuleCompat.AddAnimal(dialog);

        // Capsule slots: the main editor's submenu-over-numbered-slots shape, but with no occupancy
        // filter or overwrite confirm — the mod's own AOnSaveSlot/AOnLoadSlot carry neither, unlike
        // ASavePawn's overwrite prompt for pawn slots.

        private void OpenSaveSlotPicker()
        {
            OpenSlotPicker(isSave: true, "RimWorldAccess.CharEd.Actions.SaveToSlot".Translate());
        }

        private void OpenLoadSlotPicker()
        {
            OpenSlotPicker(isSave: false, "RimWorldAccess.CharEd.Actions.LoadFromSlot".Translate());
        }

        private void OpenSlotPicker(bool isSave, string title)
        {
            int total = CharEditorCapsuleCompat.NumCapsuleSlots;
            var options = new List<FloatMenuOption>();
            for (int i = 0; i < total; i++)
            {
                int slot = i;
                string stored = CharEditorCapsuleCompat.SlotStoredText(dialog, slot);
                string label = "RimWorldAccess.CharEd.Capsule.SlotLabel".Translate(slot + 1,
                    stored.NullOrEmpty()
                        ? "RimWorldAccess.CharEd.Capsule.SlotEmpty".Translate().ToString()
                        : stored).ToString();
                options.Add(new FloatMenuOption(label, delegate
                {
                    if (isSave)
                    {
                        CharEditorCapsuleCompat.Save(dialog, slot);
                        TolkHelper.SpeakData("RimWorldAccess.CharEd.Saved".Translate().ToString());
                    }
                    else
                    {
                        CharEditorCapsuleCompat.Load(dialog, slot);
                        RebuildTree();
                        RefreshModel();
                        TolkHelper.SpeakData("RimWorldAccess.CharEd.Capsule.SlotLoaded".Translate().ToString());
                    }
                }));
            }
            WindowlessFloatMenuState.OpenTitled(title, options);
        }
    }
}
