using System.Collections.Generic;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Extension seam for JecsTools ability gizmos (Command_PawnAbility):
    /// contributes an extra status-line suffix and/or extra keyboard options
    /// without <see cref="JecsAbilityCommandHandler"/> knowing about any
    /// specific overlay mod. RimWorld of Magic's autocast toggle is the first
    /// intended consumer; register via
    /// <see cref="JecsAbilityCommandHandler.RegisterExtension"/>.
    /// </summary>
    public interface IJecsAbilityExtension
    {
        /// <summary>Returns null/empty to contribute nothing to the status readout.</summary>
        string TryGetStatusSuffix(Gizmo gizmo);

        /// <summary>
        /// Mirrors IGizmoHandler.TryGetExtraOptions: append to
        /// <paramref name="options"/>, return whether anything was added.
        /// </summary>
        bool TryGetExtraOptions(Gizmo gizmo, List<FloatMenuOption> options);
    }
}
