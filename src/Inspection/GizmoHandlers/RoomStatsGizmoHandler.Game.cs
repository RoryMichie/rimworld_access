using System.Reflection;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Port of the original GetGizmoLabel ladder's "Gizmo_RoomStats" branch
    /// (GetRoomStatsLabel): resolves the gizmo's private `building` field, asks
    /// the game's own Gizmo_RoomStats.GetRoomToShowStatsFor for the room, and
    /// speaks the shared TileInfoHelper.GetRoomStatsInfo readout. That readout
    /// (room role, then every non-hidden RoomStatDef with stage label and score)
    /// is a superset of what the gizmo draws visually (role, impressiveness
    /// stage with score, and each non-hidden stat's stage with score — see
    /// decompiled Verse.Gizmo_RoomStats.GizmoOnGUI), so the label facet carries
    /// the full sighted-player content and the status/description facets
    /// deliberately defer. Note the gizmo type lives in the Verse namespace,
    /// not RimWorld.
    /// </summary>
    internal sealed class RoomStatsGizmoHandler : GizmoHandlerBase
    {
        /// <summary>
        /// Gizmo_RoomStats keeps its building in a private instance field; cache
        /// the FieldInfo once rather than re-reflecting per announcement.
        /// </summary>
        private static readonly FieldInfo BuildingField = typeof(Gizmo_RoomStats)
            .GetField("building", BindingFlags.Instance | BindingFlags.NonPublic);

        public override bool TryGetLabel(Gizmo gizmo, out string label)
        {
            label = null;
            if (!(gizmo is Gizmo_RoomStats))
                return false;

            try
            {
                var building = BuildingField?.GetValue(gizmo) as Building;
                if (building != null)
                {
                    // The game's own resolver handles impassable buildings,
                    // interaction cells, fog, and roleless rooms.
                    Room room = Gizmo_RoomStats.GetRoomToShowStatsFor(building);
                    if (room != null)
                    {
                        string info = TileInfoHelper.GetRoomStatsInfo(room);
                        if (!string.IsNullOrEmpty(info))
                        {
                            label = info;
                            return true;
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                ModLogger.Error($"Exception reading Gizmo_RoomStats: {ex.Message}");
            }

            label = "RimWorldAccess.Inspection.Gizmo.Type.RoomStatsFallback".Translate();
            return true;
        }

        // TryGetStatus / TryGetDescription intentionally not overridden: the
        // label above already contains the gizmo's complete visual content
        // (verified against decompiled Verse.Gizmo_RoomStats.GizmoOnGUI), so
        // both facets defer to the base no-op to avoid redundant announcements.
    }
}
