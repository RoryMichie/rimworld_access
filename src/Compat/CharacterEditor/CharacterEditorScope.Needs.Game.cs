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
        // ------------------------------------------------------------------
        // Needs section: Needs (slider rows), Memories, Actions. Gated exactly as the
        // mod gates BlockNeeds.Draw (needs tracker present AND alive) -- a dead pawn's Needs tab
        // draws nothing at all in the mod, so this section says why rather than presenting an
        // empty subsection tree.
        // ------------------------------------------------------------------

        private void BuildNeedsSection(InspectionTreeItem section, Pawn pawn)
        {
            if (!CharEditorCompat.NeedsReady)
            {
                InspectNodeFactory.DetailLine(section, "RimWorldAccess.CharEd.NoPawn".Translate());
                return;
            }
            if (pawn.needs == null)
            {
                InspectNodeFactory.DetailLine(section, "RimWorldAccess.CharEd.Needs.NoNeeds".Translate());
                return;
            }
            if (pawn.Dead)
            {
                InspectNodeFactory.DetailLine(section, "RimWorldAccess.CharEd.Needs.DeadPawn".Translate());
                return;
            }
            BuildNeedsSlidersSubsection(section, pawn);
            BuildMemoriesSubsection(section, pawn);
            BuildNeedsActionsSubsection(section, pawn);
        }

        /// <summary>One Slider row per need in pawn.needs.AllNeeds -- the mod's own enumeration (BlockNeeds.DrawNeedBars), already curated to whatever needs this pawn's race/traits carry (Mood included, drawn no differently here than any other need).</summary>
        private static void BuildNeedsSlidersSubsection(InspectionTreeItem section, Pawn pawn)
        {
            InspectionTreeItem sub = AddSubsection(section, "RimWorldAccess.CharEd.Needs.NeedsSection".Translate());
            int added = 0;
            foreach (Need need in pawn.needs.AllNeeds)
            {
                AddRow(sub, NeedsRowKind.NeedEntry, need.LabelCap, need);
                added++;
            }
            if (added == 0)
            {
                InspectNodeFactory.DetailLine(sub, "RimWorldAccess.CharEd.Needs.NoNeeds".Translate());
            }
        }

        /// <summary>
        /// Every thought except the mod's own "AteNon"-prefixed Thought_MemorySocial group
        /// (BlockNeeds.DrawMemories' own filter, replicated directly against public vanilla
        /// ThoughtDef.thoughtClass -- an exact-class check, not IsTypeOf's subclass-inclusive
        /// one), then ONE aggregated row for that group carrying its count -- exactly mirroring
        /// the mod's own listing.
        /// </summary>
        private void BuildMemoriesSubsection(InspectionTreeItem section, Pawn pawn)
        {
            InspectionTreeItem sub = AddSubsection(section, "RimWorldAccess.CharEd.Needs.MemoriesSection".Translate());
            sub.Data = GroupSummaryKind.Memories;
            List<Thought> sorted = CharEditorCompat.SortedThoughts(pawn);
            Thought example;
            int aggregateCount = CharEditorCompat.AggregatedThought(sorted, out example);
            int added = 0;
            foreach (Thought t in sorted)
            {
                if (t?.def == null)
                {
                    continue;
                }
                bool isAggregatedAteNon = t.def.thoughtClass == typeof(Thought_MemorySocial)
                    && !t.def.defName.NullOrEmpty() && t.def.defName.StartsWith("AteNon");
                if (isAggregatedAteNon)
                {
                    continue;
                }
                AddMemoryRow(sub, t, 0);
                added++;
            }
            if (example != null)
            {
                AddMemoryRow(sub, example, aggregateCount);
                added++;
            }
            if (added == 0)
            {
                InspectNodeFactory.DetailLine(sub, "RimWorldAccess.CharEd.Needs.NoMemories".Translate());
            }
            AddRow(sub, NeedsRowKind.ActionAddThought, "RimWorldAccess.CharEd.Needs.AddThought".Translate());
        }

        /// <summary>
        /// Bakes the resolved label into the tree item's OWN Label at build time, because
        /// typeahead reads a row's identity
        /// straight off the InspectionTreeItem (ContentRowSearchText), never through
        /// DescribeNeedsRow, so a row whose display text is entirely computed live would
        /// otherwise be untypeable by name.
        /// </summary>
        private void AddMemoryRow(InspectionTreeItem parent, Thought t, int count)
        {
            string label;
            CharEditorCompat.DescribeThought(window, t, count, out label, out _, out _, out _);
            AddRow(parent, NeedsRowKind.MemoryEntry, label, new NeedsMemoryEntry { Example = t, Count = count });
        }

        private void BuildNeedsActionsSubsection(InspectionTreeItem section, Pawn pawn)
        {
            InspectionTreeItem sub = AddSubsection(section, "RimWorldAccess.CharEd.Actions".Translate());
            AddRow(sub, NeedsRowKind.ActionFillNeeds, "RimWorldAccess.CharEd.Needs.FillAllNeeds".Translate());
            AddRow(sub, NeedsRowKind.ActionClearMemories, "RimWorldAccess.CharEd.Needs.ClearAllMemories".Translate());
        }

        private void DescribeNeedsRow(RegionRow<NeedsRowKind> row, ElementDescription d)
        {
            switch (row.Kind)
            {
                case NeedsRowKind.NeedEntry:
                {
                    var need = row.Payload as Need;
                    d.Label = need?.LabelCap ?? "";
                    d.Role = ElementRole.Slider;
                    if (need != null)
                    {
                        d.Value = need.CurLevelPercentage.ToStringPercent();
                        d.AtMinimum = need.CurLevelPercentage <= 0f;
                        d.AtMaximum = need.CurLevelPercentage >= 1f;
                        d.Extras = (need.CurLevelPercentage < 0.2f
                            ? "RimWorldAccess.CharEd.Needs.LowWarning".Translate().ToString() + " "
                            : "") + need.GetTipString();
                    }
                    break;
                }
                case NeedsRowKind.MemoryEntry:
                {
                    var entry = row.Payload as NeedsMemoryEntry;
                    if (entry?.Example == null)
                    {
                        break;
                    }
                    string label;
                    string tooltip;
                    CharEditorCompat.DescribeThought(window, entry.Example, entry.Count, out label, out _, out _, out tooltip);
                    float opinionOffset;
                    float moodOffset;
                    CharEditorCompat.ThoughtOffsets(window, entry.Example, out opinionOffset, out moodOffset);
                    d.Label = label;
                    d.ReadOnly = true;
                    d.Value = FormatThoughtOffsets(opinionOffset, moodOffset);
                    d.Extras = tooltip;
                    break;
                }
                case NeedsRowKind.ActionAddThought:
                    d.Label = "RimWorldAccess.CharEd.Needs.AddThought".Translate();
                    d.Role = ElementRole.Button;
                    break;
                case NeedsRowKind.ActionFillNeeds:
                    d.Label = "RimWorldAccess.CharEd.Needs.FillAllNeeds".Translate();
                    d.Role = ElementRole.Button;
                    break;
                case NeedsRowKind.ActionClearMemories:
                    d.Label = "RimWorldAccess.CharEd.Needs.ClearAllMemories".Translate();
                    d.Role = ElementRole.Button;
                    break;
            }
        }

        /// <summary>The mod's own opinion sentinel (Thought.GetOpinionOffset() returns float.MinValue for a non-social thought) is never spoken as a number.</summary>
        private static string FormatThoughtOffsets(float opinionOffset, float moodOffset)
        {
            var parts = new List<string>();
            if (moodOffset != 0f)
            {
                parts.Add("RimWorldAccess.CharEd.Needs.MoodOffset".Translate(moodOffset.ToString("+0;-0;0")));
            }
            if (opinionOffset != float.MinValue && opinionOffset != 0f)
            {
                parts.Add("RimWorldAccess.CharEd.Needs.OpinionOffset".Translate(opinionOffset.ToString("+0;-0;0")));
            }
            return string.Join(", ", parts.ToArray());
        }

        private void ActivateNeedsRow(RegionRow<NeedsRowKind> row, InspectionTreeItem item)
        {
            switch (row.Kind)
            {
                case NeedsRowKind.NeedEntry:
                    BeginNeedPercentageEdit(row.Payload as Need, item);
                    break;
                case NeedsRowKind.MemoryEntry:
                    // Read-only informational row: navigable, and says so. Enter re-reads it.
                    AnnounceCurrentItem();
                    break;
                case NeedsRowKind.ActionAddThought:
                    CharEditorCompat.OpenAddThought(window);
                    // Dialog opened; OnFocus's silent RefreshNeedsSectionInPlace covers the return.
                    break;
                case NeedsRowKind.ActionFillNeeds:
                    CharEditorCompat.FillAllNeeds(window);
                    RefreshNeedsSectionInPlace(silent: false, outcomeKey: "RimWorldAccess.CharEd.Needs.NeedsFilled");
                    break;
                case NeedsRowKind.ActionClearMemories:
                    // Our own confirm (the mod's NOMEMORIES button carries none): mass-destructive
                    // and irreversible. Standard Dialog_MessageBox, driven by the already
                    // registered MessageBoxScope.
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        "RimWorldAccess.CharEd.Needs.ConfirmClearMemories".Translate().ToString(), delegate
                        {
                            CharEditorCompat.ClearAllMemories(window);
                            RefreshNeedsSectionInPlace(silent: false, outcomeKey: "RimWorldAccess.CharEd.Needs.MemoriesCleared");
                        }, destructive: true));
                    break;
            }
        }

        /// <summary>Left/Right steps the mod's own five-percent increment (BlockNeeds.AAddNeed/ASubNeed); every other Needs row is a button or read-only informational row.</summary>
        private bool AdjustNeedsRow(RegionRow<NeedsRowKind> row, InspectionTreeItem item, int direction)
        {
            if (row.Kind != NeedsRowKind.NeedEntry)
            {
                return false;
            }
            var need = row.Payload as Need;
            if (need == null)
            {
                return true;
            }
            CharEditorCompat.StepNeed(window, need, direction > 0);
            SoundDefOf.DragSlider.PlayOneShotOnCamera();
            AnnounceAdjustedRow<NeedsRowKind>(item, DescribeNeedsRow);
            return true;
        }

        /// <summary>Exact entry (0-100): vehicle B, vanilla Need.CurLevelPercentage's own self-clamping setter -- see CharEditorCompat.Needs' class remarks.</summary>
        private void BeginNeedPercentageEdit(Need need, InspectionTreeItem item)
        {
            if (need == null)
            {
                AnnounceCurrentItem();
                return;
            }
            int currentPercent = Mathf.RoundToInt(need.CurLevelPercentage * 100f);
            // No maxLength: the mod's own control is a stepper button with no text limit to
            // harvest; range enforcement is the clamp policy alone.
            var spec = new TextFieldSpec(labelKey: null, maxLength: null, minLength: 0,
                allowedChars: CharEdNumericEntry.DigitsOnly);
            CharEdNumericEntry.OpenInt(editSession, need.LabelCap, currentPercent, 0, 100,
                percent => CharEditorCompat.SetNeedPercentageExact(need, percent / 100f),
                onExit: () => AnnounceAdjustedRow<NeedsRowKind>(item, DescribeNeedsRow), spec: spec);
        }
    }
}
