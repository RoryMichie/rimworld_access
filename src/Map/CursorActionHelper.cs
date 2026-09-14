using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// One-shot map-cursor actions driven by the ambient map keys: announce the
    /// cursor coordinates (K), toggle/clear forbid state at the cursor tile (F),
    /// unforbid every item on the map (Alt+F), and open an info card for whatever
    /// is under the cursor (Alt+I). Holds the logic verbatim from the legacy
    /// handlers so the MapScope claims can delegate to it.
    /// </summary>
    public static class CursorActionHelper
    {
        /// <summary>Speaks the cursor tile's map coordinates ("x, z").</summary>
        public static void AnnounceCursorCoordinates()
        {
            IntVec3 pos = MapNavigationState.CurrentCursorPosition;
            TolkHelper.Speak("RimWorldAccess.Input.Cursor.Coordinates".Loc(pos.x, pos.z));
        }

        /// <summary>Unforbids every forbiddable item on the current map.</summary>
        public static void UnforbidAllItems()
        {
            if (!GuardHelper.RequireMap(out Map map)) return;

            // Get all things on the map
            List<Thing> allThings = map.listerThings.AllThings;
            int unforbiddenCount = 0;

            // Iterate through all things and unforbid items
            foreach (Thing thing in allThings)
            {
                // Check if the thing can be forbidden (has CompForbiddable component)
                CompForbiddable forbiddable = thing.TryGetComp<CompForbiddable>();

                // If it has the component and is currently forbidden, unforbid it
                if (forbiddable != null && forbiddable.Forbidden)
                {
                    thing.SetForbidden(false, warnOnFail: false);
                    unforbiddenCount++;
                }
            }

            // Announce result to user
            if (unforbiddenCount == 0)
            {
                TolkHelper.Speak("RimWorldAccess.Input.Unforbid.NoForbiddenItems".Loc());
            }
            else if (unforbiddenCount == 1)
            {
                TolkHelper.Speak("RimWorldAccess.Input.Unforbid.CountOne".Loc());
                SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.Input.Unforbid.CountMany".Loc(unforbiddenCount));
                SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
            }


            // Freeing the starting items is the natural lead-in to building: the player now has
            // materials to work with and will want somewhere to store them. Teach the building
            // concept (rooms vs. buildings, the Architect menu) before they go looking for it.
            if (unforbiddenCount > 0)
                DocsTeacher.Teach("RWA_BuildingBasics");
        }

        /// <summary>
        /// Opens an info card for the thing(s) under the cursor: one thing opens
        /// its card directly, several open a picker, none falls back to terrain.
        /// </summary>
        public static void OpenInfoCardAtCursor()
        {
            IntVec3 pos = MapNavigationState.CurrentCursorPosition;
            Map map = Find.CurrentMap;

            if (!pos.IsValid || !pos.InBounds(map))
            {
                TolkHelper.Speak("RimWorldAccess.Input.Cursor.NothingToInspect".Loc());
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }

            // Fogged tiles surface no info beyond "Undiscovered" — matches vanilla's
            // MouseoverReadout, which exits early without showing terrain or things.
            if (pos.Fogged(map))
            {
                TolkHelper.Speak("Undiscovered".Loc());
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }

            // Gather all things at cursor position, sorted by AltitudeLayer descending
            // (matches TileInfoHelper ordering - highest layer first)
            var things = pos.GetThingList(map)
                .Where(t => !(t is Mote) && t.def.category != ThingCategory.Mote)
                .Where(t => !HiddenPawns.IsHidden(t))
                .OrderByDescending(t => (int)t.def.altitudeLayer)
                .ToList();

            TerrainDef terrain = map.terrainGrid.TerrainAt(pos);

            if (things.Count == 1)
            {
                // Single thing - open its info card directly
                Find.WindowStack.Add(new Dialog_InfoCard(things[0]));
            }
            else if (things.Count == 0)
            {
                if (terrain != null)
                {
                    // Only terrain at this position
                    Find.WindowStack.Add(new Dialog_InfoCard(terrain));
                }
                else
                {
                    // Nothing at all
                    TolkHelper.Speak("RimWorldAccess.Input.Cursor.NothingToInspect".Loc());
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                }
            }
            else
            {
                // Multiple things - show selection menu with terrain as last option
                var options = new List<FloatMenuOption>();

                foreach (var thing in things)
                {
                    var capturedThing = thing;
                    options.Add(new FloatMenuOption(
                        capturedThing.LabelCapNoCount.StripTags(),
                        () => Find.WindowStack.Add(new Dialog_InfoCard(capturedThing))
                    ));
                }

                if (terrain != null)
                {
                    var capturedTerrain = terrain;
                    options.Add(new FloatMenuOption(
                        ((string)capturedTerrain.LabelCap).StripTags(),
                        () => Find.WindowStack.Add(new Dialog_InfoCard(capturedTerrain))
                    ));
                }

                WindowlessFloatMenuState.Open(options, colonistOrders: false);
            }
        }

        private static float lastForbidToggleTime = 0f;
        private const float ForbidToggleCooldown = 0.3f;

        /// <summary>
        /// Toggles forbid/unforbid on items at the current cursor position.
        /// </summary>
        public static void ToggleForbidAtCursor()
        {
            // Cooldown to prevent accidental double-presses
            if (Time.time - lastForbidToggleTime < ForbidToggleCooldown)
                return;
            lastForbidToggleTime = Time.time;

            IntVec3 position = MapNavigationState.CurrentCursorPosition;
            Map map = Find.CurrentMap;

            List<Thing> allThings = position.GetThingList(map);
            List<Thing> forbiddableItems = new List<Thing>();

            foreach (Thing thing in allThings)
            {
                CompForbiddable forbiddable = thing.TryGetComp<CompForbiddable>();
                if (forbiddable != null)
                {
                    forbiddableItems.Add(thing);
                }
            }

            if (forbiddableItems.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.Input.Forbid.NothingToForbid".Loc());
                return;
            }

            // Determine if we should forbid or unforbid
            // If any item is unforbidden, forbid all. If all are forbidden, unforbid all.
            bool shouldForbid = forbiddableItems.Any(t => !t.TryGetComp<CompForbiddable>().Forbidden);

            int toggledCount = 0;
            string firstItemName = null;

            foreach (Thing item in forbiddableItems)
            {
                if (firstItemName == null)
                    firstItemName = item.LabelShort;
                item.SetForbidden(shouldForbid, warnOnFail: false);
                toggledCount++;
            }

            string announcement;
            if (toggledCount == 1)
            {
                announcement = shouldForbid
                    ? "RimWorldAccess.Input.Forbid.SingleForbidden".Translate(firstItemName).ToString()
                    : "RimWorldAccess.Input.Forbid.SingleUnforbidden".Translate(firstItemName).ToString();
            }
            else
            {
                announcement = shouldForbid
                    ? "RimWorldAccess.Input.Forbid.ManyForbidden".Translate(toggledCount).ToString()
                    : "RimWorldAccess.Input.Forbid.ManyUnforbidden".Translate(toggledCount).ToString();
            }

            TolkHelper.SpeakData(announcement);
        }
    }
}
