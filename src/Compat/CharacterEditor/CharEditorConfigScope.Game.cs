using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Scope for Character Editor's <c>DialogConfigurate</c> mod-options dialog, backed by
    /// <see cref="CharEditorConfigCompat"/> and registered against the private window type in
    /// <see cref="CharEditorDialogCompat"/>.
    /// Five content regions mirroring the mod's own visual groups: Hotkeys (each combo row plus
    /// an explicit Clear row, since the mod draws Clear as a per-row icon), Numerics (steppers
    /// with exact entry; VERSION is a read-only row only under Prefs.DevMode, matching the mod's
    /// own gate), Options (the boolean checkboxes), Pawn Slots (count-driven by the LIVE
    /// NUMPAWNSLOTS value, so changing it and returning reflects immediately), and Custom Data
    /// (the serialized modification strings the mod itself lets players edit; each carries the
    /// mod's Descr as a spoken warning).
    /// Persistence: every mutator writes only the live in-memory ModOptions dictionaries, as the
    /// mod's own rows do; the Save-and-close action is the ONE path that flushes options.txt and
    /// pawnslots.txt. Escape closes without saving, matching the dialog's close-X.
    /// The hotkey rows' Enter opens vanilla <c>Dialog_DefineBinding</c>, which the generic reader
    /// covers; <see cref="OnFocus"/> re-announces the row on return because nothing else does.
    /// </summary>
    internal sealed class CharEditorConfigScope : ScreenScope, IListingRingClient
    {
        private const int HotkeysRegion = 0;
        private const int NumericsRegion = 1;
        private const int OptionsRegion = 2;
        private const int SlotsRegion = 3;
        private const int CustomDataRegion = 4;

        private readonly Window dialog;
        private readonly List<ScreenAction> actions = new List<ScreenAction>();
        private readonly TextFieldEditSession editSession = new TextFieldEditSession();

        private List<object> intKeys = new List<object>();
        private List<object> boolKeys = new List<object>();
        private List<int> slotKeys = new List<int>();
        private List<object> customKeys = new List<object>();

        private bool announcedOpen;

        public CharEditorConfigScope(Window dialog)
        {
            this.dialog = dialog;

            Claim("charEdConfig.saveAndClose", e => PerformSaveAndClose());

            RegisterPopTeardown(editSession.CancelIfActive);
        }

        public override string Name => "char-editor-config";

        protected internal override Window OwnedWindow => dialog;

        protected override bool EnableTypeahead => true;

        /// <summary>The dialog draws its numeric +/-, hotkey, and Clear icons as real ButtonText/ButtonImage calls, so blanket capture would scrape them into Buttons.</summary>
        protected override bool CaptureWindowButtons => false;

        protected override int ContentRegionCount => 5;

        protected override string ContentRegionName(int region)
        {
            switch (region)
            {
                case HotkeysRegion: return "RimWorldAccess.CharEd.Config.HotkeysRegion".Translate().ToString();
                case NumericsRegion: return "RimWorldAccess.CharEd.Config.NumericsRegion".Translate().ToString();
                case OptionsRegion: return "RimWorldAccess.CharEd.Config.OptionsRegion".Translate().ToString();
                case SlotsRegion: return "RimWorldAccess.CharEd.Config.SlotsRegion".Translate().ToString();
                default: return "RimWorldAccess.CharEd.Config.CustomDataRegion".Translate().ToString();
            }
        }

        public override void OnPush()
        {
            base.OnPush();
            RebuildKeys();
            ScreenScopeDrawPatch.RegisterListingClient(this);
            CharEditorConfigRowRingPatch.Recording = true;
        }

        public override void OnPop()
        {
            CharEditorConfigRowRingPatch.Recording = false;
            ScreenScopeDrawPatch.UnregisterListingClient(this);
            base.OnPop();
        }

        /// <summary>
        /// The two fixed-rect regions. They draw no listing rows, so
        /// <see cref="IListingRingClient.CurrentListingFocus"/> returns None for them: the two
        /// answers are disjoint by region and only one ever rings.
        /// </summary>
        protected internal override Rect FocusedContentRect()
        {
            ListModel region = Model.CurrentRegion;
            if (region == null || region.Index < 0)
            {
                return default(Rect);
            }
            switch (Model.RegionIndex)
            {
                case HotkeysRegion: return CharEditorConfigRowRingPatch.HotkeyRowRect(region.Index);
                case NumericsRegion: return CharEditorConfigRowRingPatch.NumericRowRect(region.Index);
                default: return default(Rect);
            }
        }

        Window IListingRingClient.ListingRingWindow
        {
            get { return dialog; }
        }

        /// <summary>
        /// One sticky key stands for a whole section, so the row is picked out by the tripwire:
        /// the mod's own caption for the focused entry, read from the same live dictionary the
        /// row draws from. The tripwire must never be omitted -- a key alone rings every row in
        /// the section. Hotkeys and Numerics draw no listing rows and return None.
        /// </summary>
        ListingRingFocus IListingRingClient.CurrentListingFocus()
        {
            ListModel region = Model.CurrentRegion;
            if (region == null || region.Index < 0)
            {
                return ListingRingFocus.None;
            }
            switch (Model.RegionIndex)
            {
                case OptionsRegion:
                    if (region.Index >= boolKeys.Count)
                        break;
                    return new ListingRingFocus
                    {
                        RowKey = CharEditorConfigRowRingPatch.BoolSectionKey,
                        LabelTripwire = CharEditorConfigCompat.BoolTitle(dialog, boolKeys[region.Index]),
                    };
                case SlotsRegion:
                    if (region.Index >= slotKeys.Count)
                        break;
                    return new ListingRingFocus
                    {
                        RowKey = CharEditorConfigRowRingPatch.StringSectionKey,
                        LabelTripwire = CharEditorConfigCompat.SlotLabel(slotKeys[region.Index]),
                    };
                case CustomDataRegion:
                    if (region.Index >= customKeys.Count)
                        break;
                    return new ListingRingFocus
                    {
                        RowKey = CharEditorConfigRowRingPatch.StringSectionKey,
                        LabelTripwire = CharEditorConfigCompat.StringTitle(dialog, customKeys[region.Index]),
                    };
            }
            return ListingRingFocus.None;
        }

        private void RebuildKeys()
        {
            intKeys = CharEditorConfigCompat.IntKeys(dialog);
            intKeys.RemoveAll(k => k.ToString() == "VERSION" && !Prefs.DevMode);
            boolKeys = CharEditorConfigCompat.BoolKeys(dialog);
            slotKeys = CharEditorConfigCompat.SlotKeys(dialog);
            customKeys = CharEditorConfigCompat.CustomStringKeysList(dialog);
        }

        protected override int ContentItemCount(int region)
        {
            switch (region)
            {
                case HotkeysRegion: return 4; // editor combo, editor clear, teleport combo, teleport clear
                case NumericsRegion: return intKeys.Count;
                case OptionsRegion: return boolKeys.Count;
                case SlotsRegion: return slotKeys.Count;
                default: return customKeys.Count;
            }
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            var d = new ElementDescription();
            switch (region)
            {
                case HotkeysRegion: DescribeHotkeyRow(d, index); break;
                case NumericsRegion: DescribeNumericRow(d, index); break;
                case OptionsRegion: DescribeBoolRow(d, index); break;
                case SlotsRegion: DescribeSlotRow(d, index); break;
                default: DescribeCustomRow(d, index); break;
            }
            return d;
        }

        /// <summary>The mod's own KeyBindingDef names, as DrawHotkey's callers pass them.</summary>
        internal const string EditorHotkeyDefName = "HotkeyEditor";
        internal const string TeleportHotkeyDefName = "HotkeyTeleport";

        private static bool IsEditorRow(int index) => index == 0 || index == 1;
        private static bool IsClearRow(int index) => index == 1 || index == 3;
        private static string HotkeyDefName(int index) => IsEditorRow(index) ? EditorHotkeyDefName : TeleportHotkeyDefName;
        private static object HotkeyOptionKey(int index) => IsEditorRow(index)
            ? CharEditorConfigCompat.OptionS_HotkeyEditor
            : CharEditorConfigCompat.OptionS_HotkeyTeleport;

        private void DescribeHotkeyRow(ElementDescription d, int index)
        {
            if (IsClearRow(index))
            {
                d.Label = IsEditorRow(index)
                    ? "RimWorldAccess.CharEd.Config.ClearEditorHotkey".Translate().ToString()
                    : "RimWorldAccess.CharEd.Config.ClearTeleportHotkey".Translate().ToString();
                d.Role = ElementRole.Button;
                return;
            }
            d.Label = IsEditorRow(index) ? CharEditorConfigCompat.EditorHotkeyLabel : CharEditorConfigCompat.TeleportHotkeyLabel;
            d.Role = ElementRole.ComboBox;
            d.Value = CharEditorConfigCompat.HotkeyBoundLabel(HotkeyDefName(index));
            d.Extras = IsEditorRow(index) ? CharEditorConfigCompat.EditorHotkeyDescr : CharEditorConfigCompat.TeleportHotkeyDescr;
        }

        private void DescribeNumericRow(ElementDescription d, int index)
        {
            object key = intKeys[index];
            d.Label = CharEditorConfigCompat.IntTitle(dialog, key);
            d.Extras = CharEditorConfigCompat.IntDescr(dialog, key);
            if (key.ToString() == "VERSION")
            {
                d.Role = ElementRole.None;
                d.ReadOnly = true;
                d.Value = CharEditorConfigCompat.IntValue(dialog, key).ToString();
                return;
            }
            d.Role = ElementRole.Stepper;
            int value = CharEditorConfigCompat.IntValue(dialog, key);
            d.Value = value.ToString();
            d.AtMinimum = value <= 1;
            d.AtMaximum = value >= CharEditorConfigCompat.IntMax(key);
            d.EntersEditOnAccept = true;
        }

        protected override bool CanAdjustContentItem(int region, int index)
        {
            if (region != NumericsRegion) return false;
            return intKeys[index].ToString() != "VERSION";
        }

        protected override void AdjustContentItem(int region, int index, int direction)
        {
            if (region != NumericsRegion) return;
            object key = intKeys[index];
            if (direction > 0) CharEditorConfigCompat.IntStepPlus(dialog, key);
            else CharEditorConfigCompat.IntStepMinus(dialog, key);
            SoundDefOf.DragSlider.PlayOneShotOnCamera();
            AnnounceNumericChange(index);
        }

        private void AnnounceNumericChange(int index)
        {
            RefreshModel();
            var d = new ElementDescription();
            DescribeNumericRow(d, index);
            TolkHelper.SpeakData(AnnouncementComposer.ComposeStateChange(d, TranslatedShellVocabulary.Instance));
        }

        private void DescribeBoolRow(ElementDescription d, int index)
        {
            object key = boolKeys[index];
            d.Label = CharEditorConfigCompat.BoolTitle(dialog, key);
            d.Role = ElementRole.Checkbox;
            d.Check = CharEditorConfigCompat.BoolValue(dialog, key) ? CheckState.Checked : CheckState.Unchecked;
            d.Extras = CharEditorConfigCompat.BoolDescr(dialog, key);
        }

        private void DescribeSlotRow(ElementDescription d, int index)
        {
            int slot = slotKeys[index];
            d.Label = CharEditorConfigCompat.SlotLabel(slot);
            d.Role = ElementRole.TextField;
            string text = CharEditorConfigCompat.SlotValue(dialog, slot);
            if (string.IsNullOrEmpty(text))
            {
                d.ValueBlank = true;
            }
            else
            {
                d.Value = "RimWorldAccess.CharEd.Config.SlotOccupied".Translate().ToString();
            }
        }

        private void DescribeCustomRow(ElementDescription d, int index)
        {
            object key = customKeys[index];
            d.Label = CharEditorConfigCompat.StringTitle(dialog, key);
            d.Role = ElementRole.TextField;
            string text = CharEditorConfigCompat.StringValue(dialog, key);
            d.ValueBlank = string.IsNullOrEmpty(text);
            if (!d.ValueBlank)
            {
                d.Value = "RimWorldAccess.CharEd.Config.CustomDataPresent".Translate().ToString();
            }
            d.Extras = CharEditorConfigCompat.StringDescr(dialog, key);
        }

        protected override void ActivateContentItem(int region, int index)
        {
            switch (region)
            {
                case HotkeysRegion: ActivateHotkeyRow(index); break;
                case NumericsRegion: ActivateNumericRow(index); break;
                case OptionsRegion: ActivateBoolRow(index); break;
                case SlotsRegion: ActivateSlotRow(index); break;
                case CustomDataRegion: ActivateCustomRow(index); break;
            }
        }

        private void ActivateHotkeyRow(int index)
        {
            if (IsClearRow(index))
            {
                CharEditorConfigCompat.RemoveHotkey(dialog, HotkeyDefName(index), HotkeyOptionKey(index));
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                RefreshModel();
                var d = new ElementDescription();
                // The paired combo row sits one index earlier -- re-describe it, not the Clear row itself.
                DescribeHotkeyRow(d, IsEditorRow(index) ? 0 : 2);
                TolkHelper.SpeakData(AnnouncementComposer.ComposeStateChange(d, TranslatedShellVocabulary.Instance));
                return;
            }
            // Opens vanilla Dialog_DefineBinding; the shell's generic reader takes it from here.
            CharEditorConfigCompat.ChangeHotkey(dialog, HotkeyDefName(index));
        }

        private void ActivateNumericRow(int index)
        {
            object key = intKeys[index];
            if (key.ToString() == "VERSION") return;
            int max = CharEditorConfigCompat.IntMax(key);
            int current = CharEditorConfigCompat.IntValue(dialog, key);
            // Floor 1 / ceiling IntMax are the mod's own NumericTextField bounds.
            CharEdNumericEntry.OpenInt(editSession, CharEditorConfigCompat.IntTitle(dialog, key),
                current, 1, max,
                v => CharEditorConfigCompat.SetIntExact(dialog, key, v),
                onExit: () => AnnounceNumericChange(index));
        }

        private void ActivateBoolRow(int index)
        {
            object key = boolKeys[index];
            bool newValue = !CharEditorConfigCompat.BoolValue(dialog, key);
            CharEditorConfigCompat.SetBool(dialog, key, newValue);
            SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
            RefreshModel();
            var d = new ElementDescription();
            DescribeBoolRow(d, index);
            TolkHelper.SpeakData(AnnouncementComposer.ComposeStateChange(d, TranslatedShellVocabulary.Instance));
        }

        private void ActivateSlotRow(int index)
        {
            int slot = slotKeys[index];
            string current = CharEditorConfigCompat.SlotValue(dialog, slot);
            var spec = new TextFieldSpec(labelKey: null, minLength: 0);
            editSession.EnterEdit(current, spec, CharEditorConfigCompat.SlotLabel(slot),
                value => CharEditorConfigCompat.SetSlotText(dialog, slot, value),
                onExit: () => AnnounceSlotChange(index));
        }

        private void AnnounceSlotChange(int index)
        {
            RefreshModel();
            var d = new ElementDescription();
            DescribeSlotRow(d, index);
            TolkHelper.SpeakData(AnnouncementComposer.ComposeStateChange(d, TranslatedShellVocabulary.Instance));
        }

        private void ActivateCustomRow(int index)
        {
            object key = customKeys[index];
            string current = CharEditorConfigCompat.StringValue(dialog, key);
            var spec = new TextFieldSpec(labelKey: null, minLength: 0);
            editSession.EnterEdit(current, spec, CharEditorConfigCompat.StringTitle(dialog, key),
                value => CharEditorConfigCompat.SetCustomString(dialog, key, value),
                onExit: () => AnnounceCustomChange(index));
        }

        private void AnnounceCustomChange(int index)
        {
            RefreshModel();
            var d = new ElementDescription();
            DescribeCustomRow(d, index);
            TolkHelper.SpeakData(AnnouncementComposer.ComposeStateChange(d, TranslatedShellVocabulary.Instance));
        }

        /// <summary>Shift+Enter presses Save and close from anywhere on this screen: the one-chord proceed.</summary>
        protected override string DefaultAcceptActionId
        {
            get { return "charEdConfig.saveAndClose"; }
        }

        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                actions.Clear();
                actions.Add(new ScreenAction(CharEditorConfigCompat.ResetAllLabel, PerformResetAllWithConfirm));
                actions.Add(new ScreenAction(CharEditorConfigCompat.DeleteSlotsLabel, PerformDeleteAllSlots));
                actions.Add(new ScreenAction(CharEditorConfigCompat.ExportLabel, PerformExportSlots));
                actions.Add(new ScreenAction(CharEditorConfigCompat.ImportLabel, PerformImportSlots));
                actions.Add(new ScreenAction("RimWorldAccess.CharEd.Config.SaveAndClose".Translate(), PerformSaveAndClose, "charEdConfig.saveAndClose"));
                return actions;
            }
        }

        /// <summary>The mod's own AResetAll has no confirm; this adds one, since every option silently reverts.</summary>
        private void PerformResetAllWithConfirm()
        {
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                "RimWorldAccess.CharEd.Config.ConfirmResetAll".Translate(),
                delegate
                {
                    CharEditorConfigCompat.ResetAllDefaults(dialog);
                    RebuildKeys();
                    RefreshModel();
                    TolkHelper.SpeakData("RimWorldAccess.CharEd.Config.ResetAllDone".Translate().ToString());
                },
                destructive: true));
        }

        /// <summary>Rides the mod's own confirm (MessageTool.ShowActionDialog) -- never skip to the delete.</summary>
        private void PerformDeleteAllSlots()
        {
            CharEditorConfigCompat.DeleteAllSlotsWithConfirm(dialog);
        }

        private void PerformExportSlots()
        {
            CharEditorConfigCompat.ExportSlots(dialog);
        }

        private void PerformImportSlots()
        {
            CharEditorConfigCompat.ImportSlots(dialog);
            RefreshModel();
        }

        private void PerformSaveAndClose()
        {
            CharEditorConfigCompat.SaveAndClose(dialog);
        }

        public override void OnFocus()
        {
            base.OnFocus();
            if (!announcedOpen)
            {
                announcedOpen = true;
                TolkHelper.SpeakData("RimWorldAccess.CharEd.Config.Opened".Translate().ToString());
                AnnounceCurrentItem();
                return;
            }
            // Return from Dialog_DefineBinding: nothing else announces the change.
            RebuildKeys();
            AnnounceCurrentItem();
        }
    }
}
