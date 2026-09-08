using System;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using static RimWorldAccess.Shell.CompatText;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Teaches the generic pawn-table tier the five bespoke pawn-column workers Colony
    /// Manager Redux's Livestock tab nests inside
    /// <c>ColonyManagerRedux.Managers.ManagerTab_Livestock</c> (source:
    /// ManagerTab_Livestock_AnimalsTable.cs), following the same by-name,
    /// decline-on-missing shape as <see cref="HospitalityAreaColumnHandler"/>.
    ///
    /// CM_LifeStage's cells need no entry here: its worker subclasses
    /// <c>RimWorld.PawnColumnWorker_LifeStage</c>, itself a
    /// <see cref="PawnColumnWorker_Icon"/>, so <see cref="PawnColumnHandlerRegistry"/>'s
    /// base-type walk already resolves the cells to the vanilla
    /// <see cref="IconColumnHandler"/> — MethodInfo.Invoke on the reflected,
    /// base-declared GetIconTip performs virtual dispatch, reaching the mod's own
    /// override for free. Its HEADER does need one: the def ships neither label nor
    /// headerTip (the header is three drawn icons), so without an opinion the column
    /// would speak its raw defName; the thin wrapper below forwards every cell read to
    /// the icon handler and answers only the header, with the first line of the worker's
    /// own header tooltip (the mod's AgeHeader key).
    ///
    /// Four of the five workers here (ExpectedMeatYield, MilkProgress, ShearProgress,
    /// Tame) read only vanilla state — RaceProps.meatDef / GetStatValue, CompMilkable's
    /// and CompShearable's own public Props, the Tame designation — plus the mod's own
    /// translation keys, so they need no reflection into the mod assembly beyond
    /// resolving their own worker Type to register against. Only Cull reads the job's
    /// own CullingStrategyAction; its members are bound once in <see cref="InstallAll"/>
    /// and read with a per-call try/catch degrade, since the worker's own jobGetter
    /// injection (set by ManagerTab_Livestock.CreateAnimalsTable's PawnsListForReading
    /// hijack) could in principle still be unset the first time this cell renders.
    /// </summary>
    internal static class CmrLivestockColumnHandlers
    {
        private const string TabTypeName = "ColonyManagerRedux.Managers.ManagerTab_Livestock";
        private const string JobTypeName = "ColonyManagerRedux.Managers.ManagerJob_Livestock";

        public static void InstallAll()
        {
            RegisterSimple(TabTypeName + "+PawnColumnWorker_ExpectedMeatYield", new ExpectedMeatYieldColumnHandler());
            RegisterSimple(TabTypeName + "+PawnColumnWorker_MilkProgress", new MilkProgressColumnHandler());
            RegisterSimple(TabTypeName + "+PawnColumnWorker_ShearProgress", new ShearProgressColumnHandler());
            RegisterSimple(TabTypeName + "+PawnColumnWorker_Tame", new TameColumnHandler());
            RegisterSimple(TabTypeName + "+PawnColumnWorker_LifeStage", new LifeStageColumnHandler());
            RegisterCull();
        }

        private static void RegisterSimple(string workerTypeName, IPawnColumnHandler handler)
        {
            Type workerType = AccessTools.TypeByName(workerTypeName);
            if (workerType == null)
            {
                Log.Warning("[RimWorld Access] CMR Livestock column handler: worker type '" + workerTypeName
                    + "' not found; that column stays read-only.");
                return;
            }
            PawnColumnHandlerRegistry.Register(workerType, handler);
        }

        private static void RegisterCull()
        {
            try
            {
                Type cullType = AccessTools.TypeByName(TabTypeName + "+PawnColumnWorker_Cull");
                Type livestockWorkerType = AccessTools.TypeByName(TabTypeName + "+PawnColumnWorker_Livestock");
                Type jobType = AccessTools.TypeByName(JobTypeName);
                Type cullingActionType = AccessTools.TypeByName(JobTypeName + "+CullingAction");
                if (cullType == null || livestockWorkerType == null || jobType == null || cullingActionType == null)
                {
                    Log.Warning("[RimWorld Access] CMR Livestock column handler: Cull worker types not found; the Cull column stays read-only.");
                    return;
                }

                FieldInfo jobGetterField = AccessTools.Field(livestockWorkerType, "jobGetter");
                PropertyInfo cullingStrategyActionProp = AccessTools.Property(jobType, "CullingStrategyAction");
                MethodInfo isAlreadyCullingMethod = AccessTools.Method(cullingActionType, "IsAlreadyCulling", new[] { typeof(Pawn) });
                MethodInfo isAlreadyCulledMethod = AccessTools.Method(cullingActionType, "IsAlreadyCulled", new[] { typeof(Pawn) });
                PropertyInfo translationKeyProp = AccessTools.Property(cullingActionType, "TranslationKey");
                if (jobGetterField == null || cullingStrategyActionProp == null || isAlreadyCullingMethod == null
                    || isAlreadyCulledMethod == null || translationKeyProp == null)
                {
                    Log.Warning("[RimWorld Access] CMR Livestock column handler: Cull worker members not found; the Cull column stays read-only.");
                    return;
                }

                // jobGetter's declared field type is Func<ManagerJob_Livestock?> — a mod-internal
                // generic argument this file never names at compile time, so its own delegate
                // Invoke is resolved the same way every other member here is.
                MethodInfo jobGetterInvoke = typeof(Func<>).MakeGenericType(jobType).GetMethod("Invoke");

                PawnColumnHandlerRegistry.Register(cullType, new CullColumnHandler(
                    jobGetterField, jobGetterInvoke, cullingStrategyActionProp,
                    isAlreadyCullingMethod, isAlreadyCulledMethod, translationKeyProp));
                Log.Message("[RimWorld Access] CMR compat: Livestock Cull column handler registered.");
            }
            catch (Exception ex)
            {
                Log.Error("[RimWorld Access] CMR Livestock Cull column handler registration failed: " + ex.Message);
            }
        }

        /// <summary>
        /// The cell's icon means the pawn's current life stage (baby, juvenile, adult); its
        /// tooltip is only the birth-date prose, so the stage itself must come from the age
        /// tracker. The header is answered too, since the def ships neither label nor headerTip
        /// and the raw defName would speak otherwise.
        /// </summary>
        private sealed class LifeStageColumnHandler : PawnColumnHandler
        {
            private readonly IconColumnHandler icons = new IconColumnHandler();

            public override string HeaderLabel(PawnColumnDef def)
            {
                return ModText("ColonyManagerRedux.Livestock.AgeHeader");
            }

            public override string CellText(PawnColumnDef def, Pawn pawn)
            {
                LifeStageDef stage = pawn.ageTracker != null ? pawn.ageTracker.CurLifeStage : null;
                return stage != null ? stage.LabelCap.ToString() : icons.CellText(def, pawn);
            }

            public override string CellTip(PawnColumnDef def, Pawn pawn)
            {
                return icons.CellTip(def, pawn);
            }
        }

        /// <summary>Mirrors PawnColumnWorker_ExpectedMeatYield.DoCell: the pawn's own estimated meat yield, tipped with the meat type and amount.</summary>
        private sealed class ExpectedMeatYieldColumnHandler : PawnColumnHandler
        {
            public override string HeaderLabel(PawnColumnDef def)
            {
                return ModText("ColonyManagerRedux.Livestock.MeatHeader");
            }

            public override string CellText(PawnColumnDef def, Pawn pawn)
            {
                return EstimatedMeatCount(pawn).ToString(CultureInfo.InvariantCulture);
            }

            public override string CellTip(PawnColumnDef def, Pawn pawn)
            {
                ThingDef meatDef = pawn.RaceProps?.meatDef;
                return meatDef != null
                    ? ModArgs("ColonyManagerRedux.Livestock.Yields", meatDef.LabelCap, EstimatedMeatCount(pawn))
                    : null;
            }

            // The mod's own Utilities_Hunting.EstimatedMeatCount(Pawn) is this exact vanilla
            // stat lookup with nothing mod-specific to diverge on, so it is read directly
            // rather than reflected.
            private static int EstimatedMeatCount(Pawn pawn)
            {
                return (int)pawn.GetStatValue(StatDefOf.MeatAmount);
            }
        }

        /// <summary>Mirrors PawnColumnWorker_MilkProgress.DoCell: CompMilkable's own fullness, tipped with the milk type and amount.</summary>
        private sealed class MilkProgressColumnHandler : PawnColumnHandler
        {
            public override string HeaderLabel(PawnColumnDef def)
            {
                return ModText("ColonyManagerRedux.Livestock.MilkHeader");
            }

            public override string CellText(PawnColumnDef def, Pawn pawn)
            {
                CompMilkable comp = pawn.TryGetComp<CompMilkable>();
                return comp != null ? comp.Fullness.ToString("0%", CultureInfo.InvariantCulture) : "";
            }

            public override string CellTip(PawnColumnDef def, Pawn pawn)
            {
                CompMilkable comp = pawn.TryGetComp<CompMilkable>();
                // A modded comp with no product def would cost the whole fused row tail, where the
                // mod only loses an icon.
                return comp != null && comp.Props.milkDef != null
                    ? ModArgs("ColonyManagerRedux.Livestock.Yields", comp.Props.milkDef.LabelCap, comp.Props.milkAmount)
                    : null;
            }
        }

        /// <summary>Mirrors PawnColumnWorker_ShearProgress.DoCell: CompShearable's own fullness, tipped with the wool type and amount.</summary>
        private sealed class ShearProgressColumnHandler : PawnColumnHandler
        {
            public override string HeaderLabel(PawnColumnDef def)
            {
                return ModText("ColonyManagerRedux.Livestock.WoolHeader");
            }

            public override string CellText(PawnColumnDef def, Pawn pawn)
            {
                CompShearable comp = pawn.TryGetComp<CompShearable>();
                return comp != null ? comp.Fullness.ToString("0%", CultureInfo.InvariantCulture) : "";
            }

            public override string CellTip(PawnColumnDef def, Pawn pawn)
            {
                CompShearable comp = pawn.TryGetComp<CompShearable>();
                return comp != null && comp.Props.woolDef != null
                    ? ModArgs("ColonyManagerRedux.Livestock.Yields", comp.Props.woolDef.LabelCap, comp.Props.woolAmount)
                    : null;
            }
        }

        /// <summary>Mirrors PawnColumnWorker_Tame.DoCell: whether the wild animal carries the vanilla Tame designation.</summary>
        private sealed class TameColumnHandler : PawnColumnHandler
        {
            public override string HeaderLabel(PawnColumnDef def)
            {
                return ModText("ColonyManagerRedux.Livestock.TamingHeader");
            }

            public override string CellText(PawnColumnDef def, Pawn pawn)
            {
                if (pawn.Map?.designationManager.DesignationOn(pawn, DesignationDefOf.Tame) == null)
                {
                    return "";
                }
                string tamingWord = ModText("ColonyManagerRedux.Livestock.TamingHeader").UncapitalizeFirst();
                return ModArgs("ColonyManagerRedux.Livestock.AnimalIsDesignatedFor", tamingWord);
            }
        }

        /// <summary>
        /// Mirrors PawnColumnWorker_Cull.DoCell: whether the tame animal is already being
        /// culled, or already culled and no longer counting towards the target, by the job's
        /// current CullingStrategyAction — read through the jobGetter injection the worker
        /// itself carries (same delegate ManagerTab_Livestock hands every
        /// PawnColumnWorker_Livestock via CreateAnimalsTable's PawnsListForReading hijack).
        /// </summary>
        private sealed class CullColumnHandler : PawnColumnHandler
        {
            private readonly FieldInfo jobGetterField;
            private readonly MethodInfo jobGetterInvoke;
            private readonly PropertyInfo cullingStrategyActionProp;
            private readonly MethodInfo isAlreadyCullingMethod;
            private readonly MethodInfo isAlreadyCulledMethod;
            private readonly PropertyInfo translationKeyProp;

            internal CullColumnHandler(FieldInfo jobGetterField, MethodInfo jobGetterInvoke,
                PropertyInfo cullingStrategyActionProp, MethodInfo isAlreadyCullingMethod,
                MethodInfo isAlreadyCulledMethod, PropertyInfo translationKeyProp)
            {
                this.jobGetterField = jobGetterField;
                this.jobGetterInvoke = jobGetterInvoke;
                this.cullingStrategyActionProp = cullingStrategyActionProp;
                this.isAlreadyCullingMethod = isAlreadyCullingMethod;
                this.isAlreadyCulledMethod = isAlreadyCulledMethod;
                this.translationKeyProp = translationKeyProp;
            }

            public override string HeaderLabel(PawnColumnDef def)
            {
                return ModText("ColonyManagerRedux.Livestock.CullingHeader");
            }

            public override string CellText(PawnColumnDef def, Pawn pawn)
            {
                try
                {
                    object jobGetter = jobGetterField.GetValue(def.Worker);
                    object job = jobGetter != null ? jobGetterInvoke.Invoke(jobGetter, null) : null;
                    if (job == null)
                    {
                        return "";
                    }
                    object cullingAction = cullingStrategyActionProp.GetValue(job);
                    bool isAlreadyCulling = (bool)isAlreadyCullingMethod.Invoke(cullingAction, new object[] { pawn });
                    bool isAlreadyCulled = (bool)isAlreadyCulledMethod.Invoke(cullingAction, new object[] { pawn });
                    if (!isAlreadyCulling && !isAlreadyCulled)
                    {
                        return "";
                    }
                    string translationKey = (string)translationKeyProp.GetValue(cullingAction);
                    return isAlreadyCulling
                        ? ModArgs("ColonyManagerRedux.Livestock.AnimalIsCulling",
                            ModText("ColonyManagerRedux.Livestock.Logs." + translationKey + ".Action"))
                        : ModArgs("ColonyManagerRedux.Livestock.AnimalIsCulled",
                            ModText("ColonyManagerRedux.Livestock.Logs." + translationKey + ".Culled"));
                }
                catch (Exception)
                {
                    // A transient unset jobGetter (before the table's own PawnsListForReading
                    // hijack has run once) is a normal state, not a bug worth logging every row.
                    return "";
                }
            }
        }
    }
}
