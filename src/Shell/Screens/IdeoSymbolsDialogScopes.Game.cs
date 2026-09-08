using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Keyboard focus scope for the real vanilla <see cref="Dialog_ChooseIdeoSymbols"/> (the ideo
    /// builder's Edit symbols dialog), registered through <see cref="ScopeForWindow"/>. Three
    /// content regions mirror the window's layout: the four symbol text fields plus the
    /// worship-room Reset (region 0), the icon grid (region 1), the color grid (region 2), then
    /// the automatic Buttons region with vanilla's captured Back and Done buttons.
    ///
    /// Text rows edit through one <see cref="TextFieldEditSession"/> writing the dialog's own
    /// staged new* fields, mirrored into the vanilla TextFields each draw pass
    /// (<see cref="ChooseIdeoSymbolsDrawPatch"/>) so the sighted view tracks the typing. Nothing
    /// commits until vanilla's own Done/TryAccept runs — Enter on a field only ends its session,
    /// exactly like clicking out of the field.
    ///
    /// Icon and color rows mirror the grids' own ButtonInvisible bodies (stage the def, play
    /// Tick_High); the grids draw selection from the same staged fields, so the sighted box
    /// follows the keyboard cursor's choice.
    /// </summary>
    public sealed class ChooseIdeoSymbolsScope : ScreenScope
    {
        private const int FieldsRegion = 0;
        private const int IconRegion = 1;
        private const int ColorRegion = 2;

        private const int NameRow = 0;
        private const int AdjectiveRow = 1;
        private const int MemberNameRow = 2;
        private const int WorshipRoomRow = 3;
        private const int WorshipRoomResetRow = 4;
        private const int FieldRowCount = 5;

        // Vanilla's own symbol validation (Dialog_ChooseIdeoSymbols.cs): 40 chars, letters,
        // digits, spaces, apostrophes, hyphens. Empty stages "keep the old value" in TryAccept.
        private static readonly Regex ValidSymbolRegex = new Regex("^[\\p{L}0-9 '\\-]*$");
        private const int MaxSymbolLength = 40;

        private static readonly AccessTools.FieldRef<Dialog_ChooseIdeoSymbols, Ideo> ideoField =
            AccessTools.FieldRefAccess<Dialog_ChooseIdeoSymbols, Ideo>("ideo");
        private static readonly AccessTools.FieldRef<Dialog_ChooseIdeoSymbols, string> newNameField =
            AccessTools.FieldRefAccess<Dialog_ChooseIdeoSymbols, string>("newName");
        private static readonly AccessTools.FieldRef<Dialog_ChooseIdeoSymbols, string> newAdjectiveField =
            AccessTools.FieldRefAccess<Dialog_ChooseIdeoSymbols, string>("newAdjective");
        private static readonly AccessTools.FieldRef<Dialog_ChooseIdeoSymbols, string> newMemberNameField =
            AccessTools.FieldRefAccess<Dialog_ChooseIdeoSymbols, string>("newMemberName");
        private static readonly AccessTools.FieldRef<Dialog_ChooseIdeoSymbols, string> newWorshipRoomField =
            AccessTools.FieldRefAccess<Dialog_ChooseIdeoSymbols, string>("newWorshipRoomLabel");
        private static readonly AccessTools.FieldRef<Dialog_ChooseIdeoSymbols, IdeoIconDef> newIconDefField =
            AccessTools.FieldRefAccess<Dialog_ChooseIdeoSymbols, IdeoIconDef>("newIconDef");
        private static readonly AccessTools.FieldRef<Dialog_ChooseIdeoSymbols, ColorDef> newColorDefField =
            AccessTools.FieldRefAccess<Dialog_ChooseIdeoSymbols, ColorDef>("newColorDef");
        private static readonly PropertyInfo ideoColorsSortedProperty =
            AccessTools.Property(typeof(Dialog_ChooseIdeoSymbols), "IdeoColorsSorted");

        private readonly Dialog_ChooseIdeoSymbols dialog;
        private readonly TextFieldEditSession session = new TextFieldEditSession();
        private readonly List<IdeoIconDef> icons = new List<IdeoIconDef>();
        private readonly List<ColorDef> colors = new List<ColorDef>();
        private bool announcedOpen;

        private static OpeningFocus? pendingOpeningFocus;

        internal enum OpeningFocus { Name, Adjective, MemberName, WorshipRoom, Icon, Color }

        internal static void ArmOpeningFocus(OpeningFocus target)
        {
            pendingOpeningFocus = target;
        }

        public ChooseIdeoSymbolsScope(Dialog_ChooseIdeoSymbols dialog)
        {
            this.dialog = dialog;
            RegisterPopTeardown(session.CancelIfActive);
        }

        private void ConsumeOpeningFocus()
        {
            OpeningFocus? target = pendingOpeningFocus;
            pendingOpeningFocus = null;
            if (target == null) return;
            RefreshModel();
            int region = FieldsRegion;
            int row = NameRow;
            switch (target.Value)
            {
                case OpeningFocus.Adjective: row = AdjectiveRow; break;
                case OpeningFocus.MemberName: row = MemberNameRow; break;
                case OpeningFocus.WorshipRoom: row = WorshipRoomRow; break;
                case OpeningFocus.Icon:
                    region = IconRegion;
                    row = System.Math.Max(0, icons.IndexOf(newIconDefField(dialog)));
                    break;
                case OpeningFocus.Color:
                    region = ColorRegion;
                    row = System.Math.Max(0, colors.IndexOf(newColorDefField(dialog)));
                    break;
            }
            Model.MoveToRegion(region);
            ListModel list = Model.CurrentRegion;
            if (list != null && row < list.Count)
            {
                list.MoveTo(row);
            }
        }

        public override string Name
        {
            get { return "ideo-symbols"; }
        }

        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        /// <summary>The text fields take typed characters through their edit session, not the search.</summary>
        protected override bool ContentRegionSearchable(int region)
        {
            return region != FieldsRegion;
        }

        internal static ChooseIdeoSymbolsScope OwningScope(Window window)
        {
            return TextDialogShared.ScopeOwning<ChooseIdeoSymbolsScope>(
                window, delegate(ChooseIdeoSymbolsScope s, Window w) { return ReferenceEquals(w, s.dialog); });
        }

        /// <summary>Per-draw-pass mirror: the vanilla TextFields render the live edit buffer.</summary>
        internal void OnGuiPass()
        {
            session.MirrorLive();
        }

        // ScreenScope content contract.

        protected override int ContentRegionCount
        {
            get { return 3; }
        }

        protected override string ContentRegionName(int region)
        {
            switch (region)
            {
                case FieldsRegion: return "EditSymbols".Translate().ToString();
                case IconRegion:   return "Icon".Translate().ToString();
                default:           return "Color".Translate().ToString();
            }
        }

        protected override void RefreshContent()
        {
            icons.Clear();
            icons.AddRange(DefDatabase<IdeoIconDef>.AllDefs);
            colors.Clear();
            var sorted = ideoColorsSortedProperty?.GetValue(null) as List<ColorDef>;
            if (sorted != null)
            {
                colors.AddRange(sorted);
            }
        }

        protected override int ContentItemCount(int region)
        {
            switch (region)
            {
                case FieldsRegion: return FieldRowCount;
                case IconRegion:   return icons.Count;
                default:           return colors.Count;
            }
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            var d = new ElementDescription();
            if (region == FieldsRegion)
            {
                switch (index)
                {
                    case NameRow:
                        d.Label = "Name".Translate().ToString();
                        d.Value = newNameField(dialog);
                        d.Role = ElementRole.TextField;
                        break;
                    case AdjectiveRow:
                        d.Label = "Adjective".Translate().ToString();
                        d.Value = newAdjectiveField(dialog);
                        d.Role = ElementRole.TextField;
                        break;
                    case MemberNameRow:
                        d.Label = "IdeoMembers".Translate().ToString();
                        d.Value = newMemberNameField(dialog);
                        d.Role = ElementRole.TextField;
                        break;
                    case WorshipRoomRow:
                        d.Label = "WorshipRoom".Translate().ToString();
                        d.Value = newWorshipRoomField(dialog);
                        d.Role = ElementRole.TextField;
                        break;
                    case WorshipRoomResetRow:
                        d.Label = "WorshipRoom".Translate() + ": " + "Reset".Translate();
                        d.Role = ElementRole.Button;
                        break;
                }
                return d;
            }
            if (region == IconRegion)
            {
                if (index >= 0 && index < icons.Count)
                {
                    IdeoIconDef icon = icons[index];
                    d.Label = icon.label.NullOrEmpty() ? icon.defName : icon.LabelCap.ToString();
                    d.Role = ElementRole.RadioButton;
                    d.Selected = icon == newIconDefField(dialog);
                }
                return d;
            }
            if (index >= 0 && index < colors.Count)
            {
                ColorDef color = colors[index];
                d.Label = color.label.NullOrEmpty() ? color.defName : color.LabelCap.ToString();
                d.Role = ElementRole.RadioButton;
                d.Selected = color == newColorDefField(dialog);
            }
            return d;
        }

        protected override void ActivateContentItem(int region, int index)
        {
            if (region == FieldsRegion)
            {
                switch (index)
                {
                    case NameRow:
                        BeginFieldEdit("Name", newNameField);
                        break;
                    case AdjectiveRow:
                        BeginFieldEdit("Adjective", newAdjectiveField);
                        break;
                    case MemberNameRow:
                        BeginFieldEdit("IdeoMembers", newMemberNameField);
                        break;
                    case WorshipRoomRow:
                        BeginFieldEdit("WorshipRoom", newWorshipRoomField);
                        break;
                    case WorshipRoomResetRow:
                        // MUTATION-C: mirrors the Reset button's inline body
                        // (Dialog_ChooseIdeoSymbols.DoWindowContents) — reverts the live label to
                        // the generated default and restages the field from it.
                        SoundDefOf.Click.PlayOneShotOnCamera();
                        Ideo ideo = ideoField(dialog);
                        ideo.WorshipRoomLabel = null;
                        newWorshipRoomField(dialog) = ideo.WorshipRoomLabel;
                        var reset = new ElementDescription
                        {
                            Label = "WorshipRoom".Translate().ToString(),
                            Value = newWorshipRoomField(dialog),
                        };
                        TolkHelper.SpeakData(AnnouncementComposer.ComposeStateChange(reset, TranslatedShellVocabulary.Instance));
                        break;
                }
                return;
            }
            if (region == IconRegion)
            {
                if (index >= 0 && index < icons.Count)
                {
                    // MUTATION-C: mirrors the icon cell's ButtonInvisible body — stages the def;
                    // vanilla's Done/TryAccept commits.
                    newIconDefField(dialog) = icons[index];
                    SoundDefOf.Tick_High.PlayOneShotOnCamera();
                    AnnounceCurrentItem();
                }
                return;
            }
            if (index >= 0 && index < colors.Count)
            {
                // MUTATION-C: mirrors the color cell's ButtonInvisible body.
                newColorDefField(dialog) = colors[index];
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
                AnnounceCurrentItem();
            }
        }

        private void BeginFieldEdit(string vanillaLabelKey, AccessTools.FieldRef<Dialog_ChooseIdeoSymbols, string> field)
        {
            var spec = new TextFieldSpec(vanillaLabelKey,
                maxLength: MaxSymbolLength, minLength: 0, allowedChars: ValidSymbolRegex);
            session.EnterEdit(
                field(dialog) ?? string.Empty,
                spec,
                vanillaLabelKey.Translate().ToString(),
                value => field(dialog) = value,
                AnnounceCurrentItem);
        }

        public override void OnPush()
        {
            base.OnPush();
            announcedOpen = false;
            ConsumeOpeningFocus();
        }

        public override void OnFocus()
        {
            base.OnFocus();
            if (announcedOpen)
            {
                return;
            }
            announcedOpen = true;
            AnnounceCurrentItem();
        }
    }

    /// <summary>
    /// Keyboard focus scope for the real vanilla <see cref="Dialog_EditIdeoDescription"/> (the
    /// Edit narrative dialog): one multi-line text row staging the dialog's own newDescription,
    /// a declared Randomize action (modeled instead of captured so the fresh narrative is
    /// spoken), and vanilla's captured Cancel and Done buttons. Nothing commits until vanilla's
    /// Done/ApplyChanges runs.
    /// </summary>
    public sealed class EditIdeoDescriptionScope : ScreenScope
    {
        private static readonly AccessTools.FieldRef<Dialog_EditIdeoDescription, Ideo> ideoField =
            AccessTools.FieldRefAccess<Dialog_EditIdeoDescription, Ideo>("ideo");
        private static readonly AccessTools.FieldRef<Dialog_EditIdeoDescription, string> newDescriptionField =
            AccessTools.FieldRefAccess<Dialog_EditIdeoDescription, string>("newDescription");
        private static readonly AccessTools.FieldRef<Dialog_EditIdeoDescription, string> newTemplateField =
            AccessTools.FieldRefAccess<Dialog_EditIdeoDescription, string>("newDescriptionTemplate");

        private readonly Dialog_EditIdeoDescription dialog;
        private readonly TextFieldEditSession session = new TextFieldEditSession();
        private readonly List<ScreenAction> actions = new List<ScreenAction>();
        private bool announcedOpen;

        public EditIdeoDescriptionScope(Dialog_EditIdeoDescription dialog)
        {
            this.dialog = dialog;
            RegisterPopTeardown(session.CancelIfActive);
        }

        public override string Name
        {
            get { return "ideo-narrative"; }
        }

        internal static EditIdeoDescriptionScope OwningScope(Window window)
        {
            return TextDialogShared.ScopeOwning<EditIdeoDescriptionScope>(
                window, delegate(EditIdeoDescriptionScope s, Window w) { return ReferenceEquals(w, s.dialog); });
        }

        /// <summary>Per-draw-pass mirror: the vanilla TextArea renders the live edit buffer.</summary>
        internal void OnGuiPass()
        {
            session.MirrorLive();
        }

        // ScreenScope content contract.

        protected override int ContentRegionCount
        {
            get { return 1; }
        }

        protected override string ContentRegionName(int region)
        {
            return "EditNarrative".Translate().ToString();
        }

        protected override void RefreshContent()
        {
        }

        protected override int ContentItemCount(int region)
        {
            return 1;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            var d = new ElementDescription();
            d.Label = "EditNarrative".Translate().ToString();
            d.Value = newDescriptionField(dialog);
            d.Role = ElementRole.TextField;
            return d;
        }

        protected override void ActivateContentItem(int region, int index)
        {
            session.EnterEdit(
                newDescriptionField(dialog) ?? string.Empty,
                TextFieldSpec.MultiLineUnrestricted("Description"),
                "EditNarrative".Translate().ToString(),
                ApplyDescription,
                AnnounceCurrentItem);
        }

        /// <summary>Mirrors the TextArea's own change branch: a manual edit clears the staged template.</summary>
        private void ApplyDescription(string value)
        {
            if (newDescriptionField(dialog) != value)
            {
                newDescriptionField(dialog) = value;
                newTemplateField(dialog) = null;
            }
        }

        /// <summary>Randomize is declared, not captured: the fresh narrative must be spoken.</summary>
        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                actions.Clear();
                actions.Add(new ScreenAction("Randomize".Translate().ToString(), RandomizeNarrative));
                return actions;
            }
        }

        // MUTATION-C: mirrors the Randomize button's inline body
        // (Dialog_EditIdeoDescription.DoWindowContents) — stages text and template; Done commits.
        private void RandomizeNarrative()
        {
            IdeoDescriptionResult result = ideoField(dialog).GetNewDescription(force: true);
            newDescriptionField(dialog) = result.text;
            newTemplateField(dialog) = result.template;
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
            TolkHelper.SpeakData(result.text);
        }

        /// <summary>Randomize is modeled above; vanilla's Cancel and Done stay captured.</summary>
        protected override bool KeepCapturedButton(string rawLabel)
        {
            return !string.IsNullOrEmpty(rawLabel) && rawLabel != "Randomize".Translate();
        }

        public override void OnPush()
        {
            base.OnPush();
            announcedOpen = false;
        }

        public override void OnFocus()
        {
            base.OnFocus();
            if (announcedOpen)
            {
                return;
            }
            announcedOpen = true;
            AnnounceCurrentItem();
        }
    }

    /// <summary>Drives the per-pass live-buffer mirror for whichever of the two scopes owns the drawing dialog.</summary>
    [HarmonyPatch(typeof(Dialog_ChooseIdeoSymbols), "DoWindowContents")]
    public static class ChooseIdeoSymbolsDrawPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Dialog_ChooseIdeoSymbols __instance)
        {
            ChooseIdeoSymbolsScope.OwningScope(__instance)?.OnGuiPass();
        }
    }

    /// <summary>See <see cref="ChooseIdeoSymbolsDrawPatch"/>.</summary>
    [HarmonyPatch(typeof(Dialog_EditIdeoDescription), "DoWindowContents")]
    public static class EditIdeoDescriptionDrawPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Dialog_EditIdeoDescription __instance)
        {
            EditIdeoDescriptionScope.OwningScope(__instance)?.OnGuiPass();
        }
    }

    /// <summary>
    /// Refreshes whichever ideo-editing host is live after the symbols dialog's Done commit,
    /// the job the retired per-field editors did through their own AfterEdit.
    /// </summary>
    [HarmonyPatch(typeof(Dialog_ChooseIdeoSymbols), "TryAccept")]
    public static class ChooseIdeoSymbolsAcceptNotifyPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            IdeoEditNotifyHub.Notify();
        }
    }

    /// <summary>See <see cref="ChooseIdeoSymbolsAcceptNotifyPatch"/>.</summary>
    [HarmonyPatch(typeof(Dialog_EditIdeoDescription), "ApplyChanges")]
    public static class EditIdeoDescriptionApplyNotifyPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            IdeoEditNotifyHub.Notify();
        }
    }
}
