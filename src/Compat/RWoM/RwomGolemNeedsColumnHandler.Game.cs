using System;
using System.Reflection;
using RimWorld;
using RimWorldAccess.Shell;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// TM_GolemNeeds: mirrors <c>PawnColumnWorker_GolemNeeds.DoCell</c>
    /// (Golems/PawnColumnWorker_GolemNeeds.cs:39-85) — a bare FillableBar with
    /// no numeric label; the percentage the bar fills IS the number, so
    /// presenting it as text is parity, not an addition. A spawned golem
    /// reads its own <c>TM_GolemEnergy</c> need (a percentage via the vanilla
    /// <see cref="Need"/> base's own <see cref="Need.CurLevelPercentage"/> —
    /// no TorannMagic type involved, per the source's own <c>Need need = ...</c>
    /// local at :53); a dormant golem (row is an unspawned golem pawn held
    /// inside its workstation building, per <c>MainTabWindow_Golems.Pawns</c>)
    /// reads the building's own energy handler instead, exactly the source's
    /// own fallback (:58-66). Read-only: the source draws no interactive
    /// control here.
    /// </summary>
    internal sealed class RwomGolemNeedsColumnHandler : PawnColumnHandler
    {
        private readonly NeedDef golemEnergyNeedDef;
        private readonly Type buildingGolemBaseType;
        private readonly PropertyInfo buildingEnergyProperty;   // Building_TMGolemBase.Energy -> CompGolemEnergyHandler
        private readonly PropertyInfo storedEnergyPctProperty;  // CompGolemEnergyHandler.StoredEnergyPct

        private RwomGolemNeedsColumnHandler(NeedDef golemEnergyNeedDef, Type buildingGolemBaseType,
            PropertyInfo buildingEnergyProperty, PropertyInfo storedEnergyPctProperty)
        {
            this.golemEnergyNeedDef = golemEnergyNeedDef;
            this.buildingGolemBaseType = buildingGolemBaseType;
            this.buildingEnergyProperty = buildingEnergyProperty;
            this.storedEnergyPctProperty = storedEnergyPctProperty;
        }

        public static void TryRegister()
        {
            try
            {
                var surface = new ReflectionSurface("RwomGolemNeedsColumnHandler");
                Type workerType = surface.Type("TorannMagic.Golems.PawnColumnWorker_GolemNeeds");
                Type buildingType = surface.Type("TorannMagic.Golems.Building_TMGolemBase");
                PropertyInfo energyProp = surface.Property(buildingType, "Energy");
                PropertyInfo storedPctProp = surface.Property(energyProp?.PropertyType, "StoredEnergyPct");
                if (!surface.Ready)
                {
                    return;
                }

                NeedDef needDef = DefDatabase<NeedDef>.GetNamedSilentFail("TM_GolemEnergy");
                if (needDef == null)
                {
                    ModLogger.Error("RwomGolemNeedsColumnHandler: the TM_GolemEnergy need def is missing; declining the golem energy column.");
                    return;
                }

                PawnColumnHandlerRegistry.Register(workerType,
                    new RwomGolemNeedsColumnHandler(needDef, buildingType, energyProp, storedPctProp));
            }
            catch (Exception ex)
            {
                ModLogger.Error($"RwomGolemCompat golem needs column registration failed: {ex.Message}");
            }
        }

        public override string CellText(PawnColumnDef def, Pawn pawn)
        {
            try
            {
                float pct;
                if (pawn.Spawned)
                {
                    Need need = pawn.needs?.TryGetNeed(golemEnergyNeedDef);
                    if (need == null)
                    {
                        return "";
                    }
                    pct = need.CurLevelPercentage;
                }
                else
                {
                    object holder = pawn.ParentHolder;
                    if (!buildingGolemBaseType.IsInstanceOfType(holder))
                    {
                        return "";
                    }
                    object energy = buildingEnergyProperty.GetValue(holder);
                    if (energy == null)
                    {
                        return "";
                    }
                    pct = (float)storedEnergyPctProperty.GetValue(energy);
                }
                return "RimWorldAccess.Compat.Rwom.GolemEnergyCell".Translate(Mathf.RoundToInt(pct * 100f)).ToString();
            }
            catch (Exception ex)
            {
                ModLogger.Error($"RwomGolemNeedsColumnHandler.CellText failed: {ex.Message}");
                return "RimWorldAccess.Shell.Generic.CellNotReadable".Loc().ToString();
            }
        }
    }
}
