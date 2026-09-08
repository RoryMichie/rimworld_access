using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;

namespace RimWorldAccess
{
    /// <summary>
    /// Ends a vanilla hang our keyboard paths can reach: FindBestScale shrinks its scale in an
    /// unbounded while(true) that never terminates once MaxColonistBarWidth (screen width minus
    /// the bar margins) is zero or negative — Unity reports garbage resolutions like 144x1 while
    /// a connected display has no signal. Vanilla never hits it because its draw is gated behind
    /// ColonistBar.Visible (screenWidth >= 800), but ColonistBar.Entries recaches ungated, and
    /// comma/period colonist cycling reads it straight from the keypress. For any positive width
    /// the loop provably terminates, so the guard fires only where the bar is invisible anyway,
    /// answering with the method's own empty-entries result.
    /// </summary>
    [HarmonyPatch(typeof(ColonistBarDrawLocsFinder), nameof(ColonistBarDrawLocsFinder.CalculateDrawLocs),
        new Type[] { typeof(List<Vector2>), typeof(float), typeof(int) },
        new ArgumentType[] { ArgumentType.Normal, ArgumentType.Out, ArgumentType.Normal })]
    public static class ColonistBarLayoutGuardPatch
    {
        private static readonly Func<float> maxColonistBarWidth =
            AccessTools.MethodDelegate<Func<float>>(
                AccessTools.PropertyGetter(typeof(ColonistBarDrawLocsFinder), "MaxColonistBarWidth"));

        [HarmonyPrefix]
        public static bool Prefix(List<Vector2> outDrawLocs, ref float scale)
        {
            if (maxColonistBarWidth() > 0f)
                return true;
            outDrawLocs.Clear();
            scale = 1f;
            return false;
        }
    }
}
