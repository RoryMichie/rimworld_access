using System.Collections;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The keyboard focus scope for ISEKAI RPG LEVELING's Window_RunicStation. Three regions
    /// mirroring the window's panels: the equipment list (Enter selects), the selected item's
    /// rune slots (Enter selects a slot; a filled selected slot arms the Remove action, which
    /// rides the mod's own confirmation dialog and the comp's RemoveRuneAt), and the runes on
    /// the map that fit the item (Enter applies through the comp's gated TryAddRune, consuming
    /// the item as the window does). In god mode with no rune items on the map the window lists
    /// every compatible rune def with a rank picker; that becomes stepper rows here.
    /// </summary>
    internal sealed class IsekaiRunicStationScope : ScreenScope
    {
        private const int ListRegion = 0;
        private const int SlotsRegion = 1;
        private const int RunesRegion = 2;

        private sealed class RuneRow
        {
            public Def Rune;
            public int Rank;
            public Thing Item;
        }

        private readonly Window window;
        private readonly List<Thing> items = new List<Thing>();
        private readonly List<string> slotLines = new List<string>();
        private readonly List<RuneRow> runes = new List<RuneRow>();
        private readonly List<ScreenAction> actions = new List<ScreenAction>();
        private object selectedComp;
        private List<KeyValuePair<Def, int>> applied = new List<KeyValuePair<Def, int>>();
        private bool godModeList;
        private bool announcedOpen;

        public IsekaiRunicStationScope(Window window)
        {
            this.window = window;
        }

        public override string Name => "isekai-runic-station";

        protected internal override Window OwnedWindow => window;

        protected override bool EnableTypeahead => true;

        /// <summary>Vanilla-mode Apply/Remove/rank buttons are ButtonText calls this scope presents itself.</summary>
        protected override bool CaptureWindowButtons => false;

        protected override int ContentRegionCount => 3;

        protected override string ContentRegionName(int region)
        {
            switch (region)
            {
                case ListRegion: return "RimWorldAccess.Compat.Isekai.EquipmentRegion".Translate();
                case SlotsRegion: return "RimWorldAccess.Compat.Isekai.SlotsRegion".Translate();
                default: return "RimWorldAccess.Compat.Isekai.RunesRegion".Translate();
            }
        }

        private bool Ready => IsekaiForgeCompat.Gate.Ensure() && IsekaiCompat.CoreGate.Ensure();

        protected override void RefreshContent()
        {
            items.Clear();
            slotLines.Clear();
            runes.Clear();
            applied.Clear();
            selectedComp = null;
            godModeList = false;
            if (!Ready)
                return;
            if (!IsekaiCompat.ForgeEnabled)
            {
                slotLines.Add(CompatText.ModText("Isekai_Forge_Disabled"));
                return;
            }
            IList equipment = IsekaiForgeCompat.RunicEquipment(window);
            if (equipment != null)
            {
                foreach (object obj in equipment)
                {
                    if (obj is Thing thing)
                        items.Add(thing);
                }
            }

            Thing selected = IsekaiForgeCompat.RunicSelectedItem(window);
            if (selected == null)
            {
                slotLines.Add(CompatText.ModText("Isekai_Rune_SelectFromList"));
                return;
            }
            selectedComp = IsekaiForgeCompat.EnhancementOf(selected);
            if (selectedComp == null)
            {
                slotLines.Add(CompatText.ModText("Isekai_Rune_CannotHoldRunes"));
                return;
            }
            applied = IsekaiForgeCompat.AppliedRunes(selectedComp);
            int max = IsekaiForgeCompat.MaxRuneSlots(selectedComp);
            for (int i = 0; i < max; i++)
            {
                if (i < applied.Count)
                    slotLines.Add(applied[i].Key.LabelCap + " " + IsekaiForgeCompat.RomanNumeral(applied[i].Value) + ": "
                        + IsekaiForgeCompat.RuneDescriptionForRank(applied[i].Key, applied[i].Value));
                else
                    slotLines.Add(CompatText.ModText("Isekai_Rune_EmptySlot"));
            }

            IList runeItems = IsekaiForgeCompat.RunicAvailableRuneItems(window, selectedComp);
            if (runeItems != null)
            {
                foreach (object obj in runeItems)
                {
                    if (!(obj is Thing runeItem))
                        continue;
                    Def def = IsekaiForgeCompat.RuneDefForItem(runeItem.def.defName, out int rank);
                    if (def != null)
                        runes.Add(new RuneRow { Rune = def, Rank = rank, Item = runeItem });
                }
            }
            // The window's god-mode fallback: every compatible rune def, no item needed.
            if (runes.Count == 0 && Prefs.DevMode && DebugSettings.godMode)
            {
                godModeList = true;
                int category = selected.def.IsWeapon ? 0 : 1;
                IList all = IsekaiForgeCompat.AllRuneDefs();
                if (all != null)
                {
                    foreach (object obj in all)
                    {
                        if (obj is Def def && IsekaiForgeCompat.RuneCategoryOrdinal(def) == category)
                            runes.Add(new RuneRow { Rune = def, Rank = IsekaiForgeCompat.RunicSelectedRank(window) });
                    }
                }
            }
        }

        protected override int ContentItemCount(int region)
        {
            switch (region)
            {
                case ListRegion: return items.Count;
                case SlotsRegion: return slotLines.Count;
                default: return runes.Count;
            }
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            var d = new ElementDescription();
            if (region == ListRegion)
            {
                if (index < 0 || index >= items.Count)
                    return d;
                Thing item = items[index];
                d.Label = item.LabelCapNoCount;
                d.Role = ElementRole.RadioButton;
                d.Selected = ReferenceEquals(IsekaiForgeCompat.RunicSelectedItem(window), item);
                d.Extras = IsekaiForgeScope.OwnerLine(item);
                return d;
            }
            if (region == SlotsRegion)
            {
                if (index < 0 || index >= slotLines.Count)
                    return d;
                d.Label = slotLines[index];
                if (selectedComp == null)
                {
                    d.ReadOnly = true;
                    return d;
                }
                d.Role = ElementRole.RadioButton;
                d.Selected = IsekaiForgeCompat.RunicSelectedSlot(window) == index;
                return d;
            }
            if (index < 0 || index >= runes.Count)
                return d;
            RuneRow row = runes[index];
            d.Label = row.Rune.LabelCap + " " + IsekaiForgeCompat.RomanNumeral(row.Rank);
            if (godModeList)
            {
                d.Role = ElementRole.Stepper;
                d.Value = IsekaiForgeCompat.RomanNumeral(row.Rank);
                d.AtMinimum = row.Rank <= 1;
                d.AtMaximum = row.Rank >= IsekaiForgeCompat.RuneMaxRank(row.Rune);
            }
            else
            {
                d.Role = ElementRole.Button;
                d.Value = "RimWorldAccess.Compat.Isekai.RuneCount".Translate(row.Item.stackCount);
            }
            bool canApply = selectedComp != null && IsekaiForgeCompat.CanAddRune(selectedComp);
            d.Extras = IsekaiForgeCompat.RuneDescriptionForRank(row.Rune, row.Rank);
            if (!canApply)
            {
                d.Disabled = true;
                d.Extras = CompatText.ModText("Isekai_Rune_SlotsFull") + ". " + d.Extras;
            }
            return d;
        }

        protected override bool CanAdjustContentItem(int region, int index)
        {
            return region == RunesRegion && godModeList;
        }

        protected override void AdjustContentItem(int region, int index, int direction)
        {
            if (!CanAdjustContentItem(region, index) || index < 0 || index >= runes.Count)
                return;
            RuneRow row = runes[index];
            int next = row.Rank + direction;
            if (next < 1 || next > IsekaiForgeCompat.RuneMaxRank(row.Rune))
            {
                NumericStepperHelper.SpeakBoundary(direction);
                return;
            }
            IsekaiForgeCompat.RunicSelectRank(window, next);
            SoundDefOf.DragSlider.PlayOneShotOnCamera();
            RefreshModel();
            AnnounceCurrentItem();
        }

        protected override void ActivateContentItem(int region, int index)
        {
            if (region == ListRegion)
            {
                if (index < 0 || index >= items.Count)
                    return;
                IsekaiForgeCompat.RunicSelectItem(window, items[index]);
                SoundDefOf.Click.PlayOneShotOnCamera();
                RefreshModel();
                AnnounceCurrentItem();
                return;
            }
            if (region == SlotsRegion)
            {
                if (selectedComp == null || index < 0 || index >= slotLines.Count)
                    return;
                IsekaiForgeCompat.RunicSelectSlot(window, index);
                SoundDefOf.Click.PlayOneShotOnCamera();
                RefreshModel();
                AnnounceCurrentItem();
                return;
            }
            if (index < 0 || index >= runes.Count || selectedComp == null)
                return;
            RuneRow row = runes[index];
            if (!IsekaiForgeCompat.CanAddRune(selectedComp))
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.SpeakData(CompatText.ModText("Isekai_Rune_SlotsFull"));
                return;
            }
            Apply(row);
        }

        private void Apply(RuneRow row)
        {
            Thing target = IsekaiForgeCompat.RunicSelectedItem(window);
            if (!IsekaiForgeCompat.TryAddRune(selectedComp, row.Rune, row.Rank))
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }
            if (row.Item != null && !(Prefs.DevMode && DebugSettings.godMode))
            {
                // MUTATION-C: mirrors the Apply click in Window_RunicStation.DrawAvailableRunes,
                // which consumes one rune item after a successful TryAddRune; the mod has no
                // consume method, the handler edits the stack inline.
                if (row.Item.stackCount > 1)
                    row.Item.stackCount -= 1;
                else
                    row.Item.Destroy();
            }
            SoundDefOf.Click.PlayOneShotOnCamera();
            RefreshModel();
            TolkHelper.SpeakData((string)"RimWorldAccess.Compat.Isekai.RuneApplied".Translate(
                row.Rune.LabelCap + " " + IsekaiForgeCompat.RomanNumeral(row.Rank), target != null ? target.LabelCapNoCount : ""));
        }

        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                actions.Clear();
                int slot = Ready ? IsekaiForgeCompat.RunicSelectedSlot(window) : -1;
                if (selectedComp != null && slot >= 0 && slot < applied.Count)
                {
                    Def rune = applied[slot].Key;
                    string runeLabel = rune.LabelCap + " " + IsekaiForgeCompat.RomanNumeral(applied[slot].Value);
                    Thing target = IsekaiForgeCompat.RunicSelectedItem(window);
                    object comp = selectedComp;
                    actions.Add(new ScreenAction(CompatText.ModArgs("Isekai_Rune_RemoveWithName", runeLabel), delegate
                    {
                        // The window's Remove button: the mod's own confirmation, then RemoveRuneAt.
                        Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                            CompatText.ModArgs("Isekai_Rune_RemoveConfirm", runeLabel, target != null ? target.LabelCap : ""),
                            delegate
                            {
                                IsekaiForgeCompat.RemoveRuneAt(comp, slot);
                                IsekaiForgeCompat.RunicSelectSlot(window, -1);
                                RefreshModel();
                                TolkHelper.SpeakData((string)"RimWorldAccess.Compat.Isekai.RuneRemoved".Translate(runeLabel));
                            }));
                    }));
                }
                actions.Add(new ScreenAction(CompatText.ModText("Isekai_Close"), delegate { window.Close(); }, SharedMenuGrammar.Cancel));
                return actions;
            }
        }

        public override void OnFocus()
        {
            base.OnFocus();
            if (announcedOpen)
                return;
            announcedOpen = true;
            TolkHelper.SpeakData((string)"RimWorldAccess.Compat.Isekai.ForgeOpen".Translate(CompatText.ModText("Isekai_Rune_Title"), items.Count));
        }
    }
}
