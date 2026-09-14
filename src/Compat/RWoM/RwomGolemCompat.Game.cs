using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// RimWorld of Magic's golem overview table (<c>MainTabWindow_Golems</c>)
    /// and its map-view energy status gizmo (<c>Gizmo_EnergyStatus</c>).
    /// <see cref="TryRegister"/> declines silently when the golems feature
    /// itself isn't present (<c>MainTabWindow_Golems</c> unresolvable); each of
    /// the four features below (window attachment, three column handlers, one
    /// gizmo handler) resolves and registers independently, so one feature's
    /// member drift declines only that feature rather than the whole compat
    /// surface.
    /// </summary>
    internal static class RwomGolemCompat
    {
        public static void TryRegister()
        {
            try
            {
                Type golemsWindowType = AccessTools.TypeByName("TorannMagic.Golems.MainTabWindow_Golems");
                if (golemsWindowType == null)
                {
                    return; // RimWorld of Magic (or its golems feature) isn't loaded.
                }

                TryRegisterWindowScope(golemsWindowType);
                RwomGolemNeedsColumnHandler.TryRegister();
                RwomGolemSliderColumnHandler.TryRegisterAll();
                RwomGolemMasterColumnHandler.TryRegister();
                RwomEnergyStatusGizmoHandler.TryRegister();

            }
            catch (Exception ex)
            {
                ModLogger.Error($"RimWorld of Magic golem compat registration failed: {ex.Message}");
            }
        }

        /// <summary>
        /// <c>MainTabWindow_Golems</c> extends <c>MainTabWindow</c> directly
        /// (not <see cref="RimWorld.MainTabWindow_PawnTable"/>) and builds its
        /// own <see cref="PawnTable"/> over a private <c>table</c> field —
        /// exactly the shape <see cref="GenericPawnTableScope.TryCreateForTable"/>
        /// exists to serve.
        /// </summary>
        private static void TryRegisterWindowScope(Type golemsWindowType)
        {
            try
            {
                FieldInfo tableField = AccessTools.Field(golemsWindowType, "table");
                if (tableField == null || tableField.FieldType != typeof(PawnTable))
                {
                    ModLogger.Error("RwomGolemCompat: could not resolve MainTabWindow_Golems.table; declining the golem table window attachment.");
                    return;
                }

                ScopeForWindow.Register(golemsWindowType, delegate (Window w)
                {
                    return GenericPawnTableScope.TryCreateForTable(w as MainTabWindow, () => (PawnTable)tableField.GetValue(w));
                });
            }
            catch (Exception ex)
            {
                ModLogger.Error($"RwomGolemCompat window scope registration failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Shared master-assignment float menu, mirroring
        /// <c>GolemUtility.MasterButton</c>'s own option set (GolemUtility.cs:
        /// 43-76): a leading "None" plus every golemancer in the golem pawn's
        /// faction except the current master (its own <c>.Where(x => x !=
        /// cg.pawnMaster)</c>, :51). Options carry the source's own
        /// <c>LabelShort</c>. The source matches the clicked option back to a
        /// pawn by comparing LabelShort strings; callers here hold the actual
        /// <see cref="Pawn"/> references already, so the chosen pawn is passed
        /// straight to <paramref name="assignMaster"/> — the same option set
        /// and resulting assignment, immune to the source's own same-name
        /// FirstOrDefault collision. The write itself stays in each caller
        /// (with its own MUTATION-C citation): the golem table cell needs only
        /// the bare assignment, the golem tab row also rebuilds and announces.
        /// </summary>
        internal static void OpenMasterMenu(Pawn golemPawn, Pawn currentMaster,
            MethodInfo golemancersInFactionMethod, Action<Pawn> assignMaster)
        {
            var options = new List<FloatMenuOption>();
            options.Add(new FloatMenuOption("RimWorldAccess.Compat.Rwom.GolemMasterNone".Translate(),
                () => assignMaster(null)));
            var golemancers = golemancersInFactionMethod.Invoke(null, new object[] { golemPawn.Faction }) as List<Pawn>;
            if (golemancers != null)
            {
                foreach (Pawn candidate in golemancers)
                {
                    if (candidate == currentMaster)
                    {
                        continue;
                    }
                    Pawn captured = candidate;
                    options.Add(new FloatMenuOption(captured.LabelShort, () => assignMaster(captured)));
                }
            }
            WindowlessFloatMenuState.Open(options, colonistOrders: false);
        }

        /// <summary>Shared by every golem column and gizmo handler: the ThingComp on a golem pawn (spawned or dormant-held) whose runtime type matches the given reflected comp type.</summary>
        internal static object FindComp(Pawn pawn, Type compType)
        {
            if (pawn?.AllComps == null || compType == null)
            {
                return null;
            }
            foreach (ThingComp comp in pawn.AllComps)
            {
                if (compType.IsInstanceOfType(comp))
                {
                    return comp;
                }
            }
            return null;
        }
    }
}
