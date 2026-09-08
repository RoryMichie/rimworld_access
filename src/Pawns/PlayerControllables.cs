using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Mod-agnostic signal for "player-commandable pawn that is neither a
    /// colonist nor a colony mech" — the prime case is a Vehicle Framework
    /// vehicle (Vehicles.VehiclePawn, a Pawn subclass), but nothing here
    /// names any mod. The vanilla drafter allocation plus ShowDraftGizmo
    /// covers vanilla pawns and every mod that allocates Pawn.drafter
    /// (Vehicle Framework force-creates one for vehicles and mirrors its
    /// ignition state into it), so a mod that wants its units to show up on
    /// the accessible colonist bar only has to allocate a drafter like
    /// vanilla does — no cooperation with this mod required.
    /// </summary>
    public static class PlayerControllables
    {
        /// <summary>
        /// True for a spawned, selectable, player-faction pawn that shows a
        /// draft gizmo but is not a colonist or a colony mech.
        /// Pawn_DraftController.ShowDraftGizmo is true by default and false
        /// only for uncontrolled mechs and subhumans, so this returns true
        /// for a player vehicle, and false for animals (no drafter),
        /// guests/enemies (faction), and prisoners.
        /// </summary>
        public static bool IsOtherControllable(Pawn p) =>
            p != null
            && p.Faction == Faction.OfPlayer
            && p.Spawned
            && p.def.selectable
            && p.drafter != null
            && p.drafter.ShowDraftGizmo
            && !p.IsColonist
            && !p.IsColonyMech;

        /// <summary>
        /// All other-controllable pawns on the given map, ordered by def
        /// label (groups same-type units together) then by natural-sort
        /// pawn label ("Bus 2" before "Bus 10") — there is no vanilla
        /// ordering for this set.
        /// </summary>
        public static List<Pawn> OnMap(Map map)
        {
            if (map == null)
                return new List<Pawn>();

            return map.mapPawns.AllPawnsSpawned
                .Where(IsOtherControllable)
                .OrderBy(p => p.def.label)
                .ThenBy(p => p.Label, NaturalStringComparer.Instance)
                .ToList();
        }
    }
}
