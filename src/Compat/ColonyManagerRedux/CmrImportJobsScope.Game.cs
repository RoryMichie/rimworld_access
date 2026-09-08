using System.Collections.Generic;
using RimWorld;
using Verse;
using static RimWorldAccess.Shell.CompatText;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Keyboard support for Colony Manager Redux's job-import window
    /// (<c>ColonyManagerRedux.Managers.Dialog_ImportJobs</c>), which the Import/Export tab opens
    /// after loading a save or template file. One content region: the window's instruction line,
    /// then one checkbox row per loaded job (label, sub-label, update interval -- the same three
    /// zones its sighted row draws). The window's own Close and Import text buttons arrive
    /// through the automatic Buttons region.
    ///
    /// The window imports on ANY Enter through its own <c>OnAcceptKeyPressed</c>
    /// (Dialog_ImportJobs.cs:179-187); <see cref="ScreenScope.OwnsAccept"/> already masks that,
    /// so Enter toggles the focused row and importing stays on the Import button.
    /// </summary>
    internal sealed class CmrImportJobsScope : ScreenScope
    {
        private readonly Window window;
        private readonly List<object> jobs = new List<object>();

        public CmrImportJobsScope(Window window)
        {
            this.window = window;
            Claim("cmrImportJobs.paintDown", e => PaintRow(1), when: () => CurrentRowPaintable());
            Claim("cmrImportJobs.paintUp", e => PaintRow(-1), when: () => CurrentRowPaintable());
        }

        public override string Name
        {
            get { return "cmr-import-jobs"; }
        }

        protected internal override Window OwnedWindow
        {
            get { return window; }
        }

        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        protected override int ContentRegionCount
        {
            get { return 1; }
        }

        protected override string ContentRegionName(int region)
        {
            return "RimWorldAccess.Cmr.JobsRegion".Translate();
        }

        protected override void RefreshContent()
        {
            jobs.Clear();
            jobs.AddRange(CmrCompat.ImportExport.DialogJobs(window));
        }

        /// <summary>Row 0 is the window's own instruction line; the jobs follow it.</summary>
        protected override int ContentItemCount(int region)
        {
            return jobs.Count == 0 ? 0 : jobs.Count + 1;
        }

        private object JobAt(int index)
        {
            int jobIndex = index - 1;
            return jobIndex >= 0 && jobIndex < jobs.Count ? jobs[jobIndex] : null;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            var d = new ElementDescription();
            if (index == 0)
            {
                d.Label = Flatten(ModText("ColonyManagerRedux.SelectImportJobs"));
                d.Role = ElementRole.None;
                return d;
            }
            object job = JobAt(index);
            if (job == null)
            {
                return d;
            }
            if (!CmrCompat.ImportExport.JobIsValid(job))
            {
                // The window's own greyed row for a job that failed to load; its checkbox is
                // forced off and stays off (Dialog_ImportJobs.cs:49-53, 91-126).
                d.Label = Flatten(ModArgs("ColonyManagerRedux.InvalidJob", CmrCompat.JobLabel(job)));
                d.Role = ElementRole.None;
                d.Disabled = true;
                return d;
            }
            d.Label = CmrCompat.JobLabel(job);
            d.Role = ElementRole.None;
            d.Value = CmrCompat.TabGetSubLabel(CmrCompat.JobTab(job), job);
            d.Check = CheckOf(index);
            d.Extras = "RimWorldAccess.Cmr.ChecksInterval"
                .Translate(CmrCompat.JobUpdateIntervalLabel(job)).ToString();
            return d;
        }

        private CheckState CheckOf(int index)
        {
            MultiCheckboxState state = CmrCompat.ImportExport.DialogJobState(window, index - 1);
            if (state == MultiCheckboxState.On)
            {
                return CheckState.Checked;
            }
            return state == MultiCheckboxState.Partial
                ? CheckState.PartiallyChecked
                : CheckState.Unchecked;
        }

        /// <summary>
        /// The checkbox's own click cycle (decompiled Verse/Widgets.cs:1347: anything but Off
        /// clicks to Off, Off clicks to On), echoed with the new state alone.
        /// </summary>
        protected override void ActivateContentItem(int region, int index)
        {
            object job = JobAt(index);
            if (job == null || !CmrCompat.ImportExport.JobIsValid(job))
            {
                AnnounceCurrentItem();
                return;
            }
            MultiCheckboxState state = CmrCompat.ImportExport.DialogJobState(window, index - 1);
            CmrCompat.ImportExport.SetDialogJobState(window, index - 1,
                state != MultiCheckboxState.Off ? MultiCheckboxState.Off : MultiCheckboxState.On);
            RefreshModel();
            TolkHelper.SpeakData(AnnouncementComposer.ComposeStateChange(
                DescribeContentItem(region, index), TranslatedShellVocabulary.Instance));
        }

        // ------------------------------------------------------------------
        // Paint (Shift+Down/Up): the keyboard mirror of the window's drag-paintable
        // selection checkboxes.
        // ------------------------------------------------------------------

        private bool RowPaintable(int index)
        {
            object job = JobAt(index);
            return job != null && CmrCompat.ImportExport.JobIsValid(job);
        }

        private bool CurrentRowPaintable()
        {
            RefreshModel();
            ListModel region = Model.CurrentRegion;
            return region != null && RowPaintable(region.Index);
        }

        /// <summary>
        /// Shift+Down/Up: the keyboard mirror of the window's drag-paintable selection checkboxes.
        /// An invalid-job neighbor (its checkbox is forced off) or the list edge stops the paint
        /// in place, matching the drag passing over nothing actionable. The cursor moved, so the
        /// echo is the FULL row (identity, painted state, position), not the stationary-toggle
        /// state-only echo.
        /// </summary>
        private void PaintRow(int direction)
        {
            RefreshModel();
            ListModel region = Model.CurrentRegion;
            if (region == null || !RowPaintable(region.Index))
            {
                return;
            }
            int index = region.Index;
            int targetIndex = index + direction;
            if (!RowPaintable(targetIndex))
            {
                AnnounceCurrentItem();
                return;
            }
            bool brush = CmrCompat.ImportExport.DialogJobState(window, index - 1) == MultiCheckboxState.On;
            region.MoveTo(targetIndex);
            NotifyCursorSettled();
            CmrCompat.ImportExport.SetDialogJobState(window, targetIndex - 1,
                brush ? MultiCheckboxState.On : MultiCheckboxState.Off);
            RefreshModel();
            AnnounceCurrentItem();
        }

    }
}
