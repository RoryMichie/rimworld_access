using System.Collections.Generic;
using System.Linq;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// The one "open this designator's options" ladder, shared by the architect tree, active
    /// placement, and gizmo navigation: a registered provider's merged menu first, then the
    /// designator's own RightClickFloatMenuOptions, then the no-options announcement.
    /// </summary>
    public static class DesignatorOptionsOpener
    {
        public static void Open(Designator designator)
        {
            if (designator == null)
            {
                TolkHelper.Speak("RimWorldAccess.Building.Architect.NoDesignatorSelected".Loc());
                return;
            }

            // A mod may own a MERGED menu for this designator (its own entries plus the
            // vanilla ones) -- Allow Tool builds exactly that. Riding its opener keeps the
            // ordering and the entry-enabled settings the mod itself applies.
            bool announced = false;
            if (DesignatorContextMenuRouter.HasOptions(designator))
            {
                TolkHelper.Speak("RimWorldAccess.Building.Architect.OptionsFor".Loc(designator.LabelCap));
                announced = true;
                if (DesignatorContextMenuRouter.TryOpen(designator))
                    return;
            }

            List<FloatMenuOption> options = designator.RightClickFloatMenuOptions?.ToList();
            if (options == null || options.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.Building.Architect.NoAdditionalOptions".Loc());
                return;
            }

            if (!announced)
                TolkHelper.Speak("RimWorldAccess.Building.Architect.OptionsFor".Loc(designator.LabelCap));
            WindowlessFloatMenuState.Open(options, false);
        }
    }
}
