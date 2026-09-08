using System.Collections.Generic;
using Verse;
using static RimWorldAccess.Shell.CompatText;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The Logs tab's detail rows: one region, one row per kept log entry, each expandable into its
    /// own go-to row and detail lines. Mirrors <c>ManagerTab_Logs.DoMainContent</c>'s filter and
    /// order exactly: newest first, filtered by the settings' show-no-work-done flag and, when a job
    /// is selected, to that job's own logs (the scope's Enter-toggles-filter on a Jobs row, not this
    /// provider, drives that selection).
    ///
    /// The mod draws two independent click zones per log row: the icon (always live, jumps straight
    /// to the job's tab) and the label/date area (toggles expansion). One row carries one Enter
    /// behavior, so the log row's own Activate is the expand/collapse toggle -- the bigger target and
    /// the row's primary identity -- and the icon's jump becomes a child row that appears only while
    /// expanded, alongside the log's own detail lines.
    /// </summary>
    internal sealed class CmrLogsDetails : ICmrJobDetailsProvider
    {
        public bool Handles(object tab)
        {
            return CmrCompat.Logs.HandlesTab(tab);
        }

        public List<CmrDetailRegion> Build(object tab, object job)
        {
            var regions = new List<CmrDetailRegion>();
            if (tab == null || !CmrCompat.Logs.Ready)
            {
                return regions;
            }
            var region = new CmrDetailRegion("RimWorldAccess.Cmr.Logs.LogsRegion".Translate());
            regions.Add(region);

            List<object> kept = KeptLogs(tab, job);
            if (kept.Count == 0)
            {
                region.Rows.Add(new CmrDetailRow
                {
                    Label = ModText("ColonyManagerRedux.Logs.NoLogs"),
                    Role = ElementRole.None,
                });
                return regions;
            }
            for (int i = 0; i < kept.Count; i++)
            {
                AddLogRows(region, tab, kept[i]);
            }
            return regions;
        }

        /// <summary>Newest first (the facade yields oldest-first, so this reverses), keeping a log per the settings' no-work-done flag and, when a job is selected, to that job alone (ManagerTab_Logs.cs:49-58).</summary>
        private static List<object> KeptLogs(object tab, object job)
        {
            var kept = new List<object>();
            object manager = CmrCompat.ManagerFor(Find.CurrentMap);
            List<object> logs = CmrCompat.Logs.LogsFor(manager);
            bool showNoWorkDone = CmrCompat.Logs.ShowLogsWithNoWorkDone(tab);
            for (int i = logs.Count - 1; i >= 0; i--)
            {
                object log = logs[i];
                if (!showNoWorkDone && !CmrCompat.Logs.LogWorkDone(log))
                {
                    continue;
                }
                if (job != null && !CmrCompat.Logs.LogIsForJob(log, job))
                {
                    continue;
                }
                kept.Add(log);
            }
            return kept;
        }

        private static void AddLogRows(CmrDetailRegion region, object tab, object log)
        {
            region.Rows.Add(new CmrDetailRow
            {
                Label = Flatten(CmrCompat.Logs.LogLabelCap(log)),
                Value = () => CmrCompat.Logs.LogDate(log),
                Expanded = () => IsExpanded(tab, log),
                Tooltip = () => LogTooltip(tab, log),
                Role = ElementRole.None,
                Activate = () => CmrCompat.Logs.SetSelectedLog(tab, IsExpanded(tab, log) ? null : log),
            });
            if (!IsExpanded(tab, log))
            {
                return;
            }
            AddGoToRow(region, log);
            List<object> details = CmrCompat.Logs.LogDetails(log);
            for (int i = 0; i < details.Count; i++)
            {
                region.Rows.Add(DetailRow(details[i]));
            }
        }

        private static bool IsExpanded(object tab, object log)
        {
            return ReferenceEquals(CmrCompat.Logs.SelectedLog(tab), log);
        }

        /// <summary>The mod's own expand hint (ClickToExpandCollapse, both label and date zones carry it, ManagerTab_Logs.cs:254-275), plus the icon's dead-job note (:243-250) folded in since a job-less log gets no go-to row to carry it on.</summary>
        private static string LogTooltip(object tab, object log)
        {
            bool expanded = IsExpanded(tab, log);
            var parts = new List<string>
            {
                ModArgs("ColonyManagerRedux.Logs.ClickToExpandCollapse",
                    ModText(expanded ? "ColonyManagerRedux.Logs.Collapse" : "ColonyManagerRedux.Logs.Expand")),
            };
            if (!CmrCompat.Logs.LogHasJob(log))
            {
                parts.Add(ModText("ColonyManagerRedux.Logs.JobDoesNotExist"));
            }
            return CompatText.JoinSentences(parts);
        }

        /// <summary>The icon's own jump, present only for a log whose job still exists (ManagerTab_Logs.cs:215-241).</summary>
        private static void AddGoToRow(CmrDetailRegion region, object log)
        {
            if (!CmrCompat.Logs.LogHasJob(log))
            {
                return;
            }
            object jobTab = CmrCompat.Logs.LogTab(log);
            if (jobTab == null)
            {
                return;
            }
            string label = ModArgs("ColonyManagerRedux.Common.GoToJob", CmrCompat.Logs.LogJobLabel(log));
            if (CmrCompat.TabEnabled(jobTab))
            {
                region.Rows.Add(new CmrDetailRow
                {
                    Label = label,
                    Role = ElementRole.Button,
                    Activate = () => CmrCompat.Logs.GoToJobTab(log),
                    Confirmation = "RimWorldAccess.Cmr.TabSwitched".Translate(CmrCompat.TabLabel(jobTab)),
                });
                return;
            }
            region.Rows.Add(new CmrDetailRow
            {
                Label = label,
                Tooltip = () => CmrCompat.TabLabel(jobTab)
                    + Flatten(ModArgs("ColonyManagerRedux.Common.TabDisabledBecause",
                        CmrCompat.TabDisabledReason(jobTab))),
                Role = ElementRole.None,
            });
        }

        private static CmrDetailRow DetailRow(object details)
        {
            string label = Flatten(CmrCompat.Logs.DetailText(details));
            if (CmrCompat.Logs.DetailTargetCount(details) <= 0)
            {
                return new CmrDetailRow
                {
                    Label = label,
                    Role = ElementRole.None,
                };
            }
            return new CmrDetailRow
            {
                Label = label,
                Role = ElementRole.Button,
                OpensWindow = true,
                Activate = () => ActivateDetail(details),
            };
        }

        private static void ActivateDetail(object details)
        {
            LocalTargetInfo target;
            if (CmrCompat.Logs.TryGetNextTarget(details, out target))
            {
                // MUTATION-C: mirrors ManagerTab_Logs.cs:131-140 (detail row click: jump-and-select
                // the cycling next target, then close the main tab); inline click body with no
                // callable vanilla method.
                CameraJumper.TryJumpAndSelect(target.ToGlobalTargetInfo(Find.CurrentMap));
                Find.MainTabsRoot.EscapeCurrentTab();
            }
        }

    }
}
