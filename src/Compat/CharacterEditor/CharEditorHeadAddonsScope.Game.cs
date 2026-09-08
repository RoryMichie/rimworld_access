using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The keyboard focus scope for the Character Editor mod's <c>DialogChangeHeadAddons</c>
    /// registered through <see cref="ScopeForWindow.Register"/> from
    /// <see cref="CharEditorDialogCompat"/>. All reflection lives in
    /// <see cref="RimWorldAccess.CharEditorHeadAddonsCompat"/>; this scope only reads its typed
    /// methods. A small dedicated scope, NOT the shared <see cref="CharEditorBrowserScope"/>: there
    /// is no list here, only per-addon controls. Alien-race-only (the HAR body-addon system); with
    /// no HAR race present the mod never opens the dialog, so this scope never attaches.
    ///
    /// ONE content region, FIVE rows per addon (the dialog's own per-addon control group): a
    /// variant Stepper (0 to the def-declared or texture-probed max), a female draw Checkbox, a
    /// male draw Checkbox (both pure gender-visibility flips, not deletes, despite the mod's
    /// misleading "Remove*" method names), a draw-size Slider (0-2, uniform scale only -- the mod's
    /// own UI has no independent Y-axis control either), and a rotation Stepper (-360 to 360, the
    /// mod's own int-truncated range).
    ///
    /// THREE apply mechanisms, not one: draw size and rotation write straight
    /// through and call <c>UpdateGraphics()</c> themselves on every change (vehicle A); the
    /// gender toggles do the same through a dedicated toggle method; the variant index is the
    /// dialog's own BATCHED control (its mouse UI diffs a whole-list snapshot once per frame and
    /// commits only if something changed) -- <see cref="CharEditorHeadAddonsCompat.SetVariant"/>
    /// applies the SAME two-call commit (<c>AlienRaceComp_SetAddonVariants</c> then
    /// <c>UpdateGraphics()</c>) on every keyboard change instead of batching across a frame, a
    /// strictly more responsive application of the identical mechanism.
    ///
    /// LIVE RE-TARGET (the dialog's own <c>if (tempPawn.ThingID != CEditor.API.Pawn.ThingID) { Init(); }</c>)
    /// -- every row read goes through
    /// <see cref="RimWorldAccess.CharEditorCompat.CurrentPawn"/> live rather than a pawn captured
    /// once at construction, so the Appearance section's Character Editor pawn-switch machinery
    /// stays honest here too.
    ///
    /// EVERYTHING APPLIES LIVE, immediately, with no cancel or revert path: the dialog has no
    /// OK/Cancel distinction, only Close. The one-shot "applies immediately" notice reuses the SAME
    /// key <see cref="CharEditorColorScope"/> mints for this phrase. CLOSE is the
    /// only declared action: the dialog overrides <c>OnAcceptKeyPressed</c> to close,
    /// and never sets <c>closeOnCancel</c> away from vanilla's own default-true, so Escape already
    /// closes it through the base <see cref="Window"/>'s own <c>OnCancelKeyPressed</c> with no
    /// <see cref="ScreenScope.OwnsCancel"/> override needed here (the same reasoning
    /// <see cref="CharEditorBirthdayScope"/>/<see cref="CharEditorColorScope"/> already document).
    /// </summary>
    internal sealed class CharEditorHeadAddonsScope : ScreenScope
    {
        private const int RowsPerAddon = 5;
        private const int VariantOffset = 0;
        private const int FemaleOffset = 1;
        private const int MaleOffset = 2;
        private const int DrawSizeOffset = 3;
        private const int RotationOffset = 4;

        private const float DrawSizeMin = 0f;
        private const float DrawSizeMax = 2f;
        private const float DrawSizeStep = 0.05f;
        private const int RotationMin = -360;
        private const int RotationMax = 360;

        private readonly Window dialog;
        private readonly List<ScreenAction> actions = new List<ScreenAction>();
        private readonly TextFieldEditSession editSession = new TextFieldEditSession();

        private bool announcedOpen;

        public CharEditorHeadAddonsScope(Window dialog)
        {
            this.dialog = dialog;

            RegisterPopTeardown(editSession.CancelIfActive);
        }

        public override string Name => "char-editor-head-addons";

        protected internal override Window OwnedWindow => dialog;

        /// <summary>The dialog draws its own real close-X chrome; no other buttons exist to over-capture, but blanket capture would still duplicate the declared Close action -- matching the birthday/browser scopes' own reasoning.</summary>
        protected override bool CaptureWindowButtons => false;

        public override void OnFocus()
        {
            base.OnFocus();
            if (announcedOpen)
                return;
            announcedOpen = true;
            TolkHelper.SpeakData("RimWorldAccess.CharEd.HeadAddons.Title".Translate() + " "
                + "RimWorldAccess.CharEd.Character.AppliesImmediately".Translate());
            AnnounceCurrentItem();
        }

        // ------------------------------------------------------------------
        // Content region.
        // ------------------------------------------------------------------

        protected override int ContentRegionCount => 1;

        protected override string ContentRegionName(int region) =>
            "RimWorldAccess.CharEd.HeadAddons.AddonsRegion".Translate().ToString();

        protected override int ContentItemCount(int region) =>
            CharEditorHeadAddonsCompat.AddonCount(dialog) * RowsPerAddon;

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            var d = new ElementDescription();
            int addonIndex = index / RowsPerAddon;
            int offset = index % RowsPerAddon;
            string addonName = CharEditorHeadAddonsCompat.AddonLabel(dialog, addonIndex);
            switch (offset)
            {
                case VariantOffset:
                {
                    int value = CharEditorHeadAddonsCompat.Variant(dialog, addonIndex);
                    int max = CharEditorHeadAddonsCompat.VariantMax(dialog, addonIndex);
                    d.Label = "RimWorldAccess.CharEd.HeadAddons.VariantRow".Translate(addonName);
                    d.Role = ElementRole.Stepper;
                    d.Value = value.ToString();
                    d.AtMinimum = value <= 0;
                    d.AtMaximum = value >= max;
                    break;
                }
                case FemaleOffset:
                    d.Label = "RimWorldAccess.CharEd.HeadAddons.FemaleRow".Translate(addonName);
                    d.Role = ElementRole.Checkbox;
                    d.Check = CharEditorHeadAddonsCompat.DrawForFemale(dialog, addonIndex) ? CheckState.Checked : CheckState.Unchecked;
                    break;
                case MaleOffset:
                    d.Label = "RimWorldAccess.CharEd.HeadAddons.MaleRow".Translate(addonName);
                    d.Role = ElementRole.Checkbox;
                    d.Check = CharEditorHeadAddonsCompat.DrawForMale(dialog, addonIndex) ? CheckState.Checked : CheckState.Unchecked;
                    break;
                case DrawSizeOffset:
                {
                    float value = CharEditorHeadAddonsCompat.DrawSize(dialog, addonIndex);
                    d.Label = "RimWorldAccess.CharEd.HeadAddons.DrawSizeRow".Translate(addonName);
                    d.Role = ElementRole.Slider;
                    d.Value = value.ToString("0.00");
                    d.AtMinimum = value <= DrawSizeMin;
                    d.AtMaximum = value >= DrawSizeMax;
                    break;
                }
                case RotationOffset:
                {
                    int value = (int)CharEditorHeadAddonsCompat.Rotation(dialog, addonIndex);
                    d.Label = "RimWorldAccess.CharEd.HeadAddons.RotationRow".Translate(addonName);
                    d.Role = ElementRole.Stepper;
                    d.Value = value.ToString();
                    d.AtMinimum = value <= RotationMin;
                    d.AtMaximum = value >= RotationMax;
                    break;
                }
            }
            return d;
        }

        // ------------------------------------------------------------------
        // Left/Right: step in place. Enter: toggle the checkboxes, exact-entry for the rest.
        // The gender-draw checkboxes have no direction-sensitive adjust
        // (vanilla checkboxes have no arrow-adjust either) -- Enter/Space
        // toggle them instead, via ActivateContentItem.
        // ------------------------------------------------------------------

        protected override bool CanAdjustContentItem(int region, int index)
        {
            int offset = index % RowsPerAddon;
            return offset != FemaleOffset && offset != MaleOffset;
        }

        protected override void AdjustContentItem(int region, int index, int direction)
        {
            int addonIndex = index / RowsPerAddon;
            int offset = index % RowsPerAddon;
            Pawn pawn = CharEditorCompat.CurrentPawn;
            switch (offset)
            {
                case VariantOffset:
                {
                    int max = CharEditorHeadAddonsCompat.VariantMax(dialog, addonIndex);
                    int v = CharEditorHeadAddonsCompat.Variant(dialog, addonIndex) + direction;
                    if (v < 0) v = 0;
                    if (v > max) v = max;
                    CharEditorHeadAddonsCompat.SetVariant(dialog, pawn, addonIndex, v);
                    break;
                }
                case DrawSizeOffset:
                {
                    float v = CharEditorHeadAddonsCompat.DrawSize(dialog, addonIndex) + DrawSizeStep * direction;
                    if (v < DrawSizeMin) v = DrawSizeMin;
                    if (v > DrawSizeMax) v = DrawSizeMax;
                    CharEditorHeadAddonsCompat.SetDrawSize(dialog, addonIndex, v);
                    break;
                }
                case RotationOffset:
                {
                    int v = (int)CharEditorHeadAddonsCompat.Rotation(dialog, addonIndex) + direction;
                    if (v < RotationMin) v = RotationMin;
                    if (v > RotationMax) v = RotationMax;
                    CharEditorHeadAddonsCompat.SetRotation(dialog, addonIndex, v);
                    break;
                }
                default:
                    return;
            }
            SoundDefOf.DragSlider.PlayOneShotOnCamera();
            AnnounceRow(index);
        }

        protected override void ActivateContentItem(int region, int index)
        {
            int addonIndex = index / RowsPerAddon;
            int offset = index % RowsPerAddon;
            if (offset == FemaleOffset || offset == MaleOffset)
            {
                CharEditorHeadAddonsCompat.ToggleDrawFor(CharEditorCompat.CurrentPawn, addonIndex, female: offset == FemaleOffset);
                SoundDefOf.DragSlider.PlayOneShotOnCamera();
                AnnounceRow(index);
                return;
            }
            string addonName = CharEditorHeadAddonsCompat.AddonLabel(dialog, addonIndex);
            switch (offset)
            {
                case VariantOffset:
                {
                    int max = CharEditorHeadAddonsCompat.VariantMax(dialog, addonIndex);
                    CharEdNumericEntry.OpenInt(editSession,
                        "RimWorldAccess.CharEd.HeadAddons.VariantRow".Translate(addonName).ToString(),
                        CharEditorHeadAddonsCompat.Variant(dialog, addonIndex), 0, max,
                        v => CharEditorHeadAddonsCompat.SetVariant(dialog, CharEditorCompat.CurrentPawn, addonIndex, v),
                        onExit: () => AnnounceRow(index));
                    return;
                }
                case RotationOffset:
                {
                    CharEdNumericEntry.OpenInt(editSession,
                        "RimWorldAccess.CharEd.HeadAddons.RotationRow".Translate(addonName).ToString(),
                        (int)CharEditorHeadAddonsCompat.Rotation(dialog, addonIndex), RotationMin, RotationMax,
                        v => CharEditorHeadAddonsCompat.SetRotation(dialog, addonIndex, v),
                        onExit: () => AnnounceRow(index), signed: true);
                    return;
                }
            }
            AnnounceCurrentItem();
        }

        private void AnnounceRow(int index)
        {
            RefreshModel();
            TolkHelper.SpeakData(AnnouncementComposer.ComposeStateChange(
                DescribeContentItem(0, index), TranslatedShellVocabulary.Instance));
        }

        // ------------------------------------------------------------------
        // Buttons region: Close only -- the dialog has no OK/Cancel distinction.
        // ------------------------------------------------------------------

        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                actions.Clear();
                actions.Add(new ScreenAction("CloseButton".Translate(), () => dialog.Close()));
                return actions;
            }
        }
    }
}
