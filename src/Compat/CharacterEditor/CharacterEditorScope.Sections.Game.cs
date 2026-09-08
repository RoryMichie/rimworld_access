using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    internal sealed partial class CharacterEditorScope
    {
        private static void AddSection(InspectionTreeItem root, SectionKind kind, string label)
        {
            if (label.NullOrEmpty())
            {
                return;
            }
            var section = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.SubCategory,
                Label = label,
                Data = kind,
                IndentLevel = root.IndentLevel + 1,
                IsExpandable = true,
                IsExpanded = false,
                IsSectionHeader = true,
                AutoExpandForSearch = true,
                Parent = root,
            };
            root.Children.Add(section);
        }

        /// <summary>
        /// Page Up/Down step between groups: top-level section headers, subsection headers, and —
        /// under submenu-style navigation, where an expanded group's header leaves the visible list
        /// entirely — the first child of such a hidden group, the only visible row that can stand
        /// for it. Without that third clause the scan finds no boundary once sections are expanded
        /// and the keys silently no-op.
        /// </summary>
        protected override bool IsSectionBoundary(InspectionTreeItem item)
        {
            if (item == null)
            {
                return false;
            }
            if (item.IsSectionHeader || item.Type == InspectionTreeItem.ItemType.SubCategory)
            {
                return true;
            }
            if (!Tree.SubmenuMode)
            {
                return false;
            }
            InspectionTreeItem parent = item.Parent;
            return parent != null
                && (parent.IsSectionHeader || parent.Type == InspectionTreeItem.ItemType.SubCategory)
                && parent.IsExpanded
                && parent.Children.Count > 0
                && ReferenceEquals(parent.Children[0], item);
        }

        /// <summary>Typeahead reaches inside a collapsed section on the first typed character.</summary>
        protected override bool ShouldAutoExpandForSearch(InspectionTreeItem item)
        {
            return item != null && item.AutoExpandForSearch;
        }

        /// <summary>
        /// Lazy child population: an empty child list means "not built yet", since a section that
        /// genuinely holds nothing gets an explicit empty-state row instead.
        /// </summary>
        protected override void OnBeforeExpandNode(InspectionTreeItem item)
        {
            if (item == null || !(item.Data is SectionKind kind))
            {
                return;
            }
            if (staleSections.Remove(kind))
            {
                // A mutation landed while this section was collapsed: drop the cached children
                // so the build below runs again.
                item.Children.Clear();
            }
            if (item.Children.Count > 0)
            {
                return;
            }

            Pawn pawn = CharEditorCompat.CurrentPawn;
            if (pawn == null)
            {
                InspectNodeFactory.DetailLine(item, "RimWorldAccess.CharEd.NoPawn".Translate());
                return;
            }

            switch (kind)
            {
                case SectionKind.Character:
                    BuildCharacterSection(item, pawn);
                    break;
                case SectionKind.Appearance:
                    BuildAppearanceSection(item, pawn);
                    break;
                case SectionKind.Inventory:
                    BuildInventorySection(item, pawn);
                    break;
                case SectionKind.Health:
                    BuildHealthSection(item, pawn);
                    break;
                case SectionKind.Needs:
                    BuildNeedsSection(item, pawn);
                    break;
                case SectionKind.Social:
                    BuildSocialSection(item, pawn);
                    break;
                case SectionKind.Log:
                    BuildLogSection(item, pawn);
                    break;
                case SectionKind.Info:
                    BuildInfoSection(item, pawn);
                    break;
                case SectionKind.Records:
                    BuildRecordsSection(item, pawn);
                    break;
            }

            // Not while a search runs: typeahead auto-expands every section at once, and mirroring
            // each in turn would leave the mod's tab strip on whichever the sweep reached last.
            if (!TypeaheadHasActiveSearch)
            {
                MirrorVisualTab(kind);
            }
        }

        /// <summary>
        /// Marks a SubCategory group whose collapsed row speaks a content summary of its DATA rows'
        /// short labels, via <see cref="ComputeGroupSummary"/>. Carried as the group's own
        /// <see cref="InspectionTreeItem.Data"/>; a group that never sets it speaks its bare name.
        /// No section folds a hidden-row count into its collapsed label, which would tell the player
        /// nothing about the pawn. The summary is computed FRESH on every describe call off the
        /// group's live children: a stepped, pasted or randomized skill level changes with no tree
        /// rebuild at all, so a cached string would go stale.
        /// </summary>
        private enum GroupSummaryKind
        {
            Traits,
            Skills,
            Abilities,
            Equipment,
            Apparel,
            Inventory,
            Memories,
        }

        /// <summary>Cap on how many data-row labels a collapsed group's summary speaks before folding the rest into "and N more".</summary>
        private const int GroupSummaryCap = 5;

        /// <summary>Dispatches to the per-domain collector for <paramref name="kind"/>, then formats the result.</summary>
        private static string ComputeGroupSummary(InspectionTreeItem group, GroupSummaryKind kind)
        {
            if (kind == GroupSummaryKind.Skills)
            {
                return SummarizeSkills(group);
            }
            var labels = new List<string>();
            switch (kind)
            {
                case GroupSummaryKind.Traits:
                    CollectEntryLabels<RegionRow<CharRowKind>>(group, r => r.Kind == CharRowKind.TraitEntry, labels);
                    break;
                case GroupSummaryKind.Abilities:
                    CollectEntryLabels<RegionRow<CharRowKind>>(group, r => r.Kind == CharRowKind.AbilityEntry, labels);
                    break;
                case GroupSummaryKind.Equipment:
                case GroupSummaryKind.Apparel:
                case GroupSummaryKind.Inventory:
                    // All three Inventory-section lists share one row shape, and each builder only
                    // populates its own subsection, so filtering by kind within a sub is unambiguous.
                    CollectEntryLabels<RegionRow<InventoryRowKind>>(group, r => r.Kind == InventoryRowKind.ThingEntry, labels);
                    break;
                case GroupSummaryKind.Memories:
                    CollectEntryLabels<RegionRow<NeedsRowKind>>(group, r => r.Kind == NeedsRowKind.MemoryEntry, labels);
                    break;
            }
            return FormatGroupSummary(labels);
        }

        /// <summary>Appends every child whose Data is a <typeparamref name="TRow"/> matching <paramref name="isEntry"/>; tool buttons are excluded by construction.</summary>
        private static void CollectEntryLabels<TRow>(InspectionTreeItem group, Func<TRow, bool> isEntry, List<string> into) where TRow : class
        {
            foreach (InspectionTreeItem child in group.Children)
            {
                if (child.Data is TRow row && isEntry(row))
                {
                    into.Add(child.Label);
                }
            }
        }

        /// <summary>
        /// Skills summarize as the top five BY LEVEL DESCENDING, not build order, reading
        /// <see cref="SkillRecord.levelInt"/> off the live record every call so a stepped, pasted or
        /// randomized level is never stale. OrderByDescending is stable, so ties keep build order.
        /// </summary>
        private static string SummarizeSkills(InspectionTreeItem group)
        {
            var entries = new List<KeyValuePair<string, int>>();
            foreach (InspectionTreeItem child in group.Children)
            {
                if (child.Data is RegionRow<CharRowKind> row && row.Kind == CharRowKind.SkillEntry && row.Payload is SkillRecord skill)
                {
                    entries.Add(new KeyValuePair<string, int>(skill.def.LabelCap.ToString(), skill.levelInt));
                }
            }
            List<string> ordered = entries
                .OrderByDescending(e => e.Value)
                .Select(e => e.Key + " " + e.Value)
                .ToList();
            return FormatGroupSummary(ordered);
        }

        /// <summary>Joins up to <see cref="GroupSummaryCap"/> labels, then "and N more"; an empty set speaks as empty rather than a phantom count of tool buttons.</summary>
        private static string FormatGroupSummary(List<string> labels)
        {
            if (labels.Count == 0)
            {
                return "RimWorldAccess.CharEd.SummaryEmpty".Translate();
            }
            if (labels.Count <= GroupSummaryCap)
            {
                return string.Join(", ", labels);
            }
            return string.Join(", ", labels.Take(GroupSummaryCap)) + ", "
                + "RimWorldAccess.CharEd.SummaryMore".Translate(labels.Count - GroupSummaryCap).ToString();
        }

        /// <summary>Points the mod's own tab strip at the section just opened; cosmetic and best-effort, declining silently when the binding is missing.</summary>
        private void MirrorVisualTab(SectionKind kind)
        {
            switch (kind)
            {
                case SectionKind.Character:
                    CharEditorCompat.SwitchTab(window, CharacterTabName);
                    break;
                case SectionKind.Inventory:
                    CharEditorCompat.SwitchTab(window, InventoryTabName);
                    break;
                case SectionKind.Health:
                    CharEditorCompat.SwitchTab(window, HealthTabName);
                    break;
                case SectionKind.Needs:
                    CharEditorCompat.SwitchTab(window, NeedsTabName);
                    break;
                case SectionKind.Social:
                    CharEditorCompat.SwitchTab(window, SocialTabName);
                    break;
                case SectionKind.Log:
                    CharEditorCompat.SwitchTab(window, LogTabName);
                    break;
                case SectionKind.Info:
                    CharEditorCompat.SwitchTab(window, InfoTabName);
                    break;
                case SectionKind.Records:
                    CharEditorCompat.SwitchTab(window, RecordsTabName);
                    break;
            }
        }

        /// <summary>
        /// The pawn's combat and social log, from the same vanilla call the mod's own Log tab makes.
        /// Battle headers become section-boundary rows, so Page Up/Down steps battle to battle.
        /// </summary>
        private static void BuildLogSection(InspectionTreeItem section, Pawn pawn)
        {
            List<ITab_Pawn_Log_Utility.LogLineDisplayable> lines;
            try
            {
                lines = ITab_Pawn_Log_Utility.GenerateLogLinesFor(
                    pawn, showAll: true, showCombat: true, showSocial: true, LogLineBudget);
            }
            catch (Exception ex)
            {
                ModLogger.Error("CharacterEditorScope: log generation failed: " + ex.Message);
                lines = null;
            }

            int added = 0;
            if (lines != null)
            {
                var sb = new StringBuilder();
                foreach (ITab_Pawn_Log_Utility.LogLineDisplayable line in lines)
                {
                    // AppendTo is the only text vehicle these display objects expose, and a spacer
                    // line appends only a newline, which trims to empty.
                    sb.Length = 0;
                    line.AppendTo(sb);
                    string text = sb.ToString().StripTags().Trim();
                    if (text.Length == 0)
                    {
                        continue;
                    }

                    if (line is ITab_Pawn_Log_Utility.LogLineDisplayableHeader)
                    {
                        // The header's AppendTo prefixes a run of dashes as a visual rule.
                        string name = text.TrimStart('-').Trim();
                        InspectionTreeItem header = InspectNodeFactory.DetailLine(
                            section, "RimWorldAccess.Combat.Log.BattleHeader".Translate(name));
                        header.IsSectionHeader = true;
                    }
                    else
                    {
                        InspectNodeFactory.DetailLine(section, text);
                    }
                    added++;
                }
            }

            if (added == 0)
            {
                InspectNodeFactory.DetailLine(section, "NoRecentEntries".Translate());
            }
        }

        /// <summary>
        /// The pawn's stats report, grouped by the stat categories' own labels in the game's order.
        /// The subject follows the mod: the CORPSE of a dead pawn, the pawn itself otherwise.
        /// </summary>
        private static void BuildInfoSection(InspectionTreeItem section, Pawn pawn)
        {
            Thing subject = pawn.Dead && pawn.Corpse != null ? (Thing)pawn.Corpse : pawn;
            List<StatDrawEntry> entries = InfoCardDataExtractor.GetStatEntriesFor(subject);
            if (entries.Count == 0)
            {
                InspectNodeFactory.DetailLine(section, "RimWorldAccess.CharEd.NoStats".Translate());
                return;
            }

            InspectionTreeItem category = null;
            string categoryLabel = null;
            foreach (StatDrawEntry entry in entries)
            {
                string label = entry.category != null ? entry.category.LabelCap.ToString() : "";
                if (category == null || label != categoryLabel)
                {
                    categoryLabel = label;
                    category = new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.SubCategory,
                        Label = label,
                        IndentLevel = section.IndentLevel + 1,
                        IsExpandable = true,
                        IsExpanded = false,
                        AutoExpandForSearch = true,
                        Parent = section,
                    };
                    section.Children.Add(category);
                }
                InspectNodeFactory.DetailLine(category, FormatStatEntry(entry));
            }
        }

        private static string FormatStatEntry(StatDrawEntry entry)
        {
            string label = entry.LabelCap;
            string value = entry.ValueString;
            return value.NullOrEmpty()
                ? label
                : "RimWorldAccess.CharEd.LabelledValue".Translate(label, value).ToString();
        }

        /// <summary>
        /// The pawn's record card: time records first, then numeric ones, ordered by
        /// <c>RecordDef.displayOrder</c>, the same source and order the vanilla info card's reader
        /// uses. Built directly against <c>DefDatabase&lt;RecordDef&gt;</c> rather than that
        /// reader's composed string tuples, because each row needs a live <see cref="RecordDef"/> to
        /// target for editing. Label source and value formatting match the reader exactly.
        /// </summary>
        private static void BuildRecordsSection(InspectionTreeItem section, Pawn pawn)
        {
            int added = 0;
            if (pawn?.records != null)
            {
                foreach (RecordDef recordDef in DefDatabase<RecordDef>.AllDefsListForReading
                    .Where(r => r.type == RecordType.Time)
                    .OrderBy(r => r.displayOrder))
                {
                    AddRecordRow(section, recordDef);
                    added++;
                }
                foreach (RecordDef recordDef in DefDatabase<RecordDef>.AllDefsListForReading
                    .Where(r => r.type == RecordType.Int || r.type == RecordType.Float)
                    .OrderBy(r => r.displayOrder))
                {
                    AddRecordRow(section, recordDef);
                    added++;
                }
            }
            if (added == 0)
            {
                InspectNodeFactory.DetailLine(section, "RimWorldAccess.CharEd.NoRecords".Translate());
            }
        }

        private static InspectionTreeItem AddRecordRow(InspectionTreeItem section, RecordDef recordDef)
        {
            var item = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Item,
                Label = recordDef.LabelCap.ToString(),
                IndentLevel = section.IndentLevel + 1,
                IsExpandable = false,
                Data = new RecordRow(recordDef),
                Parent = section,
            };
            section.Children.Add(item);
            return item;
        }

        /// <summary>The same formatting the vanilla info card's reader uses: a period string for Time, "0.##" otherwise.</summary>
        private static string FormatRecordValue(RecordDef def, Pawn pawn)
        {
            return def.type == RecordType.Time
                ? pawn.records.GetAsInt(def).ToStringTicksToPeriod()
                : pawn.records.GetValue(def).ToString("0.##", CultureInfo.InvariantCulture);
        }

        private static void DescribeRecordRow(RecordRow row, Pawn pawn, ElementDescription d)
        {
            RecordDef def = row?.Def;
            if (def == null)
            {
                return;
            }
            d.Label = def.LabelCap.ToString();
            if (pawn?.records == null)
            {
                d.ReadOnly = true;
                return;
            }
            d.Role = ElementRole.Stepper;
            d.Value = FormatRecordValue(def, pawn);
            // No AtMaximum: every RecordTool box's upper bound is unreachable in practice.
            d.AtMinimum = pawn.records.GetValue(def) <= 0f;
        }

        /// <summary>Text-field limit for a record's exact entry: the mod's own three numeric boxes all cap at 32.</summary>
        private const int RecordFieldMaxLength = 32;

        /// <summary>Enter on a Records row: exact entry, gated per RecordDef.type exactly as the mod picks int, float or long.</summary>
        private void BeginRecordEdit(RecordRow row, InspectionTreeItem item)
        {
            RecordDef def = row?.Def;
            Pawn pawn = CharEditorCompat.CurrentPawn;
            if (def == null || pawn?.records == null)
            {
                AnnounceCurrentItem();
                return;
            }
            string label = def.LabelCap.ToString();
            switch (def.type)
            {
                case RecordType.Int:
                {
                    int current = pawn.records.GetAsInt(def);
                    var spec = new TextFieldSpec(labelKey: null, maxLength: RecordFieldMaxLength, minLength: 0,
                        allowedChars: CharEdNumericEntry.DigitsOnly);
                    editSession.EnterEdit(current.ToString(), spec, label,
                        value => ApplyRecordInt(pawn, def, value),
                        onExit: () => AnnounceAdjustedRecordRow(item));
                    break;
                }
                case RecordType.Float:
                {
                    float current = pawn.records.GetValue(def);
                    var spec = new TextFieldSpec(labelKey: null, maxLength: RecordFieldMaxLength, minLength: 0,
                        allowedChars: CharEdNumericEntry.UnsignedFloat);
                    editSession.EnterEdit(current.ToString("0.##", CultureInfo.InvariantCulture), spec, label,
                        value => ApplyRecordFloat(pawn, def, value),
                        onExit: () => AnnounceAdjustedRecordRow(item));
                    break;
                }
                case RecordType.Time:
                {
                    // The mod's own Time box types and commits in this same /60 unit, not raw ticks.
                    long units = (long)(pawn.records.GetValue(def) / 60f);
                    var spec = new TextFieldSpec(labelKey: null, maxLength: RecordFieldMaxLength, minLength: 0,
                        allowedChars: CharEdNumericEntry.DigitsOnly);
                    editSession.EnterEdit(units.ToString(), spec, label,
                        value => ApplyRecordTime(pawn, def, value),
                        onExit: () => AnnounceAdjustedRecordRow(item));
                    break;
                }
            }
        }

        /// <summary>Rides RecordTool's own SetRecordValue extension; the clamp mirrors its int box's bound.</summary>
        private static void ApplyRecordInt(Pawn pawn, RecordDef def, string value)
        {
            if (!int.TryParse(value, out int parsed))
            {
                return;
            }
            parsed = Mathf.Clamp(parsed, 0, int.MaxValue);
            CharEditorCompat.SetRecordValue(pawn, def, parsed);
        }

        /// <summary>Rides the mod's own setter; the clamp mirrors its float box's bound.</summary>
        private static void ApplyRecordFloat(Pawn pawn, RecordDef def, string value)
        {
            if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
            {
                return;
            }
            parsed = Mathf.Clamp(parsed, 0f, 1e9f);
            CharEditorCompat.SetRecordValue(pawn, def, parsed);
        }

        /// <summary>
        /// Mirrors the mod's own Time box exactly: the typed figure is in the same /60 unit its long
        /// box reads and writes, not raw ticks, so committing here loses the identical sub-60-tick
        /// precision. Mod behavior to ride, not fix.
        /// </summary>
        private static void ApplyRecordTime(Pawn pawn, RecordDef def, string value)
        {
            if (!long.TryParse(value, out long units))
            {
                return;
            }
            if (units < 0)
            {
                units = 0;
            }
            CharEditorCompat.SetRecordValue(pawn, def, units * 60);
        }

        /// <summary>Left/Right steps a record by the mod's own +/-1 button increment, clamped as exact entry is.</summary>
        private bool AdjustRecordRow(RecordRow row, InspectionTreeItem item, int direction)
        {
            RecordDef def = row?.Def;
            Pawn pawn = CharEditorCompat.CurrentPawn;
            if (def == null || pawn?.records == null)
            {
                return true;
            }
            switch (def.type)
            {
                case RecordType.Int:
                    ApplyRecordInt(pawn, def, (pawn.records.GetAsInt(def) + direction).ToString());
                    break;
                case RecordType.Float:
                {
                    // The rounding matches the mod's float box +/-1 handler.
                    float stepped = (float)Math.Round(pawn.records.GetValue(def) + direction, 2);
                    ApplyRecordFloat(pawn, def, stepped.ToString("0.##", CultureInfo.InvariantCulture));
                    break;
                }
                case RecordType.Time:
                {
                    long units = (long)(pawn.records.GetValue(def) / 60f) + direction;
                    ApplyRecordTime(pawn, def, units.ToString());
                    break;
                }
                default:
                    return true;
            }
            SoundDefOf.DragSlider.PlayOneShotOnCamera();
            AnnounceAdjustedRecordRow(item);
            return true;
        }

        private void AnnounceAdjustedRecordRow(InspectionTreeItem item)
        {
            RefreshModel();
            if (item?.Data is RecordRow row)
            {
                var d = new ElementDescription();
                DescribeRecordRow(row, CharEditorCompat.CurrentPawn, d);
                TolkHelper.SpeakData(AnnouncementComposer.ComposeStateChange(d, TranslatedShellVocabulary.Instance));
                return;
            }
            AnnounceCurrentItem();
        }
    }
}
