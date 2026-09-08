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
    /// DEBUG-only completeness audit for the play-settings catalog
    /// (<see cref="PlaySettingsCatalog"/>), compiled out of Release like the dev
    /// bridge, InspectionSelfAudit and <see cref="DifficultyCatalogSelfAudit"/>,
    /// whose shape this follows exactly. Reflects every public instance bool
    /// field on vanilla's <see cref="PlaySettings"/> and verifies the catalog
    /// claims each one — either offering it in the keyboard menu or excluding it
    /// with a written reason — matched by FIELD NAME
    /// (PlaySettingsEntry/PlaySettingsExclusion.CoveredField), never by matching
    /// translated label text.
    ///
    /// The failure this exists to prevent already happened once: the menu
    /// hand-listed two of vanilla's roughly fourteen row controls, and nothing
    /// anywhere recorded that the other twelve were missing. A future game
    /// version adding a play-settings toggle now trips this oracle instead of
    /// silently drifting.
    ///
    /// <b>Why bool fields.</b> A play-settings ROW control is a
    /// <c>WidgetRow.ToggleableIcon</c> over a bool (decompiled
    /// RimWorld/PlaySettings.cs:153-206), so the bools are the auditable
    /// population. The class also carries eleven <see cref="MedicalCareCategory"/>
    /// defaults, which vanilla draws as dropdowns on the Assign tab and never in
    /// this row; rather than hand-listing those eleven names — a list that would
    /// rot exactly like the one this audit replaces — the census skips non-bool
    /// fields BY TYPE and still reports any non-bool field of an unfamiliar
    /// type, so a genuinely new non-toggle play setting is visible too.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class PlaySettingsCatalogSelfAudit
    {
        static PlaySettingsCatalogSelfAudit()
        {
            try
            {
                RunCatalogCensus();
            }
            catch (Exception ex)
            {
                ModLogger.Error($"Play-settings catalog self-audit failed: {ex}");
            }
        }

        /// <summary>
        /// Audits every public instance bool field on <see cref="PlaySettings"/>
        /// against <see cref="PlaySettingsCatalog"/>. Returns the unclaimed field
        /// names (empty when clean) so the dev bridge can re-run it on demand,
        /// matching <see cref="DifficultyCatalogSelfAudit.RunCatalogCensus"/>'s
        /// contract.
        /// </summary>
        public static List<string> RunCatalogCensus()
        {
            var offered = PlaySettingsCatalog.Offered
                .Select(entry => entry.CoveredField)
                .Where(f => !string.IsNullOrEmpty(f))
                .ToList();
            var excluded = PlaySettingsCatalog.Excluded
                .Select(entry => entry.CoveredField)
                .Where(f => !string.IsNullOrEmpty(f))
                .ToList();

            var claimed = new HashSet<string>(offered.Concat(excluded));

            // "Claimed by exactly one entry": a field listed twice — most likely
            // offered AND excluded after an edit — is a contradiction about what
            // the menu does, so it is reported rather than quietly deduplicated
            // by the HashSet above.
            var duplicates = offered.Concat(excluded)
                .GroupBy(f => f)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();
            if (duplicates.Count > 0)
                ModLogger.Warning(
                    "[PlaySettingsCatalogSelfAudit] PlaySettings fields claimed by more than one "
                    + "catalog entry (a field is offered or excluded, never both): "
                    + string.Join(", ", duplicates));

            var failures = new List<string>();
            var unfamiliarNonToggles = new List<string>();
            foreach (FieldInfo field in typeof(PlaySettings).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (field.FieldType != typeof(bool))
                {
                    // See the class remarks: the medical-care defaults are the
                    // known non-toggle family and are skipped by type, not by
                    // name. Anything else non-bool is new and worth a look.
                    if (field.FieldType != typeof(MedicalCareCategory))
                        unfamiliarNonToggles.Add(field.Name + " (" + field.FieldType.Name + ")");
                    continue;
                }
                if (claimed.Contains(field.Name))
                    continue;
                failures.Add(field.Name);
            }

            if (unfamiliarNonToggles.Count > 0)
                ModLogger.Warning(
                    "[PlaySettingsCatalogSelfAudit] PlaySettings gained non-bool fields this audit "
                    + "does not recognise; check whether the play-settings row now draws a control "
                    + "for them: " + string.Join(", ", unfamiliarNonToggles));

            if (failures.Count > 0)
                ModLogger.Warning(
                    "[PlaySettingsCatalogSelfAudit] PlaySettings bool fields with no catalog entry "
                    + "(add a PlaySettingsEntry to PlaySettingsCatalog.Offered, or a "
                    + "PlaySettingsExclusion with a reason): " + string.Join(", ", failures));
            else
                ModLogger.Msg("[PlaySettingsCatalogSelfAudit] catalog census clean: every public "
                    + "PlaySettings bool field is offered by the menu or excluded with a reason.");

            return failures;
        }
    }
}
#endif
