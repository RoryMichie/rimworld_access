using System;
using HarmonyLib;
using RimWorld;
using Verse;
using static RimWorldAccess.Shell.CompatText;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Teaches the generic pawn-table tier the two bespoke pawn-column workers Colony Manager
    /// Redux's Overview tab nests inside <c>ColonyManagerRedux.Managers.ManagerTab_Overview</c>
    /// (source: ManagerTab_Overview_PawnOverviewTable.cs), following the same by-name,
    /// decline-on-missing shape as <see cref="CmrLivestockColumnHandlers"/>.
    ///
    /// The mod's third worker there, <c>PawnColumnWorker_Label</c>, subclasses
    /// <c>RimWorld.PawnColumnWorker_Label</c> and needs no entry here: the generic pawn-table
    /// tier's base-type walk already resolves its cells to the vanilla label handler.
    /// </summary>
    internal static class CmrOverviewColumnHandlers
    {
        private const string TabTypeName = "ColonyManagerRedux.Managers.ManagerTab_Overview";

        public static void InstallAll()
        {
            RegisterSimple(TabTypeName + "+PawnColumnWorker_CurrentActivity", new CurrentActivityColumnHandler());
            RegisterSimple(TabTypeName + "+PawnColumnWorker_WorkPriorities", new WorkPrioritiesColumnHandler());
        }

        private static void RegisterSimple(string workerTypeName, IPawnColumnHandler handler)
        {
            Type workerType = AccessTools.TypeByName(workerTypeName);
            if (workerType == null)
            {
                Log.Warning("[RimWorld Access] CMR Overview column handler: worker type '" + workerTypeName
                    + "' not found; that column stays read-only.");
                return;
            }
            PawnColumnHandlerRegistry.Register(workerType, handler);
        }

        /// <summary>Mirrors PawnColumnWorker_CurrentActivity.GetPawnActivityString: the pawn's current job report, or the mod's own placeholder. The def itself carries a label and headerTip, so the header goes unopinioned.</summary>
        private sealed class CurrentActivityColumnHandler : PawnColumnHandler
        {
            public override string CellText(PawnColumnDef def, Pawn pawn)
            {
                string report = pawn.jobs != null && pawn.jobs.curDriver != null
                    ? pawn.jobs.curDriver.GetReport()
                    : null;
                return !string.IsNullOrEmpty(report) ? report : ModText("ColonyManagerRedux.Overview.NoCurrentJob");
            }
        }

        /// <summary>
        /// Mirrors PawnColumnWorker_WorkPriorities: the header word tracks
        /// <c>Find.PlaySettings.useWorkPriorities</c> exactly as the mod's own GetLabel does; the cell
        /// itself routes through the lead's shared <see cref="WorkPriorityColumnHandler"/> statics
        /// with this column's own work type, resolved via <see cref="CmrCompat.Overview.WorkerWorkType"/>.
        /// A not-yet-injected work type (the table's pawns-getter hijack has not run once yet) answers
        /// quietly rather than logging -- a normal transient, not a bug.
        /// </summary>
        private sealed class WorkPrioritiesColumnHandler : PawnColumnHandler
        {
            public override string HeaderLabel(PawnColumnDef def)
            {
                return Find.PlaySettings != null && Find.PlaySettings.useWorkPriorities
                    ? Translator.Translate("ColonyManagerRedux.Overview.WorkPriority").Resolve()
                    : Translator.Translate("ColonyManagerRedux.Overview.WorkEnabled").Resolve();
            }

            public override string CellText(PawnColumnDef def, Pawn pawn)
            {
                WorkTypeDef workType = CmrCompat.Overview.WorkerWorkType(def.Worker);
                return workType == null ? "" : WorkPriorityColumnHandler.PriorityCellText(workType, pawn);
            }

            public override string CellTip(PawnColumnDef def, Pawn pawn)
            {
                WorkTypeDef workType = CmrCompat.Overview.WorkerWorkType(def.Worker);
                return workType == null ? null : WorkPriorityColumnHandler.PriorityCellTip(workType, pawn);
            }

            public override PawnColumnActivation ActivateCell(PawnColumnDef def, Pawn pawn, PawnTable table)
            {
                WorkTypeDef workType = CmrCompat.Overview.WorkerWorkType(def.Worker);
                return workType == null
                    ? PawnColumnActivation.NotHandled
                    : WorkPriorityColumnHandler.ActivatePriority(workType, pawn);
            }

            public override bool CanAdjustCell(PawnColumnDef def)
            {
                return CmrCompat.Overview.WorkerWorkType(def.Worker) != null;
            }

            public override PawnColumnActivation AdjustCell(PawnColumnDef def, Pawn pawn, int direction, PawnTable table)
            {
                WorkTypeDef workType = CmrCompat.Overview.WorkerWorkType(def.Worker);
                return workType == null
                    ? PawnColumnActivation.NotHandled
                    : WorkPriorityColumnHandler.AdjustPriority(workType, pawn, direction);
            }
        }
    }
}
