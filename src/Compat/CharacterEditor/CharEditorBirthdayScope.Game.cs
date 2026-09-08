using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The keyboard focus scope for the Character Editor mod's <c>DialogChangeBirthday</c>,
    /// registered through <see cref="ScopeForWindow.Register"/> from
    /// <see cref="CharEditorDialogCompat"/>. All reflection lives in
    /// <see cref="RimWorldAccess.CharEditorBirthdayCompat"/>; this scope only reads its typed
    /// methods, matching the <see cref="VfPainterScope"/> facade split -- a small dedicated scope
    /// over a mod dialog, NOT the shared <see cref="CharEditorBrowserScope"/> (there is no list
    /// here, only numeric fields).
    ///
    /// ONE content region of Stepper rows: the chronological Year/Quadrum/Day/Hour, then the
    /// biological Years/Quadrums/Days/Hours, then (only when the mod itself would show it) the
    /// life-stage row. Left/Right step by one within the exact ranges harvested from the mod's own
    /// <c>Listing_X.AddIntSection</c> calls; Enter opens an exact-entry
    /// <see cref="TextFieldEditSession"/> digit-capped to the same range. The two day fields use
    /// DIFFERENT bases on purpose (chronological 1-15, biological 0-14) -- this
    /// scope mirrors both exactly rather than normalizing them into one shared shape.
    ///
    /// The life-stage row DISAPPEARS, not disables, for a race with exactly
    /// one life stage, and even when present its value snaps back to the pawn's ACTUAL current
    /// life stage every frame the mod's own (still-running) <c>DoWindowContents</c> draws it -- so
    /// <see cref="DescribeLifestageRow"/> re-reads it fresh on every describe rather than trusting
    /// any locally cached copy, matching that behavior instead of fighting it.
    ///
    /// Every numeric write is a RAW field write (MUTATION-C, mirroring the mod's own -- see
    /// <see cref="CharEditorBirthdayCompat"/>'s header for why a direct write between frames is
    /// safe here). Nothing is deferred at the WRITE level -- unlike the browser family, whose
    /// Confirm applies a separate selection field, here every keystroke already lands in the exact
    /// same fields <c>DoAndClose</c> reads from directly, so "deferred commit" only describes the
    /// pawn's actual age/birthday, which does not change until Confirm. The entry announcement
    /// mirrors the browser family's own wording for that reason (same key).
    ///
    /// OK invokes the dialog's own <c>DoAndClose</c> directly (no <c>OnAcceptKeyPressed</c>
    /// override exists on this dialog -- see <see cref="CharEditorBirthdayCompat"/>); Cancel and
    /// Escape both ride the base <see cref="Window"/>'s own <c>OnCancelKeyPressed</c> (the dialog
    /// sets <c>closeOnCancel = true</c> with no override, so this scope does not need to override
    /// <see cref="OwnsCancel"/> -- the same reasoning <see cref="VfPainterScope"/> relies on for
    /// its own absorbing dialog).
    /// </summary>
    internal sealed class CharEditorBirthdayScope : ScreenScope
    {
        private const int YearRow = 0;
        private const int QuadrumRow = 1;
        private const int DayRow = 2;
        private const int HourRow = 3;
        private const int BioYearRow = 4;
        private const int BioQuadrumRow = 5;
        private const int BioDayRow = 6;
        private const int BioHourRow = 7;
        private const int LifestageRow = 8;

        // Exact ranges harvested from DialogChangeBirthday's own Listing_X.AddIntSection calls:
        // chronological day is 1-15, biological day is 0-14; do not normalize them into one range.
        private const int YearMin = -9999;
        private const int YearMax = 5500;
        private const int QuadrumMin = 0;
        private const int QuadrumMax = 3;
        private const int DayMin = 1;
        private const int DayMax = 15;
        private const int HourMin = 0;
        private const int HourMax = 23;
        private const int BioQuadrumMin = 0;
        private const int BioQuadrumMax = 3;
        private const int BioDayMin = 0;
        private const int BioDayMax = 14;
        private const int BioHourMin = 0;
        private const int BioHourMax = 23;

        private readonly Window dialog;
        private readonly Pawn pawn;
        private readonly bool hasLifestageRow;
        private readonly TextFieldEditSession editSession = new TextFieldEditSession();
        private readonly List<ScreenAction> actions = new List<ScreenAction>();

        private bool announcedOpen;

        public CharEditorBirthdayScope(Window dialog, Pawn pawn)
        {
            this.dialog = dialog;
            this.pawn = pawn;
            hasLifestageRow = CharEditorBirthdayCompat.MaxLifestage(dialog) > 0;
            // First-letter mnemonic on "OK".Translate().
            Claim("charEdBirthday.confirm", e => PerformConfirm());

            RegisterPopTeardown(editSession.CancelIfActive);
        }

        public override string Name => "char-editor-birthday";

        protected internal override Window OwnedWindow => dialog;

        public override void OnFocus()
        {
            base.OnFocus();
            if (announcedOpen)
                return;
            announcedOpen = true;
            TolkHelper.SpeakData("RimWorldAccess.CharEd.Browser.ChangesApplyOnConfirm".Translate().ToString());
            AnnounceCurrentItem();
        }

        // ------------------------------------------------------------------
        // Content region.
        // ------------------------------------------------------------------

        protected override int ContentRegionCount => 1;

        protected override string ContentRegionName(int region) =>
            "RimWorldAccess.CharEd.Birthday.FieldsRegion".Translate().ToString();

        protected override int ContentItemCount(int region) => hasLifestageRow ? 9 : 8;

        /// <summary>Both dialogs draw their own real Widgets.ButtonText for OK -- blanket capture would duplicate it alongside DeclaredActions.</summary>
        protected override bool CaptureWindowButtons => false;

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            switch (index)
            {
                case YearRow: return DescribeStepper(CharEditorBirthdayCompat.YearLabel, CharEditorBirthdayCompat.GetYear(dialog), YearMin, YearMax);
                case QuadrumRow: return DescribeStepper(CharEditorBirthdayCompat.QuadrumLabel, CharEditorBirthdayCompat.GetQuadrum(dialog), QuadrumMin, QuadrumMax);
                case DayRow: return DescribeStepper(CharEditorBirthdayCompat.DayLabel, CharEditorBirthdayCompat.GetDay(dialog), DayMin, DayMax);
                case HourRow: return DescribeStepper(CharEditorBirthdayCompat.HourLabel, CharEditorBirthdayCompat.GetHour(dialog), HourMin, HourMax);
                case BioYearRow: return DescribeStepper(CharEditorBirthdayCompat.YearsLabel, CharEditorBirthdayCompat.GetBioYear(dialog), 0, BioYearMax());
                case BioQuadrumRow: return DescribeStepper(CharEditorBirthdayCompat.QuartalsLabel, CharEditorBirthdayCompat.GetBioQuadrum(dialog), BioQuadrumMin, BioQuadrumMax);
                case BioDayRow: return DescribeStepper(CharEditorBirthdayCompat.DaysLabel, CharEditorBirthdayCompat.GetBioDay(dialog), BioDayMin, BioDayMax);
                case BioHourRow: return DescribeStepper(CharEditorBirthdayCompat.HoursLabel, CharEditorBirthdayCompat.GetBioHour(dialog), BioHourMin, BioHourMax);
                case LifestageRow: return DescribeLifestageRow();
                default: return new ElementDescription();
            }
        }

        // Enter opens the exact-entry editor, so these rows keep Enter (DefaultAcceptInertness).
        private static ElementDescription DescribeStepper(string label, int value, int min, int max)
        {
            var d = new ElementDescription();
            d.Label = label;
            d.Role = ElementRole.Stepper;
            d.Value = value.ToString();
            d.AtMinimum = value <= min;
            d.AtMaximum = value >= max;
            d.EntersEditOnAccept = true;
            return d;
        }

        /// <summary>Re-reads the pawn's own life-expectancy stat fresh (race never changes mid-dialog, but the read is cheap and matches the mod's own per-frame recompute).</summary>
        private int BioYearMax()
        {
            return pawn?.RaceProps != null ? (int)pawn.RaceProps.lifeExpectancy + 30 : 0;
        }

        /// <summary>See class remarks: the field snaps to the pawn's actual current life stage every frame, so read it fresh rather than trust a cached copy.</summary>
        private ElementDescription DescribeLifestageRow()
        {
            int max = CharEditorBirthdayCompat.MaxLifestage(dialog);
            int value = CharEditorBirthdayCompat.GetLifestage(dialog);
            var d = new ElementDescription();
            d.Label = CharEditorBirthdayCompat.LifestageLabel;
            d.Role = ElementRole.Stepper;
            d.Value = value.ToString();
            d.AtMinimum = value <= 0;
            d.AtMaximum = value >= max;
            d.EntersEditOnAccept = true;
            return d;
        }

        // ------------------------------------------------------------------
        // Left/Right: step by one within range.
        // ------------------------------------------------------------------

        protected override bool CanAdjustContentItem(int region, int index) => true;

        protected override void AdjustContentItem(int region, int index, int direction)
        {
            switch (index)
            {
                case YearRow: StepInt(CharEditorBirthdayCompat.GetYear(dialog), YearMin, YearMax, direction, v => CharEditorBirthdayCompat.SetYear(dialog, v)); break;
                case QuadrumRow: StepInt(CharEditorBirthdayCompat.GetQuadrum(dialog), QuadrumMin, QuadrumMax, direction, v => CharEditorBirthdayCompat.SetQuadrum(dialog, v)); break;
                case DayRow: StepInt(CharEditorBirthdayCompat.GetDay(dialog), DayMin, DayMax, direction, v => CharEditorBirthdayCompat.SetDay(dialog, v)); break;
                case HourRow: StepInt(CharEditorBirthdayCompat.GetHour(dialog), HourMin, HourMax, direction, v => CharEditorBirthdayCompat.SetHour(dialog, v)); break;
                case BioYearRow: StepInt(CharEditorBirthdayCompat.GetBioYear(dialog), 0, BioYearMax(), direction, v => CharEditorBirthdayCompat.SetBioYear(dialog, v)); break;
                case BioQuadrumRow: StepInt(CharEditorBirthdayCompat.GetBioQuadrum(dialog), BioQuadrumMin, BioQuadrumMax, direction, v => CharEditorBirthdayCompat.SetBioQuadrum(dialog, v)); break;
                case BioDayRow: StepInt(CharEditorBirthdayCompat.GetBioDay(dialog), BioDayMin, BioDayMax, direction, v => CharEditorBirthdayCompat.SetBioDay(dialog, v)); break;
                case BioHourRow: StepInt(CharEditorBirthdayCompat.GetBioHour(dialog), BioHourMin, BioHourMax, direction, v => CharEditorBirthdayCompat.SetBioHour(dialog, v)); break;
                case LifestageRow:
                {
                    int max = CharEditorBirthdayCompat.MaxLifestage(dialog);
                    int current = CharEditorBirthdayCompat.GetLifestage(dialog);
                    StepInt(current, 0, max, direction, v => CharEditorBirthdayCompat.SetLifestage(dialog, v));
                    break;
                }
                default: return;
            }
            SoundDefOf.DragSlider.PlayOneShotOnCamera();
            AnnounceCurrentItem();
        }

        private static void StepInt(int current, int min, int max, int direction, Action<int> apply)
        {
            int next = current + (direction > 0 ? 1 : -1);
            if (next < min) next = min;
            if (next > max) next = max;
            apply(next);
        }

        // ------------------------------------------------------------------
        // Enter: exact-entry sessions.
        // ------------------------------------------------------------------

        protected override void ActivateContentItem(int region, int index)
        {
            switch (index)
            {
                case YearRow: OpenEntry(CharEditorBirthdayCompat.YearLabel, CharEditorBirthdayCompat.GetYear(dialog), YearMin, YearMax, v => CharEditorBirthdayCompat.SetYear(dialog, v), signed: true); break;
                case QuadrumRow: OpenEntry(CharEditorBirthdayCompat.QuadrumLabel, CharEditorBirthdayCompat.GetQuadrum(dialog), QuadrumMin, QuadrumMax, v => CharEditorBirthdayCompat.SetQuadrum(dialog, v)); break;
                case DayRow: OpenEntry(CharEditorBirthdayCompat.DayLabel, CharEditorBirthdayCompat.GetDay(dialog), DayMin, DayMax, v => CharEditorBirthdayCompat.SetDay(dialog, v)); break;
                case HourRow: OpenEntry(CharEditorBirthdayCompat.HourLabel, CharEditorBirthdayCompat.GetHour(dialog), HourMin, HourMax, v => CharEditorBirthdayCompat.SetHour(dialog, v)); break;
                case BioYearRow: OpenEntry(CharEditorBirthdayCompat.YearsLabel, CharEditorBirthdayCompat.GetBioYear(dialog), 0, BioYearMax(), v => CharEditorBirthdayCompat.SetBioYear(dialog, v)); break;
                case BioQuadrumRow: OpenEntry(CharEditorBirthdayCompat.QuartalsLabel, CharEditorBirthdayCompat.GetBioQuadrum(dialog), BioQuadrumMin, BioQuadrumMax, v => CharEditorBirthdayCompat.SetBioQuadrum(dialog, v)); break;
                case BioDayRow: OpenEntry(CharEditorBirthdayCompat.DaysLabel, CharEditorBirthdayCompat.GetBioDay(dialog), BioDayMin, BioDayMax, v => CharEditorBirthdayCompat.SetBioDay(dialog, v)); break;
                case BioHourRow: OpenEntry(CharEditorBirthdayCompat.HoursLabel, CharEditorBirthdayCompat.GetBioHour(dialog), BioHourMin, BioHourMax, v => CharEditorBirthdayCompat.SetBioHour(dialog, v)); break;
                case LifestageRow:
                {
                    int max = CharEditorBirthdayCompat.MaxLifestage(dialog);
                    OpenEntry(CharEditorBirthdayCompat.LifestageLabel, CharEditorBirthdayCompat.GetLifestage(dialog), 0, max, v => CharEditorBirthdayCompat.SetLifestage(dialog, v));
                    break;
                }
            }
        }

        /// <summary>Every field here shares one exit voice -- the row re-announced from the refreshed model.</summary>
        private void OpenEntry(string label, int current, int min, int max, Action<int> apply, bool signed = false)
        {
            CharEdNumericEntry.OpenInt(editSession, label, current, min, max, apply,
                onExit: () => { RefreshModel(); AnnounceCurrentItem(); }, signed: signed);
        }

        // ------------------------------------------------------------------
        // Buttons region.
        // ------------------------------------------------------------------

        /// <summary>Shift+Enter presses OK from anywhere on this screen: the one-chord proceed.</summary>
        protected override string DefaultAcceptActionId
        {
            get { return "charEdBirthday.confirm"; }
        }

        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                actions.Clear();
                actions.Add(new ScreenAction("OK".Translate(), PerformConfirm, "charEdBirthday.confirm"));
                actions.Add(new ScreenAction("CancelButton".Translate(), PerformCancel, SharedMenuGrammar.Cancel));
                return actions;
            }
        }

        /// <summary>
        /// Vehicle A: the dialog's own DoAndClose. Announces once, gated on the PENDING biological
        /// year, that confirming will clear the adulthood backstory -- the mod's own rule fires
        /// only when the submitted biological year is under 18 (DialogChangeBirthday.DoAndClose,
        /// `if (iSelectedBioYear &lt; 18 &amp;&amp; SelectedPawn.HasStoryTracker())`).
        /// </summary>
        private void PerformConfirm()
        {
            bool willClearAdulthood = pawn?.story != null && CharEditorBirthdayCompat.GetBioYear(dialog) < 18;
            if (!CharEditorBirthdayCompat.Confirm(dialog))
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }
            if (willClearAdulthood)
            {
                TolkHelper.SpeakData("RimWorldAccess.CharEd.Birthday.ClearsAdulthood".Translate().ToString());
            }
        }

        private void PerformCancel()
        {
            dialog.OnCancelKeyPressed();
        }
    }
}
