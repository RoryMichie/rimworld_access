using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Records each candidate pawn portrait of the <see cref="Dialog_BeginLordJob"/> family
    /// (ritual, psychic ritual, gravship launch, and any modded subclass) for
    /// <see cref="LordJobDialogScope"/>'s focus ring. Every portrait in the widget's grid goes
    /// through one vanilla method carrying both the pawn and its rect —
    /// <c>PawnRoleSelectionWidgetBase&lt;RoleType&gt;.DrawPawnPortraitInternal</c> (decompiled
    /// RimWorld/PawnRoleSelectionWidgetBase.cs:431) — which is all a ring needs.
    /// </summary>
    internal static class RitualPawnRowDrawPatch
    {
        internal static readonly RowDrawCapture Rows = new RowDrawCapture();

        internal static void Record(Pawn pawn, Rect tileRect)
        {
            try
            {
                if (!LordJobDialogState.IsActive || pawn == null)
                {
                    return;
                }
                Rows.Record(pawn, tileRect);
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Ritual pawn row capture error", ex);
            }
        }
    }

    /// <summary>
    /// Harmony cannot patch the open generic <c>PawnRoleSelectionWidgetBase&lt;&gt;</c>, so this
    /// targets ONE closed instantiation: RoleType is constrained <c>class, ILordJobRole</c>, so
    /// every instantiation is a reference type and Mono's generic code sharing gives them all the
    /// same method body — patching the <c>ILordJobRole</c> form covers every subclass widget,
    /// vanilla and modded. Same shape as <see cref="RenameDrawPatch"/>; <c>__instance</c> is not
    /// injected because the identity a ring needs is the <c>pawn</c> argument alone.
    /// </summary>
    [HarmonyPatch]
    internal static class PawnPortraitDrawPatch
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(PawnRoleSelectionWidgetBase<ILordJobRole>), "DrawPawnPortraitInternal");
        }

        /// <summary>The visible tile is the portrait plus the name strip beneath it (50f + 20f, both scaled).</summary>
        [HarmonyPostfix]
        public static void Postfix(Rect r, Pawn pawn, float scale)
        {
            RitualPawnRowDrawPatch.Record(pawn, new Rect(r.x, r.y, r.width * scale, 70f * scale));
        }
    }
}
