#if DEBUG
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// DEBUG-only completeness audit for the custom-difficulty catalog
    /// (DifficultySettingsHelper.BuildSections), compiled out of Release like the
    /// dev bridge and InspectionSelfAudit. Reflects every public instance field on
    /// vanilla's <see cref="Difficulty"/> and verifies our catalog exposes each one
    /// (via DifficultySetting.CoveredField, set by the setting's own constructor —
    /// never by matching translated label text). A future game version adding a new
    /// Difficulty field trips this oracle instead of silently going unexposed.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class DifficultyCatalogSelfAudit
    {
        /// <summary>
        /// Fields covered by a catalog section that's gated behind a DLC ModsConfig
        /// flag (see DifficultySettingsHelper.BuildSections) — the census below only
        /// sees whatever DLCs are active in THIS session, so these are whitelisted
        /// by construction rather than flagged as missing on a non-full-DLC install.
        /// </summary>
        private static readonly Dictionary<string, string> dlcGatedWhitelist =
            new Dictionary<string, string>
            {
                { "fishingYieldFactor", "Economy section entry is Odyssey-gated." },
                { "wastepackInfestationChanceFactor", "Threats section entry is Biotech-gated." },
                { "lowPopConversionBoost", "Ideology section entry is Ideology-gated." },
                { "childShamblersAllowed", "Children section entry requires Biotech AND Anomaly." },
                { "anomalyThreatsInactiveFraction", "Anomaly section entry (BuildAnomalySliders) is Anomaly-gated." },
                { "anomalyThreatsActiveFraction", "Anomaly section entry (BuildAnomalySliders) is Anomaly-gated." },
                { "overrideAnomalyThreatsFraction", "Anomaly section entry (BuildAnomalySliders) is Anomaly-gated." },
                { "studyEfficiencyFactor", "Anomaly section entry (BuildAnomalySliders) is Anomaly-gated." },
                // Verified against decompiled StorytellerUI.DrawCustomLeft/DrawCustomRight and
                // Dialog_AdvancedGameConfig: neither draws a control for this field anywhere.
                // It is set only via DifficultyDef presets (Difficulty.cs:109, default 70f) and
                // Scribe save/load (Difficulty.cs:367); MinThreatPointsCeiling's curve consumes
                // it internally (Difficulty.cs:127,458-461) but no vanilla UI ever exposes a
                // widget for it. Inventing a control here would be a control vanilla doesn't
                // have, so it's whitelisted instead per the mutation doctrine's "don't invent"
                // rule.
                { "minThreatPointsRangeCeiling", "Never drawn by vanilla's custom-difficulty UI (StorytellerUI/Dialog_AdvancedGameConfig) -- preset/save-only field, no control exists to mirror." },
            };

        static DifficultyCatalogSelfAudit()
        {
            try
            {
                RunCatalogCensus();
            }
            catch (Exception ex)
            {
                ModLogger.Error($"Difficulty catalog self-audit failed: {ex}");
            }
        }

        /// <summary>
        /// Audits every public instance field on <see cref="Difficulty"/> against the
        /// catalog DifficultySettingsHelper.BuildSections produces. Returns the
        /// uncovered field names (empty when clean) so the dev bridge can re-run it
        /// on demand.
        /// </summary>
        public static List<string> RunCatalogCensus()
        {
            var sections = DifficultySettingsHelper.BuildSections(
                new Difficulty(), onReset: null, onAnomalyPlaystyleChanged: null, isCharGen: true);

            var covered = new HashSet<string>(
                sections.SelectMany(s => s.Settings)
                    .Select(setting => setting.CoveredField)
                    .Where(f => !string.IsNullOrEmpty(f)));

            var failures = new List<string>();
            foreach (FieldInfo field in typeof(Difficulty).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (covered.Contains(field.Name))
                    continue;
                if (dlcGatedWhitelist.ContainsKey(field.Name))
                    continue;
                failures.Add(field.Name);
            }

            if (failures.Count > 0)
                ModLogger.Warning(
                    "[DifficultyCatalogSelfAudit] Difficulty fields with no catalog entry "
                    + "(add a DifficultySliderSetting/DifficultyCheckboxSetting or whitelist "
                    + "with a reason): " + string.Join(", ", failures));
            else
                ModLogger.Msg("[DifficultyCatalogSelfAudit] catalog census clean: every public "
                    + "Difficulty field resolves to a catalog entry or a whitelisted DLC gate.");

            return failures;
        }
    }
}
#endif
