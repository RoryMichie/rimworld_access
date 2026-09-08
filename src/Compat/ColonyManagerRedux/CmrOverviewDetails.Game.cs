using System.Collections.Generic;
using RimWorld;
using Verse;
using static RimWorldAccess.Shell.CompatText;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The Overview tab's detail rows: a cross-tab job list plus a worker table, neither shaped like
    /// a per-job threshold pane. Regions, in the mod's own visual order
    /// (<c>ManagerTab_Overview.DoTabContents</c>): the selected job's own tab-jump (its
    /// <c>sideRectUpper</c>, present only with a job selected -- the mod's history-graph details
    /// panel there is excluded by ruling), then the worker table (<c>sideRectLower</c>, always
    /// present once the table itself exists).
    ///
    /// No region for the job list itself: the generic Jobs region every tab already carries covers
    /// it, and Overview draws no controls of its own beyond the row's shared tab-jump icon and stamp
    /// button.
    /// </summary>
    internal sealed class CmrOverviewDetails : ICmrJobDetailsProvider
    {
        public bool Handles(object tab)
        {
            return CmrCompat.Overview.HandlesTab(tab);
        }

        public List<CmrDetailRegion> Build(object tab, object job)
        {
            var regions = new List<CmrDetailRegion>();
            if (tab == null || !CmrCompat.Overview.Ready)
            {
                return regions;
            }
            if (job != null)
            {
                CmrDetailRegion selectedJob = BuildSelectedJob(job);
                if (selectedJob != null)
                {
                    regions.Add(selectedJob);
                }
            }
            CmrDetailRegion workers = BuildWorkers(tab);
            if (workers != null)
            {
                regions.Add(workers);
            }
            return regions;
        }

        // ------------------------------------------------------------------
        // Selected job: the tab-jump the sighted row's own icon performs.
        // ------------------------------------------------------------------

        private static CmrDetailRegion BuildSelectedJob(object job)
        {
            object target = CmrCompat.JobTab(job);
            if (target == null)
            {
                return null;
            }
            var region = new CmrDetailRegion(
                "RimWorldAccess.Cmr.Overview.SelectedJobRegion".Translate());

            string label = ModArgs("ColonyManagerRedux.Common.GoToJob",
                CmrCompat.JobLabel(job).UncapitalizeFirst());
            if (CmrCompat.TabEnabled(target))
            {
                region.Rows.Add(new CmrDetailRow
                {
                    Label = label,
                    Role = ElementRole.Button,
                    Activate = () => CmrCompat.GoTo(target, job),
                    Confirmation = "RimWorldAccess.Cmr.TabSwitched".Translate(CmrCompat.TabLabel(target)),
                });
            }
            else
            {
                region.Rows.Add(new CmrDetailRow
                {
                    Label = label,
                    Tooltip = () => CmrCompat.TabLabel(target)
                        + Flatten(ModArgs("ColonyManagerRedux.Common.TabDisabledBecause",
                            CmrCompat.TabDisabledReason(target))),
                    Role = ElementRole.None,
                });
            }
            return region;
        }

        // ------------------------------------------------------------------
        // Workers: the mod's pawn table, presented as a real table.
        // ------------------------------------------------------------------

        /// <summary>
        /// The worker table as a table region: the label column names each row and the remaining
        /// cells (activity, priority) are navigable columns, so Enter on the priority cell cycles
        /// it through the column's own handler exactly as a sighted click does. An empty table
        /// stays an empty flat region, which the shell's empty-region rule hides -- the same
        /// no-rows behavior this region always had.
        /// </summary>
        private static CmrDetailRegion BuildWorkers(object tab)
        {
            CmrDetailPawnTable table = CmrDetailPawnTable.Read(CmrCompat.Overview.Table(tab));
            if (table == null)
            {
                return null;
            }
            WorkTypeDef workType = CmrCompat.Overview.CurrentWorkType(tab);
            string name = workType != null
                ? workType.gerundLabel.CapitalizeFirst()
                : "RimWorldAccess.Cmr.Overview.WorkersRegion".Translate().ToString();
            var region = new CmrDetailRegion(name);
            if (table.Rows.Count > 0)
            {
                region.Table = table;
            }
            return region;
        }
    }
}
