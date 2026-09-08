using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorldAccess.Shell;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Keyboard navigation for Dialog_AnomalySettings, the popup opened from the
    /// "AnomalySettings..." button on Page_SelectStoryteller.
    ///
    /// Mirrors vanilla's structure: a single scrolling list, presented by
    /// <see cref="RimWorldAccess.Shell.AnomalySettingsScope"/> as its one content region. Row 0 is
    /// the playstyle combo box; below are the conditional anomaly sliders for that playstyle. Edits
    /// go straight into the dialog's own working fields — the same fields vanilla's widgets write —
    /// so the drawn UI tracks them; they reach the underlying Difficulty only on Accept (Alt+S),
    /// vanilla's own commit point.
    /// </summary>
    public static class AnomalySettingsDialogState
    {
        private static readonly FieldInfo DifficultyField =
            AccessTools.Field(typeof(Dialog_AnomalySettings), "difficulty");
        private static readonly FieldInfo InactiveField =
            AccessTools.Field(typeof(Dialog_AnomalySettings), "anomalyThreatsInactiveFraction");
        private static readonly FieldInfo ActiveField =
            AccessTools.Field(typeof(Dialog_AnomalySettings), "anomalyThreatsActiveFraction");
        private static readonly FieldInfo StudyField =
            AccessTools.Field(typeof(Dialog_AnomalySettings), "studyEfficiencyFactor");
        private static readonly FieldInfo OverrideField =
            AccessTools.Field(typeof(Dialog_AnomalySettings), "overrideAnomalyThreatsFraction");
        private static readonly FieldInfo PlaystyleField =
            AccessTools.Field(typeof(Dialog_AnomalySettings), "anomalyPlaystyleDef");

        private static bool isActive;
        private static Dialog_AnomalySettings currentDialog;

        /// <summary>
        /// Frame number when Escape last closed the dialog. AnomalySettingsDialogPatch's Page
        /// prefix reads it to block Page_SelectStoryteller from also receiving that Cancel
        /// keystroke: Event.current.Use() does not stop HandleEventsHighPriority from firing.
        /// </summary>
        internal static int escapeHandledOnFrame = -1;

        /// <summary>
        /// True when the close came from Accept (Alt+S). The PostClose patch reads it to pick the
        /// announcement ("saved" vs "closed") and to reset the parent page's tab cursor to the
        /// Storyteller row.
        /// </summary>
        internal static bool wasAcceptClose;

        // Flat list: index 0 is the playstyle combo box, 1..n-1 the sliders visible for that
        // playstyle. Rebuilt whenever the playstyle changes.
        private static List<DifficultySetting> items = new List<DifficultySetting>();

        // The dialog's own working fields are vanilla's edit model: its widgets write them every
        // frame and only its Accept button pushes them into the Difficulty, so reading and writing
        // them directly keeps the drawn UI in step. Its constructor has already seeded them; the
        // null-dialog fallbacks below only keep an out-of-order read from throwing.
        private static AnomalyPlaystyleDef Playstyle
        {
            get => currentDialog == null ? null : PlaystyleField?.GetValue(currentDialog) as AnomalyPlaystyleDef;
            set => WriteField(PlaystyleField, value);
        }

        private static float Inactive
        {
            get => currentDialog == null ? 0f : (float)(InactiveField?.GetValue(currentDialog) ?? 0f);
            set => WriteField(InactiveField, value);
        }

        private static float Active
        {
            get => currentDialog == null ? 0f : (float)(ActiveField?.GetValue(currentDialog) ?? 0f);
            set => WriteField(ActiveField, value);
        }

        private static float Study
        {
            get => currentDialog == null ? 1f : (float)(StudyField?.GetValue(currentDialog) ?? 1f);
            set => WriteField(StudyField, value);
        }

        private static float Override
        {
            get => currentDialog == null ? 0.15f : (float)(OverrideField?.GetValue(currentDialog) ?? 0.15f);
            set => WriteField(OverrideField, value);
        }

        // MUTATION-C: mirrors the writes Dialog_AnomalySettings' own widgets perform inline
        // (DrawPlaystyles :135-139, DrawExtraSettings :161-175); no A or B vehicle exists.
        private static void WriteField(FieldInfo field, object value)
        {
            if (currentDialog != null) field?.SetValue(currentDialog, value);
        }

        private static Difficulty Difficulty =>
            currentDialog == null ? null : DifficultyField?.GetValue(currentDialog) as Difficulty;

        public static bool IsActive => isActive;

        /// <summary>The settings rows, in vanilla's own draw order — the scope's one content region.</summary>
        internal static int RowCount => items.Count;

        /// <summary>One settings row, or null when the index is out of range.</summary>
        internal static DifficultySetting RowAt(int index)
        {
            return index >= 0 && index < items.Count ? items[index] : null;
        }

        public static void Open(Dialog_AnomalySettings dialog)
        {
            if (dialog == null) return;
            try
            {
                currentDialog = dialog;

                // Without these, Unity IMGUI focus stalls keyboard events when this
                // absorbInputAroundWindow modal opens. closeOnAccept/closeOnCancel stop the
                // WindowStack auto-closing on Enter/Escape (close is driven via TryRemove);
                // focusWhenOpened stops Unity grabbing focus into the GUI.Window, which would blind
                // the shell dispatcher.
                dialog.closeOnAccept = false;
                dialog.closeOnCancel = false;
                dialog.focusWhenOpened = false;

                RebuildItems();
                wasAcceptClose = false;
                isActive = true;
            }
            catch (Exception ex)
            {
                Log.Error($"[AnomalySettingsDialogState] Open failed: {ex.Message}");
                Close();
            }
        }

        public static void Close()
        {
            isActive = false;
            currentDialog = null;
            items.Clear();
        }

        // ===== SCOPE ROUTERS =====
        // The keys are AnomalySettingsScope's claims, delegating to the routers below; cursor,
        // typeahead and list navigation are the shared chassis grammar. What stays here is the row
        // data, the mutations and the two texts the scope asks for.

        /// <summary>Alt+S: commit the dialog's edits to the Difficulty and close (accept).</summary>
        public static void AcceptSettings()
        {
            if (!isActive || currentDialog == null) return;
            Accept();
        }

        /// <summary>Alt+R: open the set-to-standard-playstyle preset picker; <paramref name="onPicked"/> re-reads the focused row once a pick lands.</summary>
        public static void OpenStandardPlaystylePicker(Action onPicked)
        {
            if (!isActive) return;
            OpenStandardPlaystyleMenu(onPicked);
        }

        /// <summary>Escape: discard and close the dialog (the chassis handles the search-clear Escape).</summary>
        public static void HandleCancel()
        {
            if (!isActive || currentDialog == null) return;
            CloseDialog();
        }

        /// <summary>
        /// Left/Right on one row: adjust its value; <paramref name="large"/> (Shift) steps a slider
        /// by a tenth of its positions. The playstyle combo box never reaches here.
        /// </summary>
        internal static void AdjustRow(int index, int direction, bool large)
        {
            DifficultySetting setting = RowAt(index);
            if (setting == null) return;
            if (large && setting is DifficultySliderSetting slider)
            {
                slider.AdjustByPercentOfPositions(0.1f * direction);
            }
            else
            {
                setting.Adjust(direction);
            }
            SpeakRowChange(index, setting);
        }

        /// <summary>
        /// Enter/Space on one row: the playstyle combo box opens its picker (the new state is
        /// spoken only once a pick lands); everything else toggles in place.
        /// </summary>
        internal static void ToggleRow(int index, Action onPicked)
        {
            DifficultySetting setting = RowAt(index);
            if (setting == null) return;
            if (setting is AnomalyPlaystyleSetting playstyle)
            {
                playstyle.OpenPicker(onPicked);
                return;
            }
            setting.Toggle();
            SpeakRowChange(index, setting);
        }

        /// <summary>
        /// The playstyle row's onChanged rebuilds <see cref="items"/> in place. Index 0 stays
        /// valid, but re-fetch from the list in case the rebuild swapped instances.
        /// </summary>
        private static void SpeakRowChange(int index, DifficultySetting fallback)
        {
            DifficultySetting fresh = RowAt(index);
            TolkHelper.SpeakData((fresh ?? fallback).GetAdjustmentAnnouncement());
        }

        // ===== ITEM LIST =====

        private static void RebuildItems()
        {
            items.Clear();
            if (Playstyle == null) return;

            // Playstyle row at the top (a combo box; Enter opens its picker).
            items.Add(new AnomalyPlaystyleSetting(
                getter: () => Playstyle,
                setter: v =>
                {
                    // MUTATION-C: mirrors Dialog_AnomalySettings.DrawPlaystyles' scenario gate
                    // (:125-144); the gate is inline in vanilla's radio branch with no
                    // extractable Can* twin. Vanilla posts a RejectInput message here; we speak
                    // the same refusal instead.
                    if (Find.Scenario != null && Find.Scenario.standardAnomalyPlaystyleOnly
                        && v != AnomalyPlaystyleDefOf.Standard)
                    {
                        TolkHelper.SpeakData($"{(string)"DisabledByScenario".Translate()}: {Find.Scenario.name}");
                        return;
                    }
                    Playstyle = v;
                },
                onTransitionToOverride: () =>
                {
                    // Mirror vanilla DrawPlaystyles: entering an override-style playstyle seeds the
                    // override fraction so the slider starts somewhere sensible.
                    Override = 0.15f;
                },
                onChanged: RebuildItems));

            // useEnabledConditions=false returns only the sliders relevant to the current
            // playstyle, rather than always-show plus per-row enable conditions.
            items.AddRange(DifficultySettingsHelper.BuildAnomalySliders(
                playstyleGetter: () => Playstyle,
                overrideGetter: () => Override, overrideSetter: v => Override = v,
                inactiveGetter: () => Inactive, inactiveSetter: v => Inactive = v,
                activeGetter: () => Active, activeSetter: v => Active = v,
                // MUTATION-C: mirrors Dialog_AnomalySettings.cs:164's own popup floor
                // (0f), distinct from StorytellerUI.cs:247's DrawCustomLeft floor
                // (0.1f) used by the custom-difficulty section -- this dialog IS
                // Dialog_AnomalySettings, so 0f is the correct floor here.
                activeFractionFloor: 0f,
                studyGetter: () => Study, studySetter: v => Study = v,
                useEnabledConditions: false));
        }

        // ===== FOCUS RING =====

        /// <summary>
        /// Which row of vanilla's own dialog the focused item corresponds to. The playstyle row
        /// rings the CURRENT playstyle's radio, which is what a sighted player sees selected. A
        /// slider vanilla is not drawing this pass names a row nobody drew and rings nothing.
        /// </summary>
        internal static ListingRingFocus CurrentListingFocus(int index)
        {
            DifficultySetting setting = isActive ? RowAt(index) : null;
            if (setting == null)
                return ListingRingFocus.None;

            if (setting is AnomalyPlaystyleSetting)
            {
                var playstyle = Playstyle;
                return playstyle == null
                    ? ListingRingFocus.None
                    : new ListingRingFocus { RowObject = playstyle };
            }

            // The Difficulty field each slider edits is the row's identity: BuildAnomalySliders
            // stamps it on the setting, and vanilla draws one caption per field.
            switch (setting.CoveredField)
            {
                case "anomalyThreatsInactiveFraction":
                    return new ListingRingFocus
                    {
                        RowKey = AnomalyRowKeys.ThreatsInactive,
                        LabelTripwire = FrequencyCaption("Difficulty_AnomalyThreatsInactive_Label", Inactive),
                    };
                case "anomalyThreatsActiveFraction":
                    return new ListingRingFocus
                    {
                        RowKey = AnomalyRowKeys.ThreatsActive,
                        LabelTripwire = FrequencyCaption("Difficulty_AnomalyThreatsActive_Label", Active),
                    };
                case "overrideAnomalyThreatsFraction":
                    return new ListingRingFocus
                    {
                        RowKey = AnomalyRowKeys.ThreatsOverride,
                        LabelTripwire = FrequencyCaption("Difficulty_AnomalyThreats_Label", Override),
                    };
                case "studyEfficiencyFactor":
                    // No frequency tail on this one (Dialog_AnomalySettings.cs:174).
                    return new ListingRingFocus
                    {
                        RowKey = AnomalyRowKeys.StudyEfficiency,
                        LabelTripwire = ("Difficulty_StudyEfficiency_Label".Translate() + ": " + Study.ToStringPercent()).Resolve(),
                    };
                default:
                    return ListingRingFocus.None;
            }
        }

        /// <summary>
        /// Mirrors the caption vanilla builds at Dialog_AnomalySettings.cs:160, :163 and :169,
        /// frequency word included, read through its own <c>GetFrequencyLabel</c>.
        /// </summary>
        private static string FrequencyCaption(string labelKey, float value)
        {
            return (labelKey.Translate() + ": " + value.ToStringPercent()
                + " - " + Dialog_AnomalySettings.GetFrequencyLabel(value)).Resolve();
        }

        // ===== ANNOUNCEMENTS =====

        /// <summary>
        /// Builds one row's ElementDescription via the shared DifficultySettingAdapter, so this
        /// popup speaks the same role words as the persistent custom-difficulty screen. Position is
        /// the chassis's to fill. The DisabledByScenario notice is substantive content, so it goes
        /// on the end of Extras rather than Hint — Hint is silenced by the interaction-hints
        /// setting and this must always speak.
        /// </summary>
        internal static ElementDescription DescribeRow(int index)
        {
            var d = new ElementDescription();
            DifficultySetting setting = RowAt(index);
            if (setting == null) return d;
            DifficultySettingAdapter.FillRow(d, setting);

            // For the playstyle row, append the scenario-block notice if applicable.
            if (setting is AnomalyPlaystyleSetting && Playstyle != null
                && Find.Scenario != null && Find.Scenario.standardAnomalyPlaystyleOnly
                && Playstyle != AnomalyPlaystyleDefOf.Standard)
            {
                d.Extras = $"{d.Extras}. {(string)"DisabledByScenario".Translate()}: {Find.Scenario.name}";
            }
            return d;
        }

        // ===== ACCEPT / RESET =====

        private static void Accept()
        {
            try
            {
                var difficulty = Difficulty;
                if (difficulty == null)
                {
                    TolkHelper.Speak("RimWorldAccess.AnomalySettings.CannotAcceptDifficultyNotLoaded".Loc());
                    return;
                }

                var playstyle = Playstyle;

                // MUTATION-C: mirrors Dialog_AnomalySettings.DoWindowContents'
                // Accept-button branch verbatim (overrideAnomalyThreatsFraction,
                // anomalyThreatsInactiveFraction/ActiveFraction, studyEfficiencyFactor,
                // AnomalyPlaystyleDef); vanilla writes these Difficulty fields
                // bare from the same button, no gated setter exists.
                if (playstyle != null && playstyle.overrideThreatFraction)
                    difficulty.overrideAnomalyThreatsFraction = Override;
                else
                    difficulty.overrideAnomalyThreatsFraction = null;

                difficulty.anomalyThreatsInactiveFraction = Inactive;
                difficulty.anomalyThreatsActiveFraction = Active;
                difficulty.studyEfficiencyFactor = Study;
                if (playstyle != null) difficulty.AnomalyPlaystyleDef = playstyle;

                // Accept-close: PostClose announces "saved" and bumps the parent page's tab cursor
                // back to Storyteller.
                wasAcceptClose = true;
                CloseDialog();
            }
            catch (Exception ex)
            {
                Log.Error($"[AnomalySettingsDialogState] Accept failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Closes the dialog via WindowStack.TryRemove and sets escapeHandledOnFrame, so
        /// AnomalySettingsDialogPatch can block the underlying Page_SelectStoryteller from also
        /// processing the same Cancel key this frame.
        /// </summary>
        private static void CloseDialog()
        {
            escapeHandledOnFrame = Time.frameCount;
            if (currentDialog != null)
            {
                Find.WindowStack.TryRemove(currentDialog);
            }
            Close();
        }

        private static void OpenStandardPlaystyleMenu(Action onPicked)
        {
            var options = new List<FloatMenuOption>();
            foreach (DifficultyDef d in DefDatabase<DifficultyDef>.AllDefs)
            {
                if (d.isCustom) continue;
                var captured = d;
                options.Add(new FloatMenuOption(captured.LabelCap, () =>
                {
                    Inactive = captured.anomalyThreatsInactiveFraction;
                    Active = captured.anomalyThreatsActiveFraction;
                    Study = captured.studyEfficiencyFactor;
                    Playstyle = AnomalyPlaystyleDefOf.Standard;
                    RebuildItems();
                    TolkHelper.SpeakData($"{captured.LabelCap}");
                    if (onPicked != null) onPicked();
                }));
            }
            if (options.Count == 0) return;
            WindowlessFloatMenuState.Open(options, colonistOrders: false);
            TolkHelper.Speak("SetToStandardPlaystyle".Loc());
        }
    }
}
