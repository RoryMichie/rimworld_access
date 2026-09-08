using System.Globalization;
using System.Text.RegularExpressions;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Owns the one modal edit session behind the captured-tab operable widgets:
    /// a slider's "Set value" row (numeric entry) and a captured text field's own
    /// row (free-text entry). Static and single-slot because only one modal text
    /// session is ever live mod-wide (<see cref="TextInputController"/> is modal),
    /// and the inspection tree that opens these edits is a static builder with no
    /// scope instance to own the session.
    ///
    /// DEVIATION FROM THE SESSION'S LIVE-MIRROR MODEL. Every other
    /// <see cref="TextFieldEditSession"/> owner mirrors the buffer into its backing
    /// store on every GUI pass (<see cref="TextFieldEditSession.MirrorLive"/>) so
    /// the field redraws as the user types. A captured tab redraws ONLY under our
    /// own detached passes, never per-frame, so there is nothing to mirror into
    /// live: the apply callback merely caches the confirmed buffer, and the commit
    /// posts a single armed write (a slider set or a text write) that re-runs the
    /// tab's draw once with that value. Exit is silent — the outcome announcer
    /// (<see cref="DevActionOutcome"/>, reached through the Request* pump) is the
    /// single voice for the result.
    /// </summary>
    internal static class CapturedFieldEditController
    {
        private static readonly TextFieldEditSession session = new TextFieldEditSession();

        // Digits with an optional leading sign and a single decimal point;
        // minLength 0 so the buffer may be cleared mid-edit. The same permissive
        // numeric spec DevTweakValuesScope uses for its float rows — no maxLength,
        // because the vanilla slider clamps the value itself on write-back.
        private static readonly TextFieldSpec FloatSpec = new TextFieldSpec(
            labelKey: null, minLength: 0, allowedChars: new Regex(@"^-?[0-9]*\.?[0-9]*$"));

        // The confirmed buffer, cached by the apply callback so the argument-less
        // confirm callback can read it. Safe as a single shared slot: only one
        // modal session is ever live at a time.
        private static string committed;

        /// <summary>
        /// Open numeric entry seeded with the slider's captured value; on
        /// Enter-confirm, parse it (invariant culture) and post a slider-set write.
        /// A buffer that will not parse as a float speaks a "not a number" notice
        /// and posts nothing.
        /// </summary>
        public static void EditSliderValue(object target, InspectTabBase tab, string rawLabel, int ordinal,
            string spokenLabel, float currentValue, InteractiveMember member = null, InspectionTreeItem node = null)
        {
            committed = null;
            session.EnterEdit(
                currentValue.ToString(CultureInfo.InvariantCulture),
                FloatSpec,
                spokenLabel,
                text => committed = text,
                delegate { },
                announcePrompt: true,
                onConfirm: () =>
                {
                    if (float.TryParse(committed, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                    {
                        InspectTabCaptureService.RequestSliderSet(target, tab, rawLabel, ordinal, value, spokenLabel, member, node);
                    }
                    else
                    {
                        TolkHelper.SpeakData(
                            "RimWorldAccess.Inspection.Parity.NotANumber".Translate(committed ?? "").ToString());
                    }
                });
        }

        /// <summary>
        /// Open free-text entry seeded with the field's captured text (single- or
        /// multi-line per <paramref name="multiLine"/>); on Enter-confirm, post a
        /// text write of the buffer.
        /// </summary>
        public static void EditText(object target, InspectTabBase tab, int ordinal, string spokenLabel,
            string currentText, bool multiLine, InteractiveMember member = null, InspectionTreeItem node = null)
        {
            committed = null;
            TextFieldSpec spec = multiLine
                ? TextFieldSpec.MultiLineUnrestricted("RimWorldAccess.TextInput.LabelDefault")
                : TextFieldSpec.Unrestricted("RimWorldAccess.TextInput.LabelDefault");
            session.EnterEdit(
                currentText ?? "",
                spec,
                spokenLabel,
                text => committed = text,
                delegate { },
                announcePrompt: true,
                onConfirm: () =>
                    InspectTabCaptureService.RequestTextWrite(target, tab, ordinal, committed ?? "", spokenLabel, member, node));
        }
    }
}
