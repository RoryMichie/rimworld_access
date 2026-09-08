using System;
using System.Collections;
using System.Reflection;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Read-only gear rows for JecsTools' CompSlotLoadable (Source/
    /// AllModdingComponents/CompSlotLoadable/). RimWorld of Magic ships it as a
    /// separate optional assembly, so this compat stays dormant whenever the
    /// type cannot be resolved. When present, each equipped/worn item with slots
    /// gets one child row per slot: "Slot {0}: {1}" (the slot's own label, and
    /// its occupant's LabelCap or "Empty").
    ///
    /// SlotLoadable itself extends Verse.Thing and SlotOccupant returns a plain
    /// Thing, so both cast safely to the compile-time-known Verse.Thing once
    /// resolved — only CompSlotLoadable.Slots and SlotLoadable.SlotOccupant
    /// need member lookups.
    ///
    /// Slotting itself is a job-driven float menu (SlotLoadableFloatMenuPatch)
    /// that already flows through the vanilla float-menu path — this adds the
    /// read-only rows only; no actions, no mutations.
    /// </summary>
    internal static class JecsGearSlotCompat
    {
        private static Type compType;   // CompSlotLoadable.CompSlotLoadable
        private static Type slotType;   // CompSlotLoadable.SlotLoadable
        private static MemberInfo slotsMember;         // CompSlotLoadable.Slots : List<SlotLoadable>
        private static MemberInfo slotOccupantMember;  // SlotLoadable.SlotOccupant : Thing

        private static bool ready;

        public static void TryRegister()
        {
            try
            {
                var surface = new ReflectionSurface("JecsGearSlotCompat");
                compType = surface.Type("CompSlotLoadable.CompSlotLoadable");
                slotType = surface.Type("CompSlotLoadable.SlotLoadable");
                slotsMember = surface.FieldOrProperty(compType, "Slots");
                slotOccupantMember = surface.FieldOrProperty(slotType, "SlotOccupant");
                ready = surface.Ready;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"JecsGearSlotCompat registration failed: {ex.Message}");
            }
        }

        /// <summary>Cheap probe for PawnGearAdapter's expandability decision.</summary>
        internal static bool HasSlots(Thing thing)
        {
            if (!ready || thing == null)
                return false;
            object comp = FindComp(thing);
            if (comp == null)
                return false;
            IList slots = ReflectionSurface.ValueOf(slotsMember, comp) as IList;
            return slots != null && slots.Count > 0;
        }

        /// <summary>Appends one "Slot {0}: {1}" detail line per slot.</summary>
        internal static void AppendSlotRows(InspectionTreeItem parent, Thing thing)
        {
            if (!ready || thing == null)
                return;

            try
            {
                object comp = FindComp(thing);
                if (comp == null)
                    return;
                IList slots = ReflectionSurface.ValueOf(slotsMember, comp) as IList;
                if (slots == null)
                    return;

                foreach (object slotObj in slots)
                {
                    if (!(slotObj is Thing slotThing))
                        continue;
                    Thing occupant = ReflectionSurface.ValueOf(slotOccupantMember, slotObj) as Thing;
                    string occupantLabel = occupant != null
                        ? occupant.LabelCap
                        : "RimWorldAccess.Compat.Jecs.GearSlotEmpty".Translate().ToString();
                    InspectNodeFactory.DetailLine(parent,
                        "RimWorldAccess.Compat.Jecs.GearSlot".Translate(slotThing.Label, occupantLabel));
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error($"JecsGearSlotCompat.AppendSlotRows failed: {ex.Message}");
            }
        }

        private static object FindComp(Thing thing)
        {
            if (thing is ThingWithComps twc && twc.AllComps != null)
            {
                foreach (ThingComp c in twc.AllComps)
                {
                    if (compType.IsInstanceOfType(c))
                        return c;
                }
            }
            return null;
        }

    }
}
