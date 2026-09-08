using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using RimWorld;
using RimWorldAccess.Shell;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Backing state for Dialog_ChooseColonistsForIdeo, the "Assign colonists" sub-dialog of the
    /// Archonexus reform screen. Labels are the dialog's own strings, and the toggle reuses its
    /// pawnIdeoSetter so the pending-conversion bookkeeping the reform's "Next" applies stays correct.
    /// Cursor, typeahead and per-row announcement belong to
    /// <see cref="RimWorldAccess.Shell.ArchonexusConvertColonistsScope"/>; this class keeps lifecycle,
    /// the pawn list, the toggle mutation, and the two texts the scope asks it to compose.
    /// A togglable colonist is a <see cref="ElementRole.Checkbox"/> whose Check carries the state;
    /// "currently following X" rides Extras, because a bare Unchecked word cannot say which ideoligion
    /// they keep. An existing follower has no vanilla toggle at all, so it is Checkbox+Disabled with
    /// the reason in Extras.
    /// </summary>
    public static class ArchonexusConvertColonistsState
    {
        public static bool IsActive { get; private set; }

        private static Dialog_ChooseColonistsForIdeo dialog;
        private static readonly List<Pawn> pawns = new List<Pawn>();

        #region Reflection cache

        private static readonly Type DialogType = typeof(Dialog_ChooseColonistsForIdeo);
        private static readonly FieldInfo PawnsField = AccessTools.Field(DialogType, "pawns");
        private static readonly FieldInfo IdeoField = AccessTools.Field(DialogType, "ideo");
        private static readonly FieldInfo CanChangeField = AccessTools.Field(DialogType, "canChangeIdeo");
        private static readonly FieldInfo OriginalIdeoField = AccessTools.Field(DialogType, "originalIdeo");
        private static readonly FieldInfo GetterField = AccessTools.Field(DialogType, "pawnIdeoGetter");
        private static readonly FieldInfo SetterField = AccessTools.Field(DialogType, "pawnIdeoSetter");

        #endregion

        #region Lifecycle

        public static void EnsureOpen(Dialog_ChooseColonistsForIdeo d)
        {
            if (ReferenceEquals(dialog, d))
                return;
            dialog = d;
            IsActive = true;
            pawns.Clear();
            if (PawnsField.GetValue(dialog) is List<Pawn> p)
                pawns.AddRange(p);
        }

        public static void Close()
        {
            IsActive = false;
            pawns.Clear();
            // The dialog reference is intentionally retained — see ArchonexusReformIdeoState.EnsureOpen.
        }

        #endregion

        #region Input

        /// <summary>True while the dialog lists at least one colonist.</summary>
        internal static bool HasPawns => pawns.Count > 0;

        /// <summary>The dialog's colonist rows, in vanilla's own order — the scope's one content region.</summary>
        internal static int PawnCount => pawns.Count;

        /// <summary>The pawn one row stands for, or null when the index is out of range.</summary>
        internal static Pawn PawnAt(int index)
        {
            return index >= 0 && index < pawns.Count ? pawns[index] : null;
        }

        /// <summary>Escape's close path: the dialog's own Close, exactly as its bottom Close button runs it.</summary>
        internal static void CloseDialog()
        {
            if (dialog != null)
                dialog.Close();
        }

        internal static void Toggle(int index)
        {
            Pawn pawn = PawnAt(index);
            if (pawn == null) return;

            var canChange = CanChangeField.GetValue(dialog) as Func<Pawn, bool>;
            if (canChange != null && !canChange(pawn))
            {
                // An existing follower of the player ideoligion has no toggle.
                TolkHelper.Speak("ExistingFollowerOfPlayerIdeoligion".Loc());
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }

            Ideo target = IdeoField.GetValue(dialog) as Ideo;
            var getter = GetterField.GetValue(dialog) as Func<Pawn, Ideo>;
            var original = OriginalIdeoField.GetValue(dialog) as Func<Pawn, Ideo>;
            var setter = SetterField.GetValue(dialog) as Action<Pawn, Ideo>;

            Ideo currentPending = getter != null ? getter(pawn) : pawn.Ideo;
            Ideo newIdeo = (currentPending == target) ? original?.Invoke(pawn) : target;

            if (setter != null)
                setter(pawn, newIdeo);
            else
                pawn.ideo.SetIdeo(newIdeo);

            SoundDefOf.Click.PlayOneShotOnCamera();
            // When unchecked, name the ideoligion they keep: a bare Unchecked word cannot carry the
            // consequence of leaving them unconverted.
            bool nowConverting = newIdeo == target;
            ElementDescription stateDescription = new ElementDescription();
            stateDescription.Check = nowConverting ? CheckState.Checked : CheckState.Unchecked;
            string stateText = AnnouncementComposer.ComposeStateChange(stateDescription, TranslatedShellVocabulary.Instance);
            if (!nowConverting && newIdeo != null)
            {
                stateText += ". " + "RimWorldAccess.Archonexus.Convert.CurrentlyFollowing".Translate(newIdeo.name);
            }
            TolkHelper.SpeakData(stateText);
        }

        #endregion

        #region Announcements

        /// <summary>
        /// The opening text: title, description, colonist count and the how-to line. The focused row
        /// is NOT folded in; the chassis speaks it as the entry announcement right after.
        /// </summary>
        internal static string BuildOpeningText()
        {
            var sb = new StringBuilder();
            sb.Append("ChooseColonistsForIdeoTitle".Translate());
            sb.Append(". ").Append("ChooseColonistsForIdeoDesc".Translate());
            sb.Append(". ").Append(pawns.Count);
            // How to toggle and how to finish; vanilla's bottom "Close" button is Escape here.
            sb.Append(". ").Append("RimWorldAccess.Archonexus.Convert.OpenInstructions".Translate());
            return sb.ToString();
        }

        /// <summary>One colonist row, for the scope's DescribeContentItem; position is the chassis's to fill.</summary>
        internal static ElementDescription DescribeRow(int index)
        {
            ElementDescription d = new ElementDescription();
            Pawn pawn = PawnAt(index);
            if (pawn == null) return d;

            Ideo target = IdeoField.GetValue(dialog) as Ideo;
            var canChange = CanChangeField.GetValue(dialog) as Func<Pawn, bool>;
            var getter = GetterField.GetValue(dialog) as Func<Pawn, Ideo>;
            Ideo currentPending = getter != null ? getter(pawn) : pawn.Ideo;

            // Vanilla renders each colonist as a two-state Convert/Revert toggle, so this is a
            // checkbox. Colonists already following the primary have no toggle at all, so they are
            // Checkbox+Disabled rather than plain read-only rows.
            if (canChange != null && !canChange(pawn))
            {
                d.Label = pawn.LabelShortCap.ToString();
                d.Role = ElementRole.Checkbox;
                d.Disabled = true;
                d.Extras = "ExistingFollowerOfPlayerIdeoligion".Translate();
            }
            else
            {
                bool converting = currentPending == target;
                string convertLabel = "ConvertToPlayerIdeoligion".Translate().ToString(); // "Convert"
                if (target != null)
                    convertLabel += " " + target.name;
                d.Label = pawn.LabelShortCap + ". " + convertLabel;
                d.Role = ElementRole.Checkbox;
                d.Check = converting ? CheckState.Checked : CheckState.Unchecked;
                if (!converting && currentPending != null)
                {
                    // The ideoligion they would keep following; a bare Unchecked word cannot say it.
                    d.Extras = "RimWorldAccess.Archonexus.Convert.CurrentlyFollowing".Translate(currentPending.name);
                }
            }

            return d;
        }

        #endregion
    }

    // Lifecycle patches for Dialog_ChooseColonistsForIdeo; keyboard input and Enter ownership belong
    // to RimWorldAccess.Shell.ArchonexusConvertColonistsScope. An open Dialog_InfoCard masks this
    // dialog through ordinary modal-stack layering, and it is the only window type that can ever open
    // over it, so no ForeignWindowAbove override is needed. Escape is already a structural vanilla
    // no-op here (closeOnCancel=false, forceCatchAcceptAndCancelEventEvenIfUnfocused unset), but the
    // scope sets OwnsCancel true explicitly, since its own Cancel claim is the only thing that ever
    // closes this dialog.

    /// <summary>
    /// Opens ArchonexusConvertColonistsState for the shell scope when the dialog opens. Patches the
    /// generic Window.PostOpen: Dialog_ChooseColonistsForIdeo has no override of its own, and an
    /// override that skipped base would make this patch a silent no-op and leave the dialog dead to
    /// the keyboard. EnsureOpen is idempotent per dialog instance. No Notify_ManuallySetFocus is
    /// needed: the dispatcher runs once per frame from UIRootOnGUI, independent of window focus.
    /// </summary>
    [HarmonyPatch(typeof(Window), "PostOpen")]
    public static class ArchonexusConvertColonistsPatch_PostOpen
    {
        [HarmonyPostfix]
        static void Postfix(Window __instance)
        {
            if (__instance is Dialog_ChooseColonistsForIdeo d)
                ArchonexusConvertColonistsState.EnsureOpen(d);
        }
    }

    [HarmonyPatch(typeof(Window), "PostClose")]
    public static class ArchonexusConvertColonistsPatch_PostClose
    {
        [HarmonyPostfix]
        static void Postfix(Window __instance)
        {
            if (__instance is Dialog_ChooseColonistsForIdeo)
                ArchonexusConvertColonistsState.Close();
        }
    }
}
