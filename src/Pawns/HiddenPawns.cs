using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// The player-visibility gate for pawns standing on the map, mirroring what
    /// vanilla does at every surface that reports a cell's contents.
    ///
    /// Vanilla drops <c>InvisibilityUtility.IsHiddenFromPlayer</c> pawns from
    /// MouseoverReadout (Verse/MouseoverReadout.cs:127), CellInspectorDrawer
    /// (Verse/CellInspectorDrawer.cs:91), GenUI.ThingsUnderMouse (Verse/GenUI.cs:510),
    /// Dialog_MapSearch (RimWorld/Dialog_MapSearch.cs:169) and
    /// ThingSelectionUtility.SelectableByMapClick (RimWorld/ThingSelectionUtility.cs:25),
    /// and Pawn.Tick actively deselects one that slips through (Verse/Pawn.cs:1563).
    /// The predicate exempts player-faction pawns and honors
    /// DebugSettings.showHiddenPawns, so a colonist under an invisibility psycast
    /// still reads normally and dev mode still reveals everything, exactly as for
    /// a sighted player.
    ///
    /// Only presentation and command surfaces filter. Placement, pathing,
    /// enclosure and obstacle checks must keep seeing hidden pawns, because the
    /// game itself does: an invisible body still occupies its cell.
    ///
    /// A mod that implements invisibility WITHOUT vanilla's
    /// HediffComp_Invisibility is deliberately not covered. RimWorld of Magic's
    /// TM_InvisibilityHD is the live example: it skips PawnRenderer.RenderPawnAt
    /// but never opts into the vanilla contract, so vanilla's own mouseover
    /// readout, cell inspector and map search all still name that pawn. Hiding it
    /// here would tell a screen reader user less than a sighted player is told.
    /// </summary>
    public static class HiddenPawns
    {
        /// <summary>
        /// True when this thing is a pawn vanilla hides from the player.
        /// </summary>
        public static bool IsHidden(Thing thing)
        {
            return thing is Pawn pawn && pawn.IsHiddenFromPlayer();
        }

        /// <summary>
        /// The input sequence with vanilla-hidden pawns removed. Non-pawn things
        /// pass through untouched.
        /// </summary>
        public static IEnumerable<Thing> Visible(IEnumerable<Thing> things)
        {
            return things.Where(t => !IsHidden(t));
        }
    }
}
