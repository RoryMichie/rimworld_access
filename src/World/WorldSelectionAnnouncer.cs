using System.Collections.Generic;
using RimWorld.Planet;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// The shared grammar for "what is selected on the planet view now" — the world twin of
    /// <see cref="MapSelectionAnnouncer"/>. The per-object wording is
    /// <see cref="WorldObjectSelectionState.GetObjectLabel"/> rather than a second copy of it, so
    /// the click announcement and the keyboard world-object flow cannot drift apart.
    /// </summary>
    internal static class WorldSelectionAnnouncer
    {
        /// <summary>
        /// Describes a single selected world object, or null when the object has nothing to say.
        /// </summary>
        internal static string Describe(WorldObject selected)
        {
            return selected != null ? WorldObjectSelectionState.GetObjectLabel(selected) : null;
        }

        /// <summary>
        /// Describes a whole selection as one announcement: the object itself when there is one,
        /// a count when there are several, the cleared phrase when there are none.
        /// </summary>
        internal static string DescribeSelection(IReadOnlyList<WorldObject> selection)
        {
            if (selection == null || selection.Count == 0)
                return "RimWorldAccess.World.Selection.Cleared".Translate().ToString();
            if (selection.Count == 1)
                return Describe(selection[0]);
            return "RimWorldAccess.World.Selection.Multiple".Translate(selection.Count).ToString();
        }
    }
}
