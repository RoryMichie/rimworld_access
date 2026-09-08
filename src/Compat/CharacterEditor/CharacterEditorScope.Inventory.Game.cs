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
        // Inventory section: Copy All/Paste All, then Stats, Equipment, Apparel,
        // Inventory, Actions. Copy All/Paste All (the mod's own Armor-separator "copy/paste all
        // three lists at once") lead the TOP section node itself -- see BuildInventorySection's own
        // remarks on why that beats mirroring the mod's exact nested visual position. Stats mirrors
        // BlockInventory's own read-only header lines exactly (mass carried of capacity, comfortable
        // temperature range -- both gated on !pawn.Dead exactly as
        // TryDrawMassInfo/TryDrawComfyTemperatureRange are -- and Overall Armor, which BlockInventory
        // draws UNCONDITIONALLY with no Dead gate, verified against DrawArmorRating/
        // TryDrawOverallArmor, CEditor.cs). Each of Equipment/Apparel/Inventory carries its own
        // Copy/Paste pair as leading rows in the same place BlockInventory draws them (on that
        // list's own header). One row shape (ThingEntry) covers all three lists, matching
        // BlockInventory's own single DrawThingRow method. Paste rows are always PRESENT, disabled
        // with a reason when that list's clipboard is empty (the S5 HasHealthClipboard idiom),
        // never absent.
        // ------------------------------------------------------------------

        private void BuildInventorySection(InspectionTreeItem section, Pawn pawn)
        {
            if (!CharEditorCompat.InventoryReady)
            {
                InspectNodeFactory.DetailLine(section, "RimWorldAccess.CharEd.NoPawn".Translate());
                return;
            }
            // Copy All/Paste All (the mod's own Armor-separator "copy/paste all three lists at
            // once") sit as leading rows on the TOP Inventory node itself rather than
            // nested under Stats' own Overall Armor node where the mod visually draws their
            // icons: three levels of navigation to reach a bulk action is too many, and a screen
            // reader user reaches the section-level bulk verb
            // fastest as the very first thing the section offers.
            AddRow(section, InventoryRowKind.ActionCopyAllGear, "RimWorldAccess.CharEd.Inventory.CopyAllGear".Translate());
            AddRow(section, InventoryRowKind.ActionPasteAllGear, "RimWorldAccess.CharEd.Inventory.PasteAllGear".Translate());
            BuildInventoryStatsSubsection(section, pawn);
            BuildInventoryEquipmentSubsection(section, pawn);
            BuildInventoryApparelSubsection(section, pawn);
            BuildInventoryInventorySubsection(section, pawn);
            BuildInventoryActionsSubsection(section, pawn);
        }

        /// <summary>Mass carried, comfortable temperature range (both !Dead-gated, TryDrawMassInfo/TryDrawComfyTemperatureRange, CEditor.cs), then Overall Armor (Sharp/Blunt/Heat, no Dead gate, TryDrawOverallArmor) -- read-only rows; Copy All/Paste All live at the top of the Inventory section itself, see BuildInventorySection's own remarks.</summary>
        private void BuildInventoryStatsSubsection(InspectionTreeItem section, Pawn pawn)
        {
            InspectionTreeItem sub = AddSubsection(section, "RimWorldAccess.CharEd.Inventory.StatsSection".Translate());
            if (!pawn.Dead)
            {
                float mass = MassUtility.GearAndInventoryMass(pawn);
                float capacity = MassUtility.Capacity(pawn);
                InspectNodeFactory.DetailLine(sub, "MassCarried".Translate(mass.ToString("0.##"), capacity.ToString("0.##")));
                InspectNodeFactory.DetailLine(sub, "ComfyTemperatureRange".Translate() + ": "
                    + pawn.GetStatValue(StatDefOf.ComfyTemperatureMin).ToStringTemperature("F0") + " ~ "
                    + pawn.GetStatValue(StatDefOf.ComfyTemperatureMax).ToStringTemperature("F0"));
            }

            InspectionTreeItem armorNode = AddSubsection(sub, "OverallArmor".Translate());
            InspectNodeFactory.DetailLine(armorNode, "ArmorSharp".Translate() + ": " + ComputeOverallArmor(pawn, StatDefOf.ArmorRating_Sharp).ToStringPercent());
            InspectNodeFactory.DetailLine(armorNode, "ArmorBlunt".Translate() + ": " + ComputeOverallArmor(pawn, StatDefOf.ArmorRating_Blunt).ToStringPercent());
            InspectNodeFactory.DetailLine(armorNode, "ArmorHeat".Translate() + ": " + ComputeOverallArmor(pawn, StatDefOf.ArmorRating_Heat).ToStringPercent());
        }

        /// <summary>BlockInventory.TryDrawOverallArmor's own coverage-weighted accumulation, verbatim (CEditor.cs) -- the identical formula ITab_Pawn_Gear.TryDrawOverallArmor/PawnGearAdapter.GetOverallArmor use, over 100% public vanilla data, so no MUTATION marker applies (read-only).</summary>
        private static float ComputeOverallArmor(Pawn pawn, StatDef stat)
        {
            float num = 0f;
            float num2 = Mathf.Clamp01(pawn.GetStatValue(stat) / 2f);
            List<BodyPartRecord> allParts = pawn.RaceProps.body.AllParts;
            List<Apparel> list = pawn.apparel != null ? pawn.apparel.WornApparel : null;
            for (int i = 0; i < allParts.Count; i++)
            {
                float num3 = 1f - num2;
                if (list != null)
                {
                    for (int j = 0; j < list.Count; j++)
                    {
                        if (list[j].def.apparel.CoversBodyPart(allParts[i]))
                        {
                            float num4 = Mathf.Clamp01(list[j].GetStatValue(stat) / 2f);
                            num3 *= 1f - num4;
                        }
                    }
                }
                num += allParts[i].coverageAbs * (1f - num3);
            }
            return Mathf.Clamp(num * 2f, 0f, 2f);
        }

        private void BuildInventoryEquipmentSubsection(InspectionTreeItem section, Pawn pawn)
        {
            InspectionTreeItem sub = AddSubsection(section, "Equipment".Translate());
            sub.Data = GroupSummaryKind.Equipment;
            AddRow(sub, InventoryRowKind.ActionCopyEquipment, "Copy".Translate());
            AddRow(sub, InventoryRowKind.ActionPasteEquipment, "Paste".Translate());
            if (pawn.equipment?.AllEquipmentListForReading != null)
            {
                foreach (ThingWithComps eq in pawn.equipment.AllEquipmentListForReading)
                {
                    AddRow(sub, InventoryRowKind.ThingEntry, GetInventoryThingLabel(pawn, eq),
                        new InventoryThingEntry(eq, CharEditorObjectsCompat.ObjectsMode.Weapon));
                }
            }
        }

        /// <summary>Sorted by body-group draw order descending, matching BlockInventory.DrawApparel's own try path; falls back to unsorted on the same malformed-apparel exception the mod's own catch repairs live (that repair rides the mod's OWN Draw pass while this section's tab is visually active -- MirrorVisualTab keeps it so -- so this reader only needs a safe fallback ORDER, not to reproduce the repair itself).</summary>
        private void BuildInventoryApparelSubsection(InspectionTreeItem section, Pawn pawn)
        {
            InspectionTreeItem sub = AddSubsection(section, "Apparel".Translate());
            sub.Data = GroupSummaryKind.Apparel;
            AddRow(sub, InventoryRowKind.ActionCopyApparel, "Copy".Translate());
            AddRow(sub, InventoryRowKind.ActionPasteApparel, "Paste".Translate());
            if (pawn.apparel != null && pawn.apparel.WornApparelCount > 0)
            {
                List<Apparel> list;
                try
                {
                    list = pawn.apparel.WornApparel.OrderByDescending(ap => ap.def.apparel.bodyPartGroups[0].listOrder).ToList();
                }
                catch
                {
                    list = pawn.apparel.WornApparel.ToList();
                }
                foreach (Apparel ap in list)
                {
                    AddRow(sub, InventoryRowKind.ThingEntry, GetInventoryThingLabel(pawn, ap),
                        new InventoryThingEntry(ap, CharEditorObjectsCompat.ObjectsMode.Apparel));
                }
            }
        }

        private void BuildInventoryInventorySubsection(InspectionTreeItem section, Pawn pawn)
        {
            InspectionTreeItem sub = AddSubsection(section, "Inventory".Translate());
            sub.Data = GroupSummaryKind.Inventory;
            AddRow(sub, InventoryRowKind.ActionCopyInventory, "Copy".Translate());
            AddRow(sub, InventoryRowKind.ActionPasteInventory, "Paste".Translate());
            if (pawn.inventory?.innerContainer != null)
            {
                foreach (Thing thing in pawn.inventory.innerContainer)
                {
                    AddRow(sub, InventoryRowKind.ThingEntry, GetInventoryThingLabel(pawn, thing),
                        new InventoryThingEntry(thing, CharEditorObjectsCompat.ObjectsMode.Object));
                }
            }
        }

        /// <summary>
        /// Undress/Redress/Reequip/Reinvent and the three Add buttons. NONE of these seven are
        /// creation-mode gated in DrawLowerButtons, unlike the per-row random dice.
        /// Undress's own three-way branch becomes two mutually exclusive rows (by
        /// InStartingScreen) plus one always-present Destroy row -- see InventoryRowKind's remarks.
        /// </summary>
        private void BuildInventoryActionsSubsection(InspectionTreeItem section, Pawn pawn)
        {
            InspectionTreeItem sub = AddSubsection(section, "RimWorldAccess.CharEd.Actions".Translate());
            if (!CharEditorCompat.InStartingScreen)
            {
                AddRow(sub, InventoryRowKind.ActionUndressDrop, "RimWorldAccess.CharEd.Inventory.UndressDrop".Translate());
            }
            else
            {
                AddRow(sub, InventoryRowKind.ActionUndressMoveToInventory, "RimWorldAccess.CharEd.Inventory.UndressMoveToInventory".Translate());
            }
            AddRow(sub, InventoryRowKind.ActionUndressDestroy, "RimWorldAccess.CharEd.Inventory.UndressDestroy".Translate());
            AddRow(sub, InventoryRowKind.ActionRedress, "RimWorldAccess.CharEd.Inventory.Redress".Translate());
            AddRow(sub, InventoryRowKind.ActionReequip, "RimWorldAccess.CharEd.Inventory.Reequip".Translate());
            AddRow(sub, InventoryRowKind.ActionReinvent, "RimWorldAccess.CharEd.Inventory.Reinvent".Translate());
            AddRow(sub, InventoryRowKind.ActionAddEquipment, "RimWorldAccess.CharEd.Inventory.AddEquipment".Translate());
            AddRow(sub, InventoryRowKind.ActionAddApparel, "RimWorldAccess.CharEd.Inventory.AddApparel".Translate());
            AddRow(sub, InventoryRowKind.ActionAddItem, "RimWorldAccess.CharEd.Inventory.AddItem".Translate());
        }

        /// <summary>Thing.LabelCap plus BlockInventory's own forced-apparel suffix (GetThingLabel, CEditor.cs).</summary>
        private static string GetInventoryThingLabel(Pawn pawn, Thing thing)
        {
            string text = thing.LabelCap;
            if (thing is Apparel ap && pawn.outfits != null && pawn.outfits.forcedHandler.IsForced(ap))
            {
                text += ", " + "ApparelForcedLower".Translate();
            }
            return text;
        }

        /// <summary>BlockInventory's own edibility gate for the per-row Ingest icon, verbatim (InterfaceIngest's caller, CEditor.cs).</summary>
        private static bool CanIngestInventoryThing(Pawn pawn, Thing thing)
        {
            try
            {
                return (thing.def.IsNutritionGivingIngestible || thing.def.IsNonMedicalDrug)
                    && thing.IngestibleNow && pawn.RaceProps.CanEverEat(thing);
            }
            catch
            {
                return false;
            }
        }

        private void RefreshInventorySectionInPlace(bool silent, string outcomeKey = null) =>
            RefreshSectionInPlace(SectionKind.Inventory, BuildInventorySection, silent, outcomeKey);

        // ---- Describe/Activate/Adjust dispatch. ----

        private void DescribeInventoryRow(RegionRow<InventoryRowKind> row, ElementDescription d)
        {
            switch (row.Kind)
            {
                case InventoryRowKind.ThingEntry:
                {
                    var entry = row.Payload as InventoryThingEntry;
                    Pawn pawn = CharEditorCompat.CurrentPawn;
                    Thing thing = entry?.Thing;
                    if (thing == null || pawn == null)
                    {
                        break;
                    }
                    d.Label = GetInventoryThingLabel(pawn, thing);
                    d.Role = ElementRole.ComboBox;
                    float mass = thing.GetStatValue(StatDefOf.Mass) * thing.stackCount;
                    d.Value = mass.ToStringMass();
                    var extras = new List<string>();
                    if (thing.def.useHitPoints)
                    {
                        extras.Add("RimWorldAccess.CharEd.Inventory.HitPointsValue".Translate(thing.HitPoints, thing.MaxHitPoints).ToString());
                    }
                    string desc = thing.DescriptionDetailed;
                    if (!desc.NullOrEmpty())
                    {
                        extras.Add(desc);
                    }
                    d.Extras = extras.Count > 0 ? string.Join(". ", extras) : null;
                    break;
                }
                case InventoryRowKind.ActionCopyEquipment:
                case InventoryRowKind.ActionCopyApparel:
                case InventoryRowKind.ActionCopyInventory:
                    d.Label = "Copy".Translate();
                    d.Role = ElementRole.Button;
                    break;
                case InventoryRowKind.ActionCopyAllGear:
                    // Semantically a different action from the per-list Copy rows (it copies all
                    // three lists at once), so it carries a distinguishing label.
                    d.Label = "RimWorldAccess.CharEd.Inventory.CopyAllGear".Translate();
                    d.Role = ElementRole.Button;
                    break;
                case InventoryRowKind.ActionPasteEquipment:
                    DescribeInventoryPasteRow(d, CharEditorCompat.HasWeaponClipboard(window));
                    break;
                case InventoryRowKind.ActionPasteApparel:
                    DescribeInventoryPasteRow(d, CharEditorCompat.HasApparelClipboard(window));
                    break;
                case InventoryRowKind.ActionPasteInventory:
                    DescribeInventoryPasteRow(d, CharEditorCompat.HasInventoryClipboard(window));
                    break;
                case InventoryRowKind.ActionPasteAllGear:
                    DescribeInventoryPasteRow(d, CharEditorCompat.HasAnyInventoryClipboard(window));
                    d.Label = "RimWorldAccess.CharEd.Inventory.PasteAllGear".Translate();
                    break;
                case InventoryRowKind.ActionUndressDrop:
                    d.Label = "RimWorldAccess.CharEd.Inventory.UndressDrop".Translate();
                    d.Role = ElementRole.Button;
                    break;
                case InventoryRowKind.ActionUndressMoveToInventory:
                    d.Label = "RimWorldAccess.CharEd.Inventory.UndressMoveToInventory".Translate();
                    d.Role = ElementRole.Button;
                    break;
                case InventoryRowKind.ActionUndressDestroy:
                    d.Label = "RimWorldAccess.CharEd.Inventory.UndressDestroy".Translate();
                    d.Role = ElementRole.Button;
                    break;
                case InventoryRowKind.ActionRedress:
                    d.Label = "RimWorldAccess.CharEd.Inventory.Redress".Translate();
                    d.Role = ElementRole.Button;
                    break;
                case InventoryRowKind.ActionReequip:
                    d.Label = "RimWorldAccess.CharEd.Inventory.Reequip".Translate();
                    d.Role = ElementRole.Button;
                    break;
                case InventoryRowKind.ActionReinvent:
                    d.Label = "RimWorldAccess.CharEd.Inventory.Reinvent".Translate();
                    d.Role = ElementRole.Button;
                    break;
                case InventoryRowKind.ActionAddEquipment:
                    d.Label = "RimWorldAccess.CharEd.Inventory.AddEquipment".Translate();
                    d.Role = ElementRole.Button;
                    break;
                case InventoryRowKind.ActionAddApparel:
                    d.Label = "RimWorldAccess.CharEd.Inventory.AddApparel".Translate();
                    d.Role = ElementRole.Button;
                    break;
                case InventoryRowKind.ActionAddItem:
                    d.Label = "RimWorldAccess.CharEd.Inventory.AddItem".Translate();
                    d.Role = ElementRole.Button;
                    break;
            }
        }

        private static void DescribeInventoryPasteRow(ElementDescription d, bool hasClipboard)
        {
            d.Label = "Paste".Translate();
            d.Role = ElementRole.Button;
            d.Disabled = !hasClipboard;
            d.Extras = hasClipboard ? null : "RimWorldAccess.CharEd.Character.NothingToPaste".Translate().ToString();
        }

        private void ActivateInventoryRow(RegionRow<InventoryRowKind> row, InspectionTreeItem item)
        {
            switch (row.Kind)
            {
                case InventoryRowKind.ThingEntry:
                {
                    var entry = row.Payload as InventoryThingEntry;
                    if (entry?.Thing is ThingWithComps twc)
                    {
                        CharEditorObjectsCompat.OpenForEdit(twc, entry.Mode);
                        // Dialog opened; OnFocus's silent RefreshInventorySectionInPlace covers the return.
                    }
                    else
                    {
                        SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    }
                    break;
                }
                case InventoryRowKind.ActionCopyEquipment:
                    CharEditorCompat.CopyEquipment(window);
                    TolkHelper.SpeakData("RimWorldAccess.CharEd.Inventory.EquipmentCopied".Translate().ToString());
                    break;
                case InventoryRowKind.ActionPasteEquipment:
                    PerformInventoryPaste(CharEditorCompat.HasWeaponClipboard(window), () => CharEditorCompat.PasteEquipment(window));
                    break;
                case InventoryRowKind.ActionCopyApparel:
                    CharEditorCompat.CopyApparel(window);
                    TolkHelper.SpeakData("RimWorldAccess.CharEd.Inventory.ApparelCopied".Translate().ToString());
                    break;
                case InventoryRowKind.ActionPasteApparel:
                    PerformInventoryPaste(CharEditorCompat.HasApparelClipboard(window), () => CharEditorCompat.PasteApparel(window));
                    break;
                case InventoryRowKind.ActionCopyInventory:
                    CharEditorCompat.CopyInventory(window);
                    TolkHelper.SpeakData("RimWorldAccess.CharEd.Inventory.InventoryCopied".Translate().ToString());
                    break;
                case InventoryRowKind.ActionPasteInventory:
                    PerformInventoryPaste(CharEditorCompat.HasInventoryClipboard(window), () => CharEditorCompat.PasteInventory(window));
                    break;
                case InventoryRowKind.ActionCopyAllGear:
                    CharEditorCompat.CopyAllGear(window);
                    TolkHelper.SpeakData("RimWorldAccess.CharEd.Inventory.AllGearCopied".Translate().ToString());
                    break;
                case InventoryRowKind.ActionPasteAllGear:
                    PerformInventoryPaste(CharEditorCompat.HasAnyInventoryClipboard(window), () => CharEditorCompat.PasteAllGear(window));
                    break;
                case InventoryRowKind.ActionUndressDrop:
                    CharEditorCompat.Undress(window);
                    RefreshInventorySectionInPlace(silent: false);
                    break;
                case InventoryRowKind.ActionUndressMoveToInventory:
                    CharEditorCompat.Undress(window);
                    RefreshInventorySectionInPlace(silent: false);
                    break;
                case InventoryRowKind.ActionUndressDestroy:
                    CharEditorCompat.UndressDestroy(window);
                    RefreshInventorySectionInPlace(silent: false);
                    break;
                case InventoryRowKind.ActionRedress:
                    CharEditorCompat.Redress(window);
                    RefreshInventorySectionInPlace(silent: false);
                    break;
                case InventoryRowKind.ActionReequip:
                    CharEditorCompat.Reequip(window);
                    RefreshInventorySectionInPlace(silent: false);
                    break;
                case InventoryRowKind.ActionReinvent:
                    CharEditorCompat.Reinvent(window);
                    RefreshInventorySectionInPlace(silent: false);
                    break;
                case InventoryRowKind.ActionAddEquipment:
                    CharEditorCompat.AddEquipment(window);
                    // Dialog opened; OnFocus's silent RefreshInventorySectionInPlace covers the return.
                    break;
                case InventoryRowKind.ActionAddApparel:
                    CharEditorCompat.AddApparel(window);
                    break;
                case InventoryRowKind.ActionAddItem:
                    CharEditorCompat.AddItem(window);
                    break;
            }
        }

        private void PerformInventoryPaste(bool hasClipboard, Action paste)
        {
            if (!hasClipboard)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                AnnounceCurrentItem();
                return;
            }
            paste();
            RefreshInventorySectionInPlace(silent: false);
        }

        /// <summary>No Inventory row steps in place: ThingEntry's own value is edited through DialogObjects (Enter), not Left/Right, matching HealthRowKind.ConditionEntry's identical reasoning; every other row here is a plain button.</summary>
        private bool AdjustInventoryRow(RegionRow<InventoryRowKind> row, InspectionTreeItem item, int direction)
        {
            return false;
        }
    }
}
