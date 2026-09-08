using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using RimWorld;
using RimWorldAccess.Shell;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Keyboard-accessible driver for the Archonexus relocation selection screen
    /// (Dialog_ChooseThingsForNewColony). Vanilla draws four sections in one scrolled column —
    /// People / Animals / Relics / Items — which
    /// <see cref="RimWorldAccess.Shell.ArchonexusColonyScope"/> presents as one content region each;
    /// empty sections are skipped, matching vanilla's <c>count &gt; 0</c> draw gate. Accept is gated
    /// solely by the dialog's own AcceptanceReport, so relocating with more than the allowed number
    /// of any category is structurally impossible; on rejection the red reason is announced.
    /// Cursors, per-section typeaheads and the announcements live on that scope; what stays here is
    /// the section/entry data, the toggle mutation, Accept, the info-card and pawn-info entry points,
    /// and the status text.
    /// </summary>
    public static class ArchonexusColonyState
    {
        public enum Section { Colonists, Animals, Relics, Items }

        public static bool IsActive { get; private set; }

        private static Dialog_ChooseThingsForNewColony dialog;

        // Tab order is fixed (matches vanilla's draw order). Only non-empty sections appear.
        private static readonly Section[] AllSections = { Section.Colonists, Section.Animals, Section.Relics, Section.Items };
        private static List<Section> sections = new List<Section>();

        private static readonly Dictionary<Section, List<Thing>> entries = new Dictionary<Section, List<Thing>>();

        #region Reflection cache

        private static readonly Type DialogType = typeof(Dialog_ChooseThingsForNewColony);
        private static readonly FieldInfo MaxColonistsField = AccessTools.Field(DialogType, "maxColonists");
        private static readonly FieldInfo MaxAnimalsField = AccessTools.Field(DialogType, "maxAnimals");
        private static readonly FieldInfo MaxRelicsField = AccessTools.Field(DialogType, "maxRelics");
        private static readonly FieldInfo MaxItemsField = AccessTools.Field(DialogType, "maxItems");
        private static readonly FieldInfo ColonistsField = AccessTools.Field(DialogType, "colonists");
        private static readonly FieldInfo AnimalsField = AccessTools.Field(DialogType, "animals");
        private static readonly FieldInfo RelicsField = AccessTools.Field(DialogType, "relics");
        private static readonly FieldInfo ItemsField = AccessTools.Field(DialogType, "items");
        private static readonly FieldInfo SelectedField = AccessTools.Field(DialogType, "selected");
        private static readonly FieldInfo SelectedItemCountField = AccessTools.Field(DialogType, "selectedItemCount");
        private static readonly FieldInfo ItemAllowedStackCountField = AccessTools.Field(DialogType, "itemArchonexusAllowedStackCount");
        private static readonly PropertyInfo AcceptanceReportProp = AccessTools.Property(DialogType, "AcceptanceReport");
        private static readonly PropertyInfo ColonistCountProp = AccessTools.Property(DialogType, "ColonistCount");
        private static readonly PropertyInfo AnimalCountProp = AccessTools.Property(DialogType, "AnimalCount");
        private static readonly PropertyInfo RelicCountProp = AccessTools.Property(DialogType, "RelicCount");
        private static readonly PropertyInfo SlaveCountProp = AccessTools.Property(DialogType, "SlaveCount");
        private static readonly MethodInfo ConfirmConsequencesMethod = AccessTools.Method(DialogType, "ConfirmArchonexusSettlementConsequences");

        #endregion

        #region Lifecycle

        public static void EnsureOpen(Dialog_ChooseThingsForNewColony d)
        {
            // Reference equality alone (see IdeoLoadState.EnsureOpen): keying off IsActive would
            // re-announce every time vanilla closes the dialog.
            if (ReferenceEquals(dialog, d))
                return;
            dialog = d;
            IsActive = true;

            // This dialog heads the relocation chain, reached by accepting the quest from the
            // windowless quest menu, which does NOT close on accept and would linger active through
            // the whole chain — later screens without exclusive protection (the world-tile pick) would
            // then route arrows to it instead of the world map. Clear it here, silently.
            if (QuestMenuState.IsActive)
                QuestMenuState.Close(announce: false);

            RebuildAll();
        }

        public static void Close()
        {
            IsActive = false;
            sections.Clear();
            entries.Clear();
            // dialog reference is intentionally retained — see EnsureOpen.
        }

        private static void RebuildAll()
        {
            sections.Clear();
            entries.Clear();

            foreach (Section s in AllSections)
            {
                var list = GetSourceList(s);
                if (list == null || list.Count == 0)
                    continue; // matches vanilla's `count > 0` draw gate
                sections.Add(s);
                entries[s] = list;
            }
        }

        private static List<Thing> GetSourceList(Section s)
        {
            FieldInfo f;
            switch (s)
            {
                case Section.Colonists: f = ColonistsField; break;
                case Section.Animals: f = AnimalsField; break;
                case Section.Relics: f = RelicsField; break;
                case Section.Items: f = ItemsField; break;
                default: return null;
            }
            return f.GetValue(dialog) as List<Thing>;
        }

        #endregion

        #region Input

        /// <summary>True while any section holds at least one eligible thing.</summary>
        internal static bool HasSections => sections.Count > 0;

        /// <summary>The non-empty sections, in vanilla's draw order — one scope content region each.</summary>
        internal static int SectionCount => sections.Count;

        internal static int EntryCount(int section)
        {
            return section >= 0 && section < sections.Count ? entries[sections[section]].Count : 0;
        }

        /// <summary>The Thing one row stands for, or null when either index is out of range.</summary>
        internal static Thing EntryAt(int section, int index)
        {
            if (section < 0 || section >= sections.Count) return null;
            List<Thing> list = entries[sections[section]];
            return index >= 0 && index < list.Count ? list[index] : null;
        }

        internal static void ShowHealthInfo(Pawn pawn) => CaravanInputHelper.HandlePawnInfoShortcuts(KeyCode.H, pawn, true, false, false);
        internal static void ShowMoodInfo(Pawn pawn) => CaravanInputHelper.HandlePawnInfoShortcuts(KeyCode.M, pawn, true, false, false);
        internal static void ShowNeedsInfo(Pawn pawn) => CaravanInputHelper.HandlePawnInfoShortcuts(KeyCode.N, pawn, true, false, false);
        internal static void ShowGearInfo(Pawn pawn) => CaravanInputHelper.HandlePawnInfoShortcuts(KeyCode.G, pawn, true, false, false);
        internal static void ShowSkillsInfo(Pawn pawn) => CaravanInputHelper.HandlePawnInfoShortcuts(KeyCode.K, pawn, true, false, false);

        #endregion

        #region Mutation

        internal static void Toggle(int section, int index)
        {
            Thing t = EntryAt(section, index);
            if (t == null) return;
            Section s = sections[section];
            var selected = (HashSet<Thing>)SelectedField.GetValue(dialog);
            bool wasSelected = selected.Contains(t);
            if (wasSelected)
            {
                selected.Remove(t);
                if (s == Section.Items)
                    SetSelectedItemCount(GetSelectedItemCount() - 1);
                SoundDefOf.Checkbox_TurnedOff.PlayOneShotOnCamera();
            }
            else
            {
                selected.Add(t);
                if (s == Section.Items)
                    SetSelectedItemCount(GetSelectedItemCount() + 1);
                SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
            }
            AnnounceToggle(s, !wasSelected);
        }

        internal static void AttemptAccept()
        {
            var report = (AcceptanceReport)AcceptanceReportProp.GetValue(dialog);
            if (!report.Accepted)
            {
                TolkHelper.SpeakData(report.Reason, SpeechPriority.High);
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }
            int slaveCount = (int)SlaveCountProp.GetValue(dialog);
            int colonistCount = (int)ColonistCountProp.GetValue(dialog);
            bool onlySlavesSelected = slaveCount > 0 && slaveCount == colonistCount;
            var selectedList = ((HashSet<Thing>)SelectedField.GetValue(dialog)).ToList();
            // ConfirmArchonexusSettlementConsequences opens a real Dialog_MessageBox driven by
            // MessageBoxScope; on confirm it closes this dialog and fires postAccepted.
            ConfirmConsequencesMethod.Invoke(dialog, new object[] { selectedList, onlySlavesSelected });
        }

        internal static void OpenInfoCard(Thing t)
        {
            if (t == null) return;
            Find.WindowStack.Add(new Dialog_InfoCard(t));
        }

        #endregion

        #region Reflection accessors

        private static int GetMax(Section s)
        {
            switch (s)
            {
                case Section.Colonists: return (int)MaxColonistsField.GetValue(dialog);
                case Section.Animals: return (int)MaxAnimalsField.GetValue(dialog);
                case Section.Relics: return (int)MaxRelicsField.GetValue(dialog);
                case Section.Items: return (int)MaxItemsField.GetValue(dialog);
                default: return 0;
            }
        }

        private static int GetCount(Section s)
        {
            switch (s)
            {
                case Section.Colonists: return (int)ColonistCountProp.GetValue(dialog);
                case Section.Animals: return (int)AnimalCountProp.GetValue(dialog);
                case Section.Relics: return (int)RelicCountProp.GetValue(dialog);
                case Section.Items: return GetSelectedItemCount();
                default: return 0;
            }
        }

        private static int GetSelectedItemCount() => (int)SelectedItemCountField.GetValue(dialog);
        private static void SetSelectedItemCount(int v) => SelectedItemCountField.SetValue(dialog, v);

        private static int GetItemStackCount(Thing t)
        {
            if (MoveColonyUtility.IsDistinctArchonexusItem(t.def))
                return t.stackCount;
            var map = (Dictionary<Thing, int>)ItemAllowedStackCountField.GetValue(dialog);
            return map.TryGetValue(t, out int n) ? n : t.stackCount;
        }

        private static bool IsSelected(Thing t) =>
            ((HashSet<Thing>)SelectedField.GetValue(dialog)).Contains(t);

        #endregion

        #region Labels and tooltips

        private static string LabelFor(Thing t, Section s)
        {
            if (t is Pawn p && p.RaceProps?.Animal == true)
                return $"{p.LabelCap} ({p.GetGenderLabel()}, {Mathf.FloorToInt(p.ageTracker.AgeBiologicalYearsFloat)})";
            if (s == Section.Items)
                return GenLabel.ThingLabel(t, 1, includeHp: false).CapitalizeFirst();
            return t.LabelCap;
        }

        /// <summary>
        /// One section's region name: vanilla's short tab-style label plus the selected-count-of-maximum.
        /// The chassis's region frame spends Extras on the row under the cursor, so the fullness count
        /// rides the name — the one place a Tab landing, a typeahead jump and an empty-section
        /// announcement all read it from.
        /// </summary>
        internal static string SectionName(int section)
        {
            if (section < 0 || section >= sections.Count) return "";
            Section s = sections[section];
            return SectionLabel(s) + ". "
                + "RimWorldAccess.Archonexus.Colony.CountOfMax".Translate(GetCount(s), GetMax(s));
        }

        private static string SectionLabel(Section s)
        {
            // Short tab-style label used in section-switch announcements.
            switch (s)
            {
                case Section.Colonists: return "People".Translate().ToString();
                case Section.Animals: return "AnimalsLower".Translate().ToString().CapitalizeFirst();
                case Section.Relics: return GetMax(s) == 1 ? "RelicLower".Translate().ToString().CapitalizeFirst() : "RelicsLower".Translate().ToString().CapitalizeFirst();
                case Section.Items: return "ItemsLower".Translate().ToString().CapitalizeFirst();
                default: return "";
            }
        }

        #endregion

        #region Announcements

        /// <summary>
        /// The screen's opening text: title, description, how-to line and the full four-category
        /// status. The section header and focused row are not folded in — the chassis speaks both as
        /// its entry announcement right after this one.
        /// </summary>
        internal static string BuildOpeningText()
        {
            var sb = new StringBuilder();
            sb.Append("ChooseThingsForNewColonyTitle".Translate());
            sb.Append(". ").Append("ChooseThingsForNewColonyDesc".Translate());
            sb.Append(". ").Append("RimWorldAccess.Archonexus.Colony.OpenInstructions".Translate());
            sb.Append(" ").Append(BuildStatusText());
            return sb.ToString();
        }

        /// <summary>
        /// One entry row for the scope's DescribeContentItem: a <see cref="ElementRole.Checkbox"/>
        /// whose Check carries the selected state, with the item stack count riding Extras where one
        /// applies. Position is the chassis's to fill.
        /// </summary>
        internal static ElementDescription DescribeEntry(int section, int index)
        {
            ElementDescription d = new ElementDescription();
            Thing t = EntryAt(section, index);
            if (t == null) return d;
            Section s = sections[section];
            d.Label = LabelFor(t, s);
            d.Role = ElementRole.Checkbox;
            d.Check = IsSelected(t) ? CheckState.Checked : CheckState.Unchecked;
            if (s == Section.Items)
            {
                d.Extras = GetItemStackCount(t).ToString();
            }
            return d;
        }

        private static void AnnounceToggle(Section section, bool nowSelected)
        {
            // Toggle convention: announce only the changed value plus the running total, not the full
            // label. No label prefix is needed — the toggle never closes this surface, since Accept is
            // a separate action.
            ElementDescription d = new ElementDescription();
            d.Check = nowSelected ? CheckState.Checked : CheckState.Unchecked;
            string stateText = AnnouncementComposer.ComposeStateChange(d, TranslatedShellVocabulary.Instance);
            string countText = "RimWorldAccess.Archonexus.Colony.CountOfMax".Translate(GetCount(section), GetMax(section)).ToString();
            TolkHelper.SpeakData(stateText + ". " + countText);
        }

        internal static void AnnounceStatus()
        {
            TolkHelper.SpeakData(BuildStatusText(), SpeechPriority.High);
        }

        private static string BuildStatusText()
        {
            // Always reports all four categories (the dialog tracks them even with zero entries), in
            // vanilla's order.
            var sb = new StringBuilder();
            int cMax = (int)MaxColonistsField.GetValue(dialog);
            int aMax = (int)MaxAnimalsField.GetValue(dialog);
            int rMax = (int)MaxRelicsField.GetValue(dialog);
            int iMax = (int)MaxItemsField.GetValue(dialog);
            sb.Append("RimWorldAccess.Archonexus.Colony.CountOfMax".Translate(GetCount(Section.Colonists), cMax).ToString()).Append(" ").Append("People".Translate().ToString().ToLower());
            sb.Append(", ").Append("RimWorldAccess.Archonexus.Colony.CountOfMax".Translate(GetCount(Section.Animals), aMax).ToString()).Append(" ").Append("AnimalsLower".Translate());
            sb.Append(", ").Append("RimWorldAccess.Archonexus.Colony.CountOfMax".Translate(GetCount(Section.Relics), rMax).ToString()).Append(" ").Append(rMax == 1 ? "RelicLower".Translate() : "RelicsLower".Translate());
            sb.Append(", ").Append("RimWorldAccess.Archonexus.Colony.CountOfMax".Translate(GetCount(Section.Items), iMax).ToString()).Append(" ").Append("ItemsLower".Translate());
            var report = (AcceptanceReport)AcceptanceReportProp.GetValue(dialog);
            if (!report.Accepted)
                sb.Append(". ").Append(report.Reason);
            int slaveCount = (int)SlaveCountProp.GetValue(dialog);
            int colonistCount = (int)ColonistCountProp.GetValue(dialog);
            if (report.Accepted && slaveCount > 0 && slaveCount == colonistCount)
                sb.Append(". ").Append("ChooseOnlySlavesInfo".Translate());
            return sb.ToString();
        }

        #endregion
    }
}
