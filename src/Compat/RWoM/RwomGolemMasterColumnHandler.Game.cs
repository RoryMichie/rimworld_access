using System;
using System.Reflection;
using RimWorld;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// TM_GolemMaster: mirrors <c>GolemUtility.MasterButton</c>
    /// (GolemUtility.cs:43-76) -- a <c>Widgets.ButtonText</c> whose click
    /// opens a float menu built from
    /// <c>TM_Calc.GolemancersInFaction(cg.PawnGolem.Faction)</c> (the row
    /// pawn's own Faction, since the column's <c>pawn</c> parameter IS that
    /// same golem pawn whether spawned or dormant-held) filtered to exclude
    /// the current master, plus a leading "None" option. The source matches
    /// options back to a golemancer by comparing <c>LabelShort</c> strings;
    /// this handler holds the actual <see cref="Pawn"/> references already
    /// (no need to round-trip through a name), so it compares/assigns by
    /// object reference instead -- the same option set and the same
    /// resulting assignment for every pawn, but immune to the source's own
    /// same-name collision risk.
    /// </summary>
    internal sealed class RwomGolemMasterColumnHandler : PawnColumnHandler
    {
        private readonly Type compGolemType;
        private readonly FieldInfo pawnMasterField;             // CompGolem.pawnMaster : Pawn
        private readonly MethodInfo golemancersInFactionMethod;  // TM_Calc.GolemancersInFaction(Faction) : List<Pawn>

        private RwomGolemMasterColumnHandler(Type compGolemType, FieldInfo pawnMasterField, MethodInfo golemancersInFactionMethod)
        {
            this.compGolemType = compGolemType;
            this.pawnMasterField = pawnMasterField;
            this.golemancersInFactionMethod = golemancersInFactionMethod;
        }

        public static void TryRegister()
        {
            try
            {
                var surface = new ReflectionSurface("RwomGolemMasterColumnHandler");
                Type workerType = surface.Type("TorannMagic.Golems.PawnColumnWorker_GolemMaster");
                Type compGolemType = surface.Type("TorannMagic.Golems.CompGolem");
                Type calcType = surface.Type("TorannMagic.TM_Calc");
                FieldInfo pawnMasterField = surface.Field(compGolemType, "pawnMaster");
                MethodInfo golemancersMethod = surface.Method(calcType, "GolemancersInFaction", new[] { typeof(Faction) });

                if (!surface.Ready)
                {
                    return;
                }

                PawnColumnHandlerRegistry.Register(workerType,
                    new RwomGolemMasterColumnHandler(compGolemType, pawnMasterField, golemancersMethod));
            }
            catch (Exception ex)
            {
                ModLogger.Error($"RwomGolemCompat golem master column registration failed: {ex.Message}");
            }
        }

        public override string CellText(PawnColumnDef def, Pawn pawn)
        {
            try
            {
                object cg = RwomGolemCompat.FindComp(pawn, compGolemType);
                if (cg == null)
                {
                    return "";
                }
                var master = pawnMasterField.GetValue(cg) as Pawn;
                return master != null
                    ? master.LabelShortCap.StripTags()
                    : "RimWorldAccess.Compat.Rwom.GolemMasterNone".Translate().ToString();
            }
            catch (Exception ex)
            {
                ModLogger.Error($"RwomGolemMasterColumnHandler.CellText failed: {ex.Message}");
                return "RimWorldAccess.Shell.Generic.CellNotReadable".Loc().ToString();
            }
        }

        public override PawnColumnActivation ActivateCell(PawnColumnDef def, Pawn pawn, PawnTable table)
        {
            object cg = RwomGolemCompat.FindComp(pawn, compGolemType);
            if (cg == null)
            {
                return PawnColumnActivation.NotHandled;
            }

            var currentMaster = pawnMasterField.GetValue(cg) as Pawn;
            RwomGolemCompat.OpenMasterMenu(pawn, currentMaster, golemancersInFactionMethod, delegate (Pawn newMaster)
            {
                // MUTATION-C: mirrors GolemUtility.MasterButton (GolemUtility.cs:
                // 43-76) -- its float menu options assign cg.pawnMaster directly
                // ("None" -> null, a golemancer -> that pawn); button and menu are
                // built inline in the draw pass with no Try*/Can* twin.
                pawnMasterField.SetValue(cg, newMaster);
            });
            return PawnColumnActivation.OpenedUI;
        }
    }
}
