using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Adapter for a pawn's "Job Queue" inspection category. Shows the
    /// current job and all queued jobs, with delete capability on queued
    /// entries in Full inspection mode.
    /// </summary>
    internal sealed class PawnJobQueueAdapter : InspectNodeAdapter
    {
        public override string CategoryKey => "Job Queue";

        public override TabHandlerType Handler => TabHandlerType.RichNavigation;

        /// <summary>
        /// Decorates the collapsed Job Queue row with the queued-job count (verbatim from
        /// InspectionTreeBuilder.GetCategoryLabel).
        /// </summary>
        public override string CategoryLabel(object obj, string displayName)
        {
            if (obj is Pawn jobPawn && jobPawn.jobs?.jobQueue != null)
            {
                int queueCount = jobPawn.jobs.jobQueue.Count;
                return "RimWorldAccess.Inspection.CategoryName.JobQueueLabel".Translate(displayName, queueCount);
            }
            return displayName;
        }

        public override void BuildChildren(InspectionTreeItem categoryItem, object obj, InspectionMode mode)
        {
            Pawn pawn = InspectionTreeBuilder.GetPawnFromThing(obj);
            if (pawn == null)
                return;
            BuildJobQueueChildren(categoryItem, pawn, mode);
        }

        /// <summary>
        /// Builds children for Job Queue category.
        /// Shows current job and all queued jobs with delete capability.
        /// </summary>
        private static void BuildJobQueueChildren(InspectionTreeItem parentItem, Pawn pawn, InspectionMode mode)
        {
            if (pawn.jobs == null)
                return;

            var jobTracker = pawn.jobs;
            int indent = parentItem.IndentLevel + 1;

            // Add current job (not deletable)
            if (jobTracker.curJob != null)
            {
                string unknownJob = "RimWorldAccess.Inspection.Tree.JobUnknown".Translate();
                string currentJobReport;
                try
                {
                    currentJobReport = jobTracker.curJob.GetReport(pawn)?.CapitalizeFirst() ?? unknownJob;
                }
                catch
                {
                    currentJobReport = jobTracker.curJob.def?.label?.CapitalizeFirst() ?? unknownJob;
                }

                var currentItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Item,
                    Label = "RimWorldAccess.Inspection.Tree.JobCurrent".Translate(currentJobReport),
                    Data = jobTracker.curJob,
                    IndentLevel = indent,
                    IsExpandable = false
                };
                parentItem.Children.Add(currentItem);
            }
            else
            {
                var idleItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Item,
                    Label = "RimWorldAccess.Inspection.Tree.JobCurrentIdle".Translate(),
                    IndentLevel = indent,
                    IsExpandable = false
                };
                parentItem.Children.Add(idleItem);
            }

            // Add queued jobs (deletable in Full mode only)
            var jobQueue = jobTracker.jobQueue;
            if (jobQueue != null && jobQueue.Count > 0)
            {
                int queueIndex = 1;
                foreach (var queuedJob in jobQueue)
                {
                    if (queuedJob?.job == null)
                        continue;

                    string jobReport;
                    string unknownJobLabel = "RimWorldAccess.Inspection.Tree.JobUnknown".Translate();
                    try
                    {
                        jobReport = queuedJob.job.GetReport(pawn)?.CapitalizeFirst() ?? unknownJobLabel;
                    }
                    catch
                    {
                        jobReport = queuedJob.job.def?.label?.CapitalizeFirst() ?? unknownJobLabel;
                    }

                    var queuedItem = new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.Item,
                        Label = "RimWorldAccess.Inspection.Tree.JobQueued".Translate(queueIndex, jobReport),
                        Data = queuedJob,
                        IndentLevel = indent,
                        IsExpandable = false
                    };

                    // Only add delete action in Full mode
                    if (mode == InspectionMode.Full)
                    {
                        // Capture the job for the closure
                        var jobToCancel = queuedJob.job;
                        var jobLabel = jobReport;
                        queuedItem.OnDelete = () =>
                        {
                            // Cancel the queued job
                            jobQueue.Extract(jobToCancel);
                            TolkHelper.Speak("RimWorldAccess.Inspection.Tree.JobCancelled".Loc(jobLabel), SpeechPriority.High);

                            InspectionTreeBuilder.RebuildBranchInPlace(parentItem, () =>
                            {
                                parentItem.Children.Clear();
                                BuildJobQueueChildren(parentItem, pawn, mode);
                            });
                        };
                    }

                    parentItem.Children.Add(queuedItem);
                    queueIndex++;
                }
            }
        }
    }
}
