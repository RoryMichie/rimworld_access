using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Host-agnostic editor-region rows for a single displayed <see cref="Ideo"/>: section rows
    /// (<see cref="IdeoBuilderHelper.BuildSections"/>, with an optional section-kind filter), the
    /// description-lock row, randomize-symbols row, per-category randomize-precepts row, the fluid
    /// development-points row (+ dev "+" max-points row), the dev show-all/edit-mode toggle rows,
    /// three debug-button rows, and a trailing validation/impact status row. Shared with
    /// <c>IdeoBuilderScreenScope</c>'s editor region, whose ElementRoles, fragments and mutation
    /// vehicles these are.
    /// The host supplies everything through <see cref="Options"/>.
    /// <see cref="Options.RefreshModel"/> and <see cref="Options.AnnounceCurrentItem"/> stay two
    /// SEPARATE delegates because the row handlers need every combination of them: refresh and
    /// reannounce, refresh only (the ritual-preview toggle already speaks), reannounce only, and
    /// refresh plus a bespoke <see cref="AnnouncementComposer.ComposeStateChange"/> line.
    /// This class must never reference <c>Page_ConfigureIdeo</c> or any other window type, which
    /// is why even the worldgen host's submit-readout arrives as
    /// <see cref="Options.ValidationOrImpactText"/>.
    /// </summary>
    public sealed class IdeoEditorRegionCore
    {
        public sealed class Options
        {
            /// <summary>Which Ideo the rows describe and operate on right now.</summary>
            public Func<Ideo> DisplayIdeo;

            public Action RefreshModel;

            public Action AnnounceCurrentItem;

            /// <summary>Include the Prefs.DevMode "DEV: Show all"/"DEV: Edit mode" checkbox rows.</summary>
            public bool IncludeDevToggles;

            /// <summary>Include the fluid development-points row and the dev "+" max-points row.</summary>
            public bool IncludeFluidDevPoints;

            /// <summary>Include the three Prefs.DevMode debug-button rows.</summary>
            public bool IncludeDebugButtons;

            /// <summary>Null includes every section BuildSections returns; non-null filters by SectionKind.</summary>
            public Func<IdeoBuilderHelper.SectionKind, bool> IncludeSection;

            /// <summary>Null omits the validation/impact row; non-null supplies its Value text, re-evaluated on every rebuild.</summary>
            public Func<string> ValidationOrImpactText;
        }

        private enum RowKind
        {
            DevShowAll,
            DevEditMode,
            FluidDevPoints,
            FluidDevPointsMax,
            Section,
            DescriptionLock,
            RitualAmbiencePreview,
            RandomizeSymbols,
            RandomizePrecepts,
            DebugSinglePrecept,
            DebugTestDescriptions,
            DebugTestNames,
            ValidationOrImpact,
        }

        private sealed class Row
        {
            public RowKind Kind;
            public IdeoBuilderHelper.HubSection Section;
        }

        // MUTATION-C reflection into IdeoUIUtility's private dev-mode statics.
        private static readonly FieldInfo ShowAllField = AccessTools.Field(typeof(IdeoUIUtility), "showAll");
        private static readonly FieldInfo DevEditModeField = AccessTools.Field(typeof(IdeoUIUtility), "devEditMode");

        private readonly Options options;
        private readonly List<Row> rows = new List<Row>();

        public IdeoEditorRegionCore(Options options)
        {
            this.options = options;
        }

        public int RowCount
        {
            get { return rows.Count; }
        }

        /// <summary>Rebuilds the row list from fresh game state. Call before Describe/Activate/InspectableDefs after anything might have changed.</summary>
        public void Rebuild()
        {
            rows.Clear();
            Ideo displayIdeo = options.DisplayIdeo();
            if (displayIdeo == null)
            {
                return;
            }

            // Page-global, not tied to which ideo is displayed: DoIdeoListAndDetails draws these
            // once, above the list/details split (decompiled :252-256).
            if (options.IncludeDevToggles && Prefs.DevMode)
            {
                rows.Add(new Row { Kind = RowKind.DevShowAll });
                rows.Add(new Row { Kind = RowKind.DevEditMode });
            }

            // Keyed off the DISPLAYED ideo's own Fluid flag (decompiled DoIdeoDetails :552-555),
            // independent of which page is open: a foreign NPC ideo can be fluid while browsing
            // the Fixed page, and vice versa.
            if (options.IncludeFluidDevPoints && displayIdeo.Fluid && displayIdeo.development != null)
            {
                rows.Add(new Row { Kind = RowKind.FluidDevPoints });
                // The dev "+" button requires the DEV Edit-mode checkbox on, not merely
                // Prefs.DevMode (decompiled :983).
                if (IdeoUIUtility.DevEditMode && !displayIdeo.development.CanReformNow)
                {
                    rows.Add(new Row { Kind = RowKind.FluidDevPointsMax });
                }
            }

            // Every section is fully editable regardless of ownership: no Disabled forcing here.
            List<IdeoBuilderHelper.HubSection> sections = IdeoBuilderHelper.BuildSections(displayIdeo);
            for (int i = 0; i < sections.Count; i++)
            {
                IdeoBuilderHelper.HubSection section = sections[i];
                // A mod-added section has no SectionKind of its own, so it is never offered to a
                // host's kind filter: those filters name vanilla facets a host drops.
                if (section.PreceptClass == null && options.IncludeSection != null && !options.IncludeSection(section.Kind))
                {
                    continue;
                }
                rows.Add(new Row { Kind = RowKind.Section, Section = section });

                if (section.Kind == IdeoBuilderHelper.SectionKind.Description)
                {
                    rows.Add(new Row { Kind = RowKind.DescriptionLock });
                }
                if (section.Kind == IdeoBuilderHelper.SectionKind.Color)
                {
                    // Vanilla wraps its ritual-ambience-preview button in
                    // `if (Current.ProgramState == ProgramState.Entry)` (decompiled
                    // IdeoUIUtility.cs:659-665): worldgen-only, never drawn by an in-game host, so
                    // an in-game host reusing this core must not offer the row either.
                    if (Current.ProgramState == ProgramState.Entry && displayIdeo.SoundOngoingRitual != null)
                    {
                        rows.Add(new Row { Kind = RowKind.RitualAmbiencePreview });
                    }
                    rows.Add(new Row { Kind = RowKind.RandomizeSymbols });
                }
                if (section.Kind == IdeoBuilderHelper.SectionKind.Precepts)
                {
                    rows.Add(new Row { Kind = RowKind.RandomizePrecepts });
                }
            }

            // Displayed-ideo-scoped (decompiled DoIdeoDetails :579-582: `DoDebugButtons(..., ideo)`
            // where `ideo` is whichever one is being shown), Prefs.DevMode gated only.
            if (options.IncludeDebugButtons && Prefs.DevMode)
            {
                rows.Add(new Row { Kind = RowKind.DebugSinglePrecept });
                rows.Add(new Row { Kind = RowKind.DebugTestDescriptions });
                rows.Add(new Row { Kind = RowKind.DebugTestNames });
            }

            if (options.ValidationOrImpactText != null)
            {
                rows.Add(new Row { Kind = RowKind.ValidationOrImpact });
            }
        }

        public ElementDescription Describe(int index)
        {
            var d = new ElementDescription();
            if (index < 0 || index >= rows.Count)
            {
                return d;
            }
            Row row = rows[index];
            Ideo displayIdeo = options.DisplayIdeo();
            switch (row.Kind)
            {
                case RowKind.Section:
                    IdeoBuilderHelper.HubSection s = row.Section;
                    d.Label = s.Label;
                    d.Value = s.ValueSummary;
                    d.Role = ElementRole.Button;
                    d.Disabled = s.Disabled;
                    if (s.Disabled && !string.IsNullOrEmpty(s.DisabledReason))
                    {
                        d.Extras = s.DisabledReason;
                    }
                    if (s.InspectableDefs != null && s.InspectableDefs.Count > 0)
                    {
                        d.Extras = (d.Extras ?? "") + (string)"RimWorldAccess.InfoCard.Inspectable".Translate();
                    }
                    return d;
                case RowKind.DevShowAll:
                    d.Label = "DEV: Show all"; // l10n-exempt: dev-only diagnostic label, matches IdeoUIUtility's own unlocalized dev checkbox text (the EntityCodexState/FactionTabState/StylingStationState precedent)
                    d.Role = ElementRole.Checkbox;
                    d.Check = (ShowAllField != null && (bool)ShowAllField.GetValue(null)) ? CheckState.Checked : CheckState.Unchecked;
                    return d;
                case RowKind.DevEditMode:
                    d.Label = "DEV: Edit mode"; // l10n-exempt: dev-only diagnostic label, matches vanilla's own unlocalized dev checkbox text
                    d.Role = ElementRole.Checkbox;
                    d.Check = (DevEditModeField != null && (bool)DevEditModeField.GetValue(null)) ? CheckState.Checked : CheckState.Unchecked;
                    return d;
                case RowKind.FluidDevPoints:
                    d.Label = (string)"CurrentDevelopmentPoints".Translate();
                    d.Value = displayIdeo.development.Points + " / " + displayIdeo.development.NextReformationDevelopmentPoints;
                    d.Extras = BuildFluidDevPointsTip(displayIdeo);
                    if (displayIdeo.development.CanReformNow)
                    {
                        d.Role = ElementRole.Button;
                        d.Extras += " " + (string)"RimWorldAccess.Ideology.PressEnterToReform".Translate();
                    }
                    else
                    {
                        d.Role = ElementRole.None;
                        d.ReadOnly = true;
                    }
                    return d;
                case RowKind.FluidDevPointsMax:
                    d.Label = (string)"RimWorldAccess.Ideology.Builder.DevMaxPoints".Translate();
                    d.Role = ElementRole.Button;
                    return d;
                case RowKind.DescriptionLock:
                    d.Label = (string)"RimWorldAccess.Ideology.Builder.DescriptionLock".Translate();
                    d.Role = ElementRole.Checkbox;
                    d.Check = displayIdeo.descriptionLocked ? CheckState.Checked : CheckState.Unchecked;
                    d.Extras = ((string)"LockButtonDesc".Translate()) + " "
                        + ((string)(displayIdeo.descriptionLocked ? "LockInOn" : "LockInOff").Translate(
                            (string)"Narrative".Translate(), (string)"NarrativeLower".Translate()));
                    return d;
                case RowKind.RitualAmbiencePreview:
                    d.Label = (string)"RitualAmbienceSound".Translate();
                    d.Role = ElementRole.Checkbox;
                    d.Check = IdeoEditorCommands.RitualPreviewPlaying ? CheckState.Checked : CheckState.Unchecked;
                    d.Extras = (string)"TipPreviewRitualAmbienceSound".Translate();
                    return d;
                case RowKind.RandomizeSymbols:
                    d.Label = (string)"RandomizeSymbols".Translate();
                    d.Role = ElementRole.Button;
                    d.Extras = "RandomizeSymbolsTooltip".Translate().Resolve().StripTags();
                    return d;
                case RowKind.RandomizePrecepts:
                    d.Label = (string)"RandomizePrecepts".Translate();
                    d.Role = ElementRole.Button;
                    return d;
                case RowKind.DebugSinglePrecept:
                    d.Label = "DEV: Single precept"; // l10n-exempt: dev-only diagnostic label, mirrors IdeoUIUtility.DoDebugButtons' own unlocalized button text verbatim
                    d.Role = ElementRole.Button;
                    return d;
                case RowKind.DebugTestDescriptions:
                    d.Label = "DEV: test descriptions..."; // l10n-exempt: dev-only diagnostic label, mirrors IdeoUIUtility.DoDebugButtons' own unlocalized button text verbatim
                    d.Role = ElementRole.Button;
                    return d;
                case RowKind.DebugTestNames:
                    d.Label = "DEV: Test names..."; // l10n-exempt: dev-only diagnostic label, mirrors IdeoUIUtility.DoDebugButtons' own unlocalized button text verbatim
                    d.Role = ElementRole.Button;
                    return d;
                case RowKind.ValidationOrImpact:
                    d.Label = (string)"RimWorldAccess.Ideology.Builder.StatusLabel".Translate();
                    d.Role = ElementRole.None;
                    d.ReadOnly = true;
                    d.Value = options.ValidationOrImpactText();
                    return d;
                default:
                    return d;
            }
        }

        /// <summary>Mirrors IdeologyHelper.BuildFluidSection's tooltip children (the read-only viewer's own fluid tip) so the editor's live row and the viewer agree.</summary>
        private static string BuildFluidDevPointsTip(Ideo ideo)
        {
            var sb = new StringBuilder();
            sb.Append("FluidIdeoTip".Translate().Resolve().StripTags());
            sb.Append(" ").Append("FluidIdeoTipGetPoints".Translate().Resolve().StripTags()).Append(": ");
            sb.Append("FluidIdeoTipGetPoinsByConversion".Translate().Resolve().StripTags());
            var rituals = new List<Precept_Ritual>();
            IdeoDevelopmentUtility.GetAllRitualsThatGiveDevelopmentPoints(ideo, rituals);
            foreach (Precept_Ritual ritual in rituals)
            {
                sb.Append(", ").Append(ritual.LabelCap).Append(" (").Append((string)"Ritual".Translate()).Append(")");
            }
            var questEvents = new List<HistoryEventDef>();
            IdeoDevelopmentUtility.GetAllQuestSuccessEventsThatGiveDevelopmentPoints(ideo, questEvents);
            foreach (HistoryEventDef questEvent in questEvents)
            {
                sb.Append(", ").Append(questEvent.LabelCap).Append(" (").Append((string)"QuestLower".Translate()).Append(")");
            }
            return sb.ToString();
        }

        public void Activate(int index)
        {
            if (index < 0 || index >= rows.Count)
            {
                return;
            }
            Row row = rows[index];
            Ideo displayIdeo = options.DisplayIdeo();
            switch (row.Kind)
            {
                case RowKind.Section:
                    ActivateSectionRow(row.Section, displayIdeo);
                    return;
                case RowKind.DevShowAll:
                    ToggleDevField(ShowAllField, "DEV: Show all");
                    return;
                case RowKind.DevEditMode:
                    ToggleDevField(DevEditModeField, "DEV: Edit mode");
                    return;
                case RowKind.FluidDevPoints:
                    if (displayIdeo.development.CanReformNow)
                    {
                        Find.WindowStack.Add(new Dialog_ReformIdeo(displayIdeo));
                    }
                    else
                    {
                        options.AnnounceCurrentItem();
                    }
                    return;
                case RowKind.FluidDevPointsMax:
                    if (!EditAllowed(displayIdeo))
                    {
                        return;
                    }
                    // Mirrors IdeoUIUtility.DoFluidIdeo's dev "+" button (decompiled :983-986); no
                    // gated setter exists — a raw public field write, dev-mode only, exactly as
                    // vanilla's own button performs it. Operates on the DISPLAYED ideo, matching
                    // vanilla's own `ideo.development.points = ...` (the `ideo` DoFluidIdeo param).
                    displayIdeo.development.points = displayIdeo.development.NextReformationDevelopmentPoints;
                    options.RefreshModel();
                    options.AnnounceCurrentItem();
                    return;
                case RowKind.DescriptionLock:
                    ToggleDescriptionLock(displayIdeo);
                    return;
                case RowKind.RitualAmbiencePreview:
                    // IdeoEditorCommands.ToggleRitualPreview speaks its own full announcement
                    // (Playing/Stopped) — no additional state-change speak here (one announcement
                    // per action).
                    IdeoEditorCommands.ToggleRitualPreview(displayIdeo);
                    options.RefreshModel();
                    return;
                case RowKind.RandomizeSymbols:
                    ActivateRandomizeSymbols(displayIdeo);
                    return;
                case RowKind.RandomizePrecepts:
                    ActivateRandomizePrecepts(displayIdeo);
                    return;
                case RowKind.DebugSinglePrecept:
                    ActivateDebugSinglePrecept(displayIdeo);
                    return;
                case RowKind.DebugTestDescriptions:
                    ActivateDebugTestDescriptions(displayIdeo);
                    return;
                case RowKind.DebugTestNames:
                    ActivateDebugTestNames(displayIdeo);
                    return;
                case RowKind.ValidationOrImpact:
                    options.AnnounceCurrentItem();
                    return;
            }
        }

        /// <summary>The InspectableDefs the unified Alt+I picker should offer for this row, or an empty list. Only Section rows (BuildSections' StructureMeme/NormalMemes) ever carry any.</summary>
        public List<Def> InspectableDefs(int index)
        {
            if (index < 0 || index >= rows.Count)
            {
                return new List<Def>();
            }
            Row row = rows[index];
            if (row.Kind != RowKind.Section || row.Section.InspectableDefs == null)
            {
                return new List<Def>();
            }
            return row.Section.InspectableDefs;
        }

        // ------------------------------------------------------------------
        // Focus-ring geometry for the host's details pane (see IdeoBoxDrawPatch).
        // ------------------------------------------------------------------

        /// <summary>The clipped screen rect of the box the row at <paramref name="index"/> points at, or empty when it points at none.</summary>
        internal Rect RowScreenRect(int index)
        {
            return IdeoBoxDrawPatch.ScreenRectFor(RowRingIdentity(index));
        }

        /// <summary>The same box's raw view-space rect, which is what the details pane's scroll follow clamps against.</summary>
        internal Rect? RowRawRect(int index)
        {
            return IdeoBoxDrawPatch.RawRectFor(RowRingIdentity(index));
        }

        /// <summary>
        /// The precept, meme, or deity a row's ring belongs on. The structure meme resolves through
        /// the meme family like any other now that <see cref="IdeoBoxDrawPatch.RecordNameIcons"/>
        /// records the icon <c>DoName</c> draws inline for it; the dev rows still ring nothing. A
        /// section resolves to its FIRST box (the W3-D many-to-one precedent) rather than a
        /// fabricated band around the whole section, most of which would sit off-screen anyway.
        /// </summary>
        private object RowRingIdentity(int index)
        {
            if (index < 0 || index >= rows.Count)
            {
                return null;
            }
            Ideo ideo = options.DisplayIdeo();
            if (ideo == null)
            {
                return null;
            }
            Row row = rows[index];
            // The randomize row acts on the Precepts section, so it points at the same box.
            if (row.Kind == RowKind.RandomizePrecepts)
            {
                return FirstDrawnPrecept(ideo);
            }
            // A mod-discovered section carries no meaningful Kind (see HubSection.PreceptClass).
            if (row.Kind != RowKind.Section || row.Section.PreceptClass != null)
            {
                return null;
            }
            switch (row.Section.Kind)
            {
                case IdeoBuilderHelper.SectionKind.StructureMeme:
                    return ideo.StructureMeme;
                case IdeoBuilderHelper.SectionKind.NormalMemes:
                    return TopmostRecordedBox(ideo.memes, m => m.category != MemeCategory.Structure);
                case IdeoBuilderHelper.SectionKind.Deities:
                    IdeoFoundation_Deity deityFoundation = ideo.foundation as IdeoFoundation_Deity;
                    return deityFoundation == null ? null : TopmostRecordedBox(deityFoundation.DeitiesListForReading, null);
                case IdeoBuilderHelper.SectionKind.Precepts:
                    return FirstDrawnPrecept(ideo);
                default:
                    return null;
            }
        }

        private static Precept FirstDrawnPrecept(Ideo ideo)
        {
            return TopmostRecordedBox(ideo.PreceptsListForReading, null);
        }

        /// <summary>
        /// The candidate whose box the pass in flight drew highest. Draw order, not list order,
        /// decides which box comes first: vanilla groups precepts by issue and lays memes out in
        /// centered rows, so neither list's index 0 is reliably the topmost box. Null when the pass
        /// recorded none of them (the section is scrolled away, or vanilla drew it inline).
        /// </summary>
        private static T TopmostRecordedBox<T>(List<T> candidates, Predicate<T> include) where T : class
        {
            if (candidates == null)
            {
                return null;
            }
            T best = null;
            float bestY = 0f;
            for (int i = 0; i < candidates.Count; i++)
            {
                T candidate = candidates[i];
                if (candidate == null || (include != null && !include(candidate)))
                {
                    continue;
                }
                Rect? raw = IdeoBoxDrawPatch.RawRectFor(candidate);
                if (raw == null)
                {
                    continue;
                }
                if (best == null || raw.Value.yMin < bestY)
                {
                    best = candidate;
                    bestY = raw.Value.yMin;
                }
            }
            return best;
        }

        private void ActivateSectionRow(IdeoBuilderHelper.HubSection section, Ideo ideo)
        {
            if (section.Disabled)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.SpeakData(string.IsNullOrEmpty(section.DisabledReason)
                    ? (string)"RimWorldAccess.Ideology.Builder.Unavailable".Translate()
                    : section.DisabledReason);
                return;
            }
            try
            {
                // Operates on whichever ideo is DISPLAYED, not unconditionally the host's own
                // ideo — vanilla lets a sighted player edit any ideo browsed from the list (see
                // IdeoBuilderScreenScope's class remarks).
                IdeoBuilderSectionActions.Activate(ideo, section);
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorld Access] Error opening editor for section {section.Label}: {ex}");
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
            }
        }

        private void ToggleDevField(FieldInfo field, string label)
        {
            if (field == null)
            {
                return;
            }
            bool next = !(bool)field.GetValue(null);
            // MUTATION-C: mirrors IdeoUIUtility's own dev-mode CheckboxLabeled writes to its
            // private statics (decompiled :254-255); no gated setter exists (the
            // IdeosDuringLandingScope precedent).
            field.SetValue(null, next);
            options.RefreshModel();
            var d = new ElementDescription { Check = next ? CheckState.Checked : CheckState.Unchecked };
            TolkHelper.SpeakData(label + ", " + AnnouncementComposer.ComposeStateChange(d, TranslatedShellVocabulary.Instance));
        }

        private void ToggleDescriptionLock(Ideo ideo)
        {
            if (ideo == null)
            {
                return;
            }
            ideo.descriptionLocked = !ideo.descriptionLocked;
            (ideo.descriptionLocked ? SoundDefOf.Checkbox_TurnedOn : SoundDefOf.Checkbox_TurnedOff).PlayOneShotOnCamera();
            options.RefreshModel();
            var d = new ElementDescription { Check = ideo.descriptionLocked ? CheckState.Checked : CheckState.Unchecked };
            TolkHelper.SpeakData(AnnouncementComposer.ComposeStateChange(d, TranslatedShellVocabulary.Instance));
        }

        private void ActivateRandomizeSymbols(Ideo ideo)
        {
            if (ideo == null || !EditAllowed(ideo))
            {
                return;
            }
            // Mirrors IdeoUIUtility.DoIdeoDetails' "RandomizeSymbols" button body verbatim
            // (decompiled :700-709).
            ideo.MakeMemeberNamePluralDirty();
            ideo.foundation.RandomizePlace();
            ideo.foundation.GenerateTextSymbols();
            ideo.foundation.GenerateLeaderTitle();
            ideo.foundation.RandomizeIcon();
            ideo.RegenerateAllPreceptNames();
            ideo.RegenerateDescription(force: true);
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
            options.RefreshModel();
            options.AnnounceCurrentItem();
        }

        private void ActivateRandomizePrecepts(Ideo ideo)
        {
            if (ideo == null || !EditAllowed(ideo))
            {
                return;
            }
            if (ideo.anyPreceptEdited)
            {
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    ((string)"ChangesRandomizePrecepts".Translate((string)"RandomizePrecepts".Translate())),
                    () => DoRandomizePrecepts(ideo)));
            }
            else
            {
                DoRandomizePrecepts(ideo);
            }
        }

        private void DoRandomizePrecepts(Ideo ideo)
        {
            ideo.foundation.RandomizePrecepts(init: true, new IdeoGenerationParms(IdeoUIUtility.FactionForRandomization(ideo)));
            ideo.RegenerateDescription();
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
            ideo.anyPreceptEdited = false;
            options.RefreshModel();
            options.AnnounceCurrentItem();
        }

        /// <summary>
        /// Public zero-arg entry points for a host that presents the three DEV debug buttons OUTSIDE
        /// this core's own row model (<see cref="Options.IncludeDebugButtons"/> false) — the in-game
        /// viewer's Buttons-region rows (vanilla draws these whenever <c>Prefs.DevMode</c>,
        /// independent of edit mode, so they must
        /// stay reachable even while this core's own row-hosting region is absent). Delegate to the
        /// exact same private implementation <see cref="Activate"/> uses internally — no duplicated
        /// logic, no behavior change for the existing row-based callers.
        /// </summary>
        public void ActivateDebugSinglePrecept()
        {
            ActivateDebugSinglePrecept(options.DisplayIdeo());
        }

        /// <summary>See <see cref="ActivateDebugSinglePrecept()"/>'s remarks.</summary>
        public void ActivateDebugTestDescriptions()
        {
            ActivateDebugTestDescriptions(options.DisplayIdeo());
        }

        /// <summary>See <see cref="ActivateDebugSinglePrecept()"/>'s remarks.</summary>
        public void ActivateDebugTestNames()
        {
            ActivateDebugTestNames(options.DisplayIdeo());
        }

        private void ActivateDebugSinglePrecept(Ideo ideo)
        {
            if (ideo == null)
            {
                return;
            }
            var floatOptions = new List<FloatMenuOption>();
            foreach (PreceptDef def in DefDatabase<PreceptDef>.AllDefsListForReading)
            {
                PreceptDef captured = def;
                string label = captured.issue.LabelCap + ": " + (captured.LabelCap.NullOrEmpty() ? captured.defName : (string)captured.LabelCap);
                floatOptions.Add(new FloatMenuOption(label, delegate
                {
                    ideo.ClearPrecepts();
                    ideo.AddPrecept(PreceptMaker.MakePrecept(captured), init: true);
                    options.RefreshModel();
                    options.AnnounceCurrentItem();
                }));
            }
            WindowlessFloatMenuState.Open(floatOptions, colonistOrders: false);
        }

        private void ActivateDebugTestDescriptions(Ideo ideo)
        {
            if (ideo == null)
            {
                return;
            }
            var sb = new StringBuilder();
            for (int i = 0; i < 100; i++)
            {
                ideo.RegenerateDescription(force: true);
                sb.Append("template: " + ideo.descriptionTemplate).AppendLine();
                sb.Append("text: " + ideo.description).AppendLine().AppendLine();
            }
            Log.Message(sb.ToString());
            options.RefreshModel();
            options.AnnounceCurrentItem();
        }

        private void ActivateDebugTestNames(Ideo ideo)
        {
            if (ideo == null)
            {
                return;
            }
            var floatOptions = new List<FloatMenuOption>
            {
                // l10n-exempt: dev-only diagnostic label, mirrors IdeoUIUtility's own
                // unlocalized dev FloatMenuOption text verbatim (IdeoUIUtility.cs:2087-2088)
                new FloatMenuOption("Ideo names (replace)", delegate
                {
                    var sb = new StringBuilder();
                    for (int j = 0; j < 200; j++)
                    {
                        ideo.foundation.GenerateTextSymbols();
                        ideo.foundation.GenerateLeaderTitle();
                        sb.AppendLine(ideo.name + "\n- " + ideo.adjective + "\n- " + ideo.memberName + "\n- " + ideo.leaderTitleMale);
                        sb.AppendLine();
                    }
                    Log.Message(sb.ToString());
                    options.RefreshModel();
                    options.AnnounceCurrentItem();
                }),
            };
            foreach (Precept precept in ideo.PreceptsListForReading)
            {
                PreceptDef def = precept.def;
                if (!(def.preceptClass == typeof(Precept_Ritual)) && !typeof(Precept_Role).IsAssignableFrom(def.preceptClass) && !(def.preceptClass == typeof(Precept_Building)))
                {
                    continue;
                }
                Precept capturedPrecept = precept;
                floatOptions.Add(new FloatMenuOption(def.issue.LabelCap + ": " + precept.LabelCap, delegate
                {
                    var sb = new StringBuilder();
                    for (int j = 0; j < 200; j++)
                    {
                        sb.AppendLine(capturedPrecept.GenerateNameRaw());
                    }
                    Log.Message(sb.ToString());
                }));
            }
            WindowlessFloatMenuState.Open(floatOptions, colonistOrders: false);
        }

        /// <summary>
        /// Mirrors <c>IdeoUIUtility.TutorAllowsInteraction(editMode)</c> for the
        /// worldgen/reform/Archonexus hosts, all of which pass GameStart (or Dev, once the DEV
        /// Edit-mode checkbox is on), never None, so there is no ownership term here at all. A future
        /// host that needs a genuine read-only (IdeoEditMode.None) mode should not mount this class at
        /// all for that ideo, rather than ask this gate to model a fourth state.
        /// </summary>
        private static bool EditAllowed(Ideo ideo)
        {
            return ideo != null && (IdeoUIUtility.DevEditMode || TutorSystem.AllowAction("ConfiguringIdeo"));
        }
    }
}
