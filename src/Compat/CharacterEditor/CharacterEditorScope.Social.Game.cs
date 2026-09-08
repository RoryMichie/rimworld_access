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
        // Social section: State (in-game only), the relation-builder guided flow,
        // Direct relations, Indirect relations (read-only), Opinions (reusing SocialTabHelper's
        // existing plain-data reader; SocialCardUtility is never re-transcribed).
        // Gated exactly as the mod gates BlockSocial.Draw (relation tracker
        // present).
        // ------------------------------------------------------------------

        private void BuildSocialSection(InspectionTreeItem section, Pawn pawn)
        {
            if (!CharEditorCompat.SocialReady)
            {
                InspectNodeFactory.DetailLine(section, "RimWorldAccess.CharEd.NoPawn".Translate());
                return;
            }
            if (pawn.relations == null)
            {
                InspectNodeFactory.DetailLine(section, "RimWorldAccess.CharEd.Social.NoTracker".Translate());
                return;
            }
            if (!CharEditorCompat.InStartingScreen)
            {
                BuildSocialStateSubsection(section, pawn);
            }
            BuildRelationBuilderSubsection(section, pawn);
            BuildDirectRelationsSubsection(section, pawn);
            BuildIndirectRelationsSubsection(section, pawn);
            BuildOpinionsSubsection(section, pawn);
        }

        private static void BuildSocialStateSubsection(InspectionTreeItem section, Pawn pawn)
        {
            InspectionTreeItem sub = AddSubsection(section, "RimWorldAccess.CharEd.Social.StateSection".Translate());
            AddRow(sub, SocialRowKind.StateMentalState, "RimWorldAccess.CharEd.Social.MentalState".Translate());
            AddRow(sub, SocialRowKind.StateInspiration, "RimWorldAccess.CharEd.Social.Inspiration".Translate());
        }

        /// <summary>
        /// The relation combo first, then the slot rows the mod reveals for the chosen relation
        /// (plan 2.3's guided flow). <see cref="CharEditorCompat.ReconcileRelationBuilder"/> runs
        /// FIRST, exactly where the mod's own per-frame Draw call would have run the matching
        /// Handle* method -- see that facade's class remarks.
        /// </summary>
        private void BuildRelationBuilderSubsection(InspectionTreeItem section, Pawn pawn)
        {
            CharEditorCompat.ReconcileRelationBuilder(window);
            InspectionTreeItem sub = AddSubsection(section, "RimWorldAccess.CharEd.Social.RelationBuilderSection".Translate());

            AddRow(sub, SocialRowKind.RelationChoice, "RimWorldAccess.CharEd.Social.Relation".Translate());
            AddRelationSlotRow(sub, 1);
            AddRelationGenderRow(sub, 1);

            PawnRelationDef relation = CharEditorCompat.RelationChoice(window);
            if (relation != null && relation.implied)
            {
                AddRelationSlotRow(sub, 2);
                if (CharEditorCompat.SlotGenderToggleAllowed(2, window))
                {
                    AddRelationGenderRow(sub, 2);
                }
                if (CharEditorCompat.SlotVisible(3, window))
                {
                    AddRelationSlotRow(sub, 3);
                    if (CharEditorCompat.SlotGenderToggleAllowed(3, window))
                    {
                        AddRelationGenderRow(sub, 3);
                    }
                }
                if (CharEditorCompat.SlotVisible(4, window))
                {
                    AddRelationSlotRow(sub, 4);
                    if (CharEditorCompat.SlotGenderToggleAllowed(4, window))
                    {
                        AddRelationGenderRow(sub, 4);
                    }
                }
                if (CharEditorCompat.ImpliedRelationReady(window))
                {
                    AddRow(sub, SocialRowKind.RelationAdd, "RimWorldAccess.CharEd.Social.AddRelation".Translate());
                }
            }
            else if (CharEditorCompat.SlotPawn(window, 1) != null)
            {
                AddRow(sub, SocialRowKind.RelationAdd, "RimWorldAccess.CharEd.Social.AddRelation".Translate());
            }

            AddRow(sub, SocialRowKind.ActionAddSocialThought, "RimWorldAccess.CharEd.Social.AddThought".Translate());
        }

        /// <summary>Vanilla Pawn_RelationsTracker.DirectRelations, read directly -- no reflection at all (see CharEditorCompat.Social's class remarks).</summary>
        private static void BuildDirectRelationsSubsection(InspectionTreeItem section, Pawn pawn)
        {
            InspectionTreeItem sub = AddSubsection(section, "RimWorldAccess.CharEd.Social.DirectRelationsSection".Translate());
            int added = 0;
            if (!pawn.relations.DirectRelations.NullOrEmpty())
            {
                foreach (DirectPawnRelation relation in pawn.relations.DirectRelations)
                {
                    if (relation?.otherPawn == null)
                    {
                        continue;
                    }
                    string label = relation.def.GetGenderSpecificLabelCap(relation.otherPawn) + " " + relation.otherPawn.LabelShortCap;
                    AddRow(sub, SocialRowKind.DirectRelationEntry, label, relation);
                    added++;
                }
            }
            if (added == 0)
            {
                InspectNodeFactory.DetailLine(sub, "RimWorldAccess.CharEd.Social.NoDirectRelations".Translate());
            }
        }

        /// <summary>Vanilla Pawn_RelationsTracker.RelatedPawns plus PawnRelationUtility.GetRelations, filtered to implied -- no reflection at all (mirrors BlockSocial.CreateIndirect/DrawIndirect exactly, both 100% vanilla calls).</summary>
        private static void BuildIndirectRelationsSubsection(InspectionTreeItem section, Pawn pawn)
        {
            InspectionTreeItem sub = AddSubsection(section, "RimWorldAccess.CharEd.Social.IndirectRelationsSection".Translate());
            int added = 0;
            if (pawn.relations.RelatedPawns.Any())
            {
                foreach (Pawn related in pawn.relations.RelatedPawns)
                {
                    foreach (PawnRelationDef relationDef in pawn.GetRelations(related))
                    {
                        if (!relationDef.implied)
                        {
                            continue;
                        }
                        string label = relationDef.GetGenderSpecificLabelCap(related) + " " + related.LabelShortCap;
                        AddRow(sub, SocialRowKind.IndirectRelationEntry, label,
                            new IndirectRelationEntry { OtherPawn = related, Relation = relationDef });
                        added++;
                    }
                }
            }
            if (added == 0)
            {
                InspectNodeFactory.DetailLine(sub, "RimWorldAccess.CharEd.Social.NoIndirectRelations".Translate());
            }
        }

        /// <summary>Reuses SocialTabHelper's existing plain-data reader, the same surface the inspection social card reads; SocialCardUtility is never re-transcribed.</summary>
        private static void BuildOpinionsSubsection(InspectionTreeItem section, Pawn pawn)
        {
            InspectionTreeItem sub = AddSubsection(section, "RimWorldAccess.CharEd.Social.OpinionsSection".Translate());
            List<SocialTabHelper.RelationInfo> relations = SocialTabHelper.GetRelations(pawn);
            foreach (SocialTabHelper.RelationInfo info in relations)
            {
                AddRow(sub, SocialRowKind.OpinionEntry, info.OtherPawnName, info);
            }
            if (relations.Count == 0)
            {
                InspectNodeFactory.DetailLine(sub, "RimWorldAccess.CharEd.Social.NoOpinions".Translate());
            }
        }

        /// <summary>
        /// Bakes both the slot's caption AND its current pawn's name into the tree item's OWN
        /// Label at build time (matching AddMemoryRow's own reasoning) -- typeahead reads a row's
        /// identity straight off the InspectionTreeItem, never through DescribeSocialRow.
        /// </summary>
        private InspectionTreeItem AddRelationSlotRow(InspectionTreeItem parent, int slot)
        {
            string caption = SocialSlotCaption(window, slot);
            Pawn slotPawn = CharEditorCompat.SlotPawn(window, slot);
            string label = caption + ": " + (slotPawn != null ? slotPawn.LabelShortCap : "None".Translate().ToString());
            return AddRow(parent, SocialRowKind.RelationSlotPawn, label, slot);
        }

        private InspectionTreeItem AddRelationGenderRow(InspectionTreeItem parent, int slot)
        {
            return AddRow(parent, SocialRowKind.RelationSlotGender,
                "RimWorldAccess.CharEd.Social.RelativeGender".Translate().ToString(), slot);
        }

        /// <summary>The mod's own gender-specific relation label (BlockSocial.GetLabelSelectedPawn(PawnRelationDef)) -- a plain vanilla formula (PawnRelationDef.label/labelFemale), so no reflection is needed for slot 1's own caption (unlike slots 2-4, whose captions ride BlockSocial's own GetLabelSelectedPawn2/3/4 through CharEditorCompat.SlotCaption).</summary>
        private static string RelationGenderLabel(PawnRelationDef r, Gender g)
        {
            if (r == null)
            {
                return "";
            }
            return g == Gender.Male ? r.label : (r.labelFemale.NullOrEmpty() ? r.label : r.labelFemale);
        }

        private string SocialSlotCaption(Window editorUI, int slot)
        {
            if (slot == 1)
            {
                PawnRelationDef relation = CharEditorCompat.RelationChoice(editorUI);
                Gender gender = CharEditorCompat.SlotGender(editorUI, 1);
                return relation != null
                    ? RelationGenderLabel(relation, gender).CapitalizeFirst()
                    : "RimWorldAccess.CharEd.Social.RelationTarget".Translate().ToString();
            }
            string caption = CharEditorCompat.SlotCaption(editorUI, slot);
            return caption.NullOrEmpty() ? "RimWorldAccess.CharEd.Social.RelationTarget".Translate().ToString() : caption;
        }

        private void DescribeSocialRow(RegionRow<SocialRowKind> row, ElementDescription d)
        {
            Pawn pawn = CharEditorCompat.CurrentPawn;
            switch (row.Kind)
            {
                case SocialRowKind.StateMentalState:
                {
                    MentalStateDef current = pawn?.MentalState?.def;
                    d.Label = "RimWorldAccess.CharEd.Social.MentalState".Translate();
                    d.Role = ElementRole.ComboBox;
                    d.Value = current != null ? current.LabelCap.ToString() : "None".Translate().ToString();
                    d.Extras = current?.description;
                    break;
                }
                case SocialRowKind.StateInspiration:
                {
                    InspirationDef current = pawn?.Inspiration?.def;
                    d.Label = "RimWorldAccess.CharEd.Social.Inspiration".Translate();
                    d.Role = ElementRole.ComboBox;
                    d.Value = current != null ? current.LabelCap.ToString() : "None".Translate().ToString();
                    d.Extras = current?.description;
                    break;
                }
                case SocialRowKind.RelationChoice:
                {
                    PawnRelationDef relation = CharEditorCompat.RelationChoice(window);
                    Gender gender = CharEditorCompat.SlotGender(window, 1);
                    d.Label = "RimWorldAccess.CharEd.Social.Relation".Translate();
                    d.Role = ElementRole.ComboBox;
                    d.Value = relation != null ? RelationGenderLabel(relation, gender).CapitalizeFirst() : "None".Translate().ToString();
                    break;
                }
                case SocialRowKind.RelationSlotPawn:
                {
                    int slot = row.Payload is int s ? s : 1;
                    Pawn slotPawn = CharEditorCompat.SlotPawn(window, slot);
                    d.Label = SocialSlotCaption(window, slot);
                    d.Role = ElementRole.ComboBox;
                    d.Value = slotPawn != null ? slotPawn.LabelShortCap : "None".Translate().ToString();
                    break;
                }
                case SocialRowKind.RelationSlotGender:
                {
                    // D10 fix: this drives the relative's gender (Mother-vs-Father
                    // wording, which grandparent slot fills -- BlockSocial's
                    // AOnGenderChange2-4) rather than a property of the row itself,
                    // so present it as what it is: a button whose value is the
                    // relative's current gender, spoken through vanilla's own
                    // Gender.GetLabel() rather than a bare "female checkbox".
                    int slot = row.Payload is int s ? s : 1;
                    Gender gender = CharEditorCompat.SlotGender(window, slot);
                    d.Label = "RimWorldAccess.CharEd.Social.RelativeGender".Translate();
                    d.Role = ElementRole.Button;
                    d.Value = gender.GetLabel().CapitalizeFirst();
                    break;
                }
                case SocialRowKind.RelationAdd:
                    d.Label = "RimWorldAccess.CharEd.Social.AddRelation".Translate();
                    d.Role = ElementRole.Button;
                    break;
                case SocialRowKind.ActionAddSocialThought:
                    d.Label = "RimWorldAccess.CharEd.Social.AddThought".Translate();
                    d.Role = ElementRole.Button;
                    break;
                case SocialRowKind.DirectRelationEntry:
                {
                    var relation = row.Payload as DirectPawnRelation;
                    if (relation?.otherPawn != null)
                    {
                        d.Label = relation.def.GetGenderSpecificLabelCap(relation.otherPawn) + " " + relation.otherPawn.LabelShortCap;
                        d.Role = ElementRole.ComboBox;
                        d.Extras = relation.otherPawn.MainDesc(writeFaction: true);
                    }
                    break;
                }
                case SocialRowKind.IndirectRelationEntry:
                {
                    var entry = row.Payload as IndirectRelationEntry;
                    if (entry?.OtherPawn != null && entry.Relation != null)
                    {
                        d.Label = entry.Relation.GetGenderSpecificLabelCap(entry.OtherPawn) + " " + entry.OtherPawn.LabelShortCap;
                        d.ReadOnly = true;
                        d.Extras = entry.OtherPawn.MainDesc(writeFaction: true);
                    }
                    break;
                }
                case SocialRowKind.OpinionEntry:
                {
                    var info = row.Payload as SocialTabHelper.RelationInfo;
                    if (info != null)
                    {
                        d.Label = info.OtherPawnName;
                        d.ReadOnly = true;
                        d.Value = info.Relations.Count > 0 ? string.Join(", ", info.Relations.ToArray()) : "";
                        d.Extras = info.DetailLines.Count > 1
                            ? string.Join(". ", info.DetailLines.Skip(1).ToArray())
                            : null;
                    }
                    break;
                }
            }
        }

        private void ActivateSocialRow(RegionRow<SocialRowKind> row, InspectionTreeItem item)
        {
            switch (row.Kind)
            {
                case SocialRowKind.StateMentalState:
                    OpenMentalStatePicker(item);
                    break;
                case SocialRowKind.StateInspiration:
                    OpenInspirationPicker(item);
                    break;
                case SocialRowKind.RelationChoice:
                    OpenRelationChoicePicker();
                    break;
                case SocialRowKind.RelationSlotPawn:
                {
                    int slot = row.Payload is int s ? s : 1;
                    if (slot == 1)
                    {
                        CharEditorCompat.OpenSlot1Picker(window);
                    }
                    else
                    {
                        CharEditorCompat.OpenSlotPicker(window, slot);
                    }
                    // Dialog opened; OnFocus's silent RefreshSocialSectionInPlace covers the return.
                    break;
                }
                case SocialRowKind.RelationSlotGender:
                {
                    int slot = row.Payload is int s ? s : 1;
                    if (slot == 1)
                    {
                        CharEditorCompat.ToggleSlot1Gender(window);
                    }
                    else
                    {
                        CharEditorCompat.ToggleSlotGender(window, slot);
                    }
                    RefreshSocialSectionInPlace(silent: false);
                    break;
                }
                case SocialRowKind.RelationAdd:
                {
                    PawnRelationDef relation = CharEditorCompat.RelationChoice(window);
                    if (relation != null && relation.implied)
                    {
                        CharEditorCompat.AddImpliedRelation(window);
                    }
                    else
                    {
                        CharEditorCompat.AddRelation(window);
                    }
                    RefreshSocialSectionInPlace(silent: false, outcomeKey: "RimWorldAccess.CharEd.Social.RelationAdded");
                    break;
                }
                case SocialRowKind.ActionAddSocialThought:
                    CharEditorCompat.OpenAddSocialThought(window);
                    // Dialog opened; OnFocus's silent RefreshSocialSectionInPlace covers the return.
                    break;
                case SocialRowKind.DirectRelationEntry:
                    OpenDirectRelationDrillIn(row.Payload as DirectPawnRelation);
                    break;
                case SocialRowKind.IndirectRelationEntry:
                case SocialRowKind.OpinionEntry:
                    // Read-only rows: navigable, and they say so. Enter re-reads.
                    AnnounceCurrentItem();
                    break;
            }
        }

        // Every interactive Social row is a combo box (mental state, inspiration, relation
        // choice, the relation slots), a button, a gender checkbox, or read-only -- nothing
        // adjusts in place, so there is no AdjustSocialRow arm at all: Enter/Space open the
        // pickers (see ActivateSocialRow) and Left/Right fall through to the tree grammar.

        private void OpenMentalStatePicker(InspectionTreeItem item)
        {
            List<MentalStateDef> candidates = CharEditorCompat.MentalStateCandidates();
            var options = new List<FloatMenuOption>();
            foreach (MentalStateDef candidate in candidates)
            {
                MentalStateDef captured = candidate;
                options.Add(new FloatMenuOption(captured != null ? captured.LabelCap.ToString() : "None".Translate().ToString(), delegate
                {
                    CharEditorCompat.SetMentalState(window, captured);
                    AnnounceAdjustedRow<SocialRowKind>(item, DescribeSocialRow);
                }));
            }
            WindowlessFloatMenuState.OpenTitled("RimWorldAccess.CharEd.Social.MentalState".Translate(), options);
        }

        private void OpenInspirationPicker(InspectionTreeItem item)
        {
            List<InspirationDef> candidates = CharEditorCompat.InspirationCandidates();
            var options = new List<FloatMenuOption>();
            foreach (InspirationDef candidate in candidates)
            {
                InspirationDef captured = candidate;
                options.Add(new FloatMenuOption(captured != null ? captured.LabelCap.ToString() : "None".Translate().ToString(), delegate
                {
                    CharEditorCompat.SetInspiration(window, captured);
                    AnnounceAdjustedRow<SocialRowKind>(item, DescribeSocialRow);
                }));
            }
            WindowlessFloatMenuState.OpenTitled("RimWorldAccess.CharEd.Social.Inspiration".Translate(), options);
        }

        private void OpenRelationChoicePicker()
        {
            List<PawnRelationDef> candidates = CharEditorCompat.RelationCandidates(window);
            Gender gender = CharEditorCompat.SlotGender(window, 1);
            var options = new List<FloatMenuOption>();
            foreach (PawnRelationDef candidate in candidates)
            {
                PawnRelationDef captured = candidate;
                options.Add(new FloatMenuOption(RelationGenderLabel(captured, gender).CapitalizeFirst(), delegate
                {
                    CharEditorCompat.SetRelationChoice(window, captured);
                    RefreshSocialSectionInPlace(silent: false);
                }));
            }
            WindowlessFloatMenuState.OpenTitled("RimWorldAccess.CharEd.Social.Relation".Translate(), options);
        }

        /// <summary>The Alt+I drill-in pattern's shape (a small WindowlessFloatMenuState of named options), opened directly on Enter here since a Direct-relation row has no other Enter action to reserve: Switch to this pawn (CharEditorCompat.SelectPawn, the SAME CEditor.Pawn setter every other pawn-switch path rides) and Details (re-reads the row's own tooltip).</summary>
        private void OpenDirectRelationDrillIn(DirectPawnRelation relation)
        {
            if (relation?.otherPawn == null)
            {
                AnnounceCurrentItem();
                return;
            }
            Pawn other = relation.otherPawn;
            string title = relation.def.GetGenderSpecificLabelCap(other) + " " + other.LabelShortCap;
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("RimWorldAccess.CharEd.Social.SwitchToPawn".Translate(), delegate
                {
                    CharEditorCompat.SelectPawn(other);
                    RefreshModel();
                    TolkHelper.SpeakData("RimWorldAccess.CharEd.SwitchedPawn".Translate(DescribePawnValue()).ToString());
                }),
                new FloatMenuOption("RimWorldAccess.CharEd.Social.Details".Translate(), delegate
                {
                    TolkHelper.SpeakData(other.MainDesc(writeFaction: true));
                }),
            };
            WindowlessFloatMenuState.OpenTitled(title, options);
        }
    }
}
