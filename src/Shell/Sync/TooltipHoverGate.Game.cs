using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The replacement <c>Verse.Mouse::IsOver</c> installed at call sites
    /// <see cref="TooltipGateAnalysis"/> certified as tooltip-only.
    /// </summary>
    public static class TooltipHoverGate
    {
        /// <summary>
        /// Deliberately rect-independent: during a harvest every certified site
        /// on the surface should register, so one pass recovers the whole
        /// surface's tooltips rather than the one element a cursor would be
        /// over. The rect parameter stays because the transpiler swaps a call
        /// operand and the stack shape has to match <see cref="Mouse.IsOver"/>.
        /// </summary>
        public static bool IsOverOrHarvesting(Rect rect)
        {
            return Mouse.IsOver(rect) || TooltipCapture.HarvestWantsHover();
        }
    }
}
