using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The keyboard focus scope for Progression: Education's <c>Dialog_CreateClass</c> and
    /// <c>Dialog_EditClass</c>, registered by <see cref="PeModule"/>; all reflection lives in
    /// <see cref="PeCompat"/>. The dialogs' form controls (name field, subject/classroom/hour
    /// dropdowns, each subject's own configuration row) are ordinary captured widgets, so the
    /// captured-extras region presents and operates them with no bespoke work — including
    /// controls contributed by other Progression mods, like Therapy's focus and quirk pickers.
    ///
    /// What capture cannot see is the mod's role-assignment grid: a thousand-line copy of
    /// vanilla's <c>PawnRoleSelectionWidgetBase</c> driven by drag-and-drop and raw mouse
    /// events. Its data model implements vanilla interfaces, so this scope presents one content
    /// region per role (teacher, students) with a checkbox row per candidate pawn, toggling
    /// through the manager's own gated <c>TryAssign</c>/<c>TryUnassignAnyRole</c> — the same
    /// calls the widget's click handlers make. The dialogs' read-only lines (class speed,
    /// requirements, a subject's own informational text) ride the extras region's read-only
    /// rows, read straight from the render path.
    /// </summary>
    internal sealed class PeClassDialogScope : ScreenScope
    {
        private readonly Window dialog;
        private readonly List<ILordJobRole> roles = new List<ILordJobRole>();
        private readonly List<Pawn> candidates = new List<Pawn>();

        public PeClassDialogScope(Window w)
        {
            dialog = w;
        }

        public override string Name => "pe-class-dialog";

        protected internal override Window OwnedWindow => dialog;

        protected override bool EnableTypeahead => true;

        /// <summary>All the window's ButtonTexts are form controls or the Cancel/commit pair the
        /// extras region already presents in place; blanket Buttons-region capture would collect
        /// the subject/classroom/hour dropdowns as if they were window chrome.</summary>
        protected override bool CaptureWindowButtons => false;

        protected override bool IncludeCapturedExtrasRegion => true;

        protected override bool ExtrasIncludeReadOnlyRows => true;

        protected override void RefreshContent()
        {
            roles.Clear();
            candidates.Clear();
            ILordJobRole teacher = PeCompat.TeacherRoleOf(dialog);
            ILordJobRole student = PeCompat.StudentRoleOf(dialog);
            if (teacher != null)
            {
                roles.Add(teacher);
            }
            if (student != null)
            {
                roles.Add(student);
            }
            ILordJobCandidatePool pool = PeCompat.CandidatePoolOf(dialog);
            if (pool != null)
            {
                candidates.AddRange(pool.AllCandidatePawns);
            }
        }

        protected override int ContentRegionCount => roles.Count;

        protected override string ContentRegionName(int region)
        {
            ILordJobRole role = roles[region];
            object manager = PeCompat.AssignmentsManagerOf(dialog);
            int assigned = manager == null ? 0 : PeCompat.AssignedPawns(manager, role).Count;
            return "RimWorldAccess.Compat.Pe.RoleRegion".Translate(
                role.LabelCap, assigned, role.MaxCount);
        }

        protected override int ContentItemCount(int region)
        {
            return candidates.Count;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            var d = new ElementDescription();
            if (index < 0 || index >= candidates.Count)
            {
                return d;
            }
            Pawn pawn = candidates[index];
            ILordJobRole role = roles[region];
            object manager = PeCompat.AssignmentsManagerOf(dialog);
            if (manager == null)
            {
                return d;
            }

            d.Label = pawn.LabelShortCap;
            d.Role = ElementRole.Checkbox;
            ILordJobRole current = PeCompat.RoleForPawn(manager, pawn);
            bool inThisRole = current != null && ReferenceEquals(current, role);
            d.Check = inThisRole ? CheckState.Checked : CheckState.Unchecked;
            if (!inThisRole)
            {
                if (current != null)
                {
                    d.Extras = "RimWorldAccess.Compat.Pe.AssignedElsewhere".Translate(current.LabelCap);
                }
                else
                {
                    string reason = PeCompat.NotAssignableReason(manager, pawn, role);
                    if (!string.IsNullOrEmpty(reason))
                    {
                        d.Disabled = true;
                        d.Extras = CompatText.Flatten(reason);
                    }
                }
            }
            return d;
        }

        protected override void ActivateContentItem(int region, int index)
        {
            if (index < 0 || index >= candidates.Count)
            {
                return;
            }
            Pawn pawn = candidates[index];
            ILordJobRole role = roles[region];
            object manager = PeCompat.AssignmentsManagerOf(dialog);
            if (manager == null)
            {
                return;
            }

            ILordJobRole current = PeCompat.RoleForPawn(manager, pawn);
            if (current != null && ReferenceEquals(current, role))
            {
                if (PeCompat.TryUnassign(manager, pawn))
                {
                    SoundDefOf.Checkbox_TurnedOff.PlayOneShotOnCamera();
                    RefreshModel();
                    AnnounceCurrentItem();
                }
                return;
            }

            if (PeCompat.TryAssign(manager, pawn, role))
            {
                SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
                RefreshModel();
                AnnounceCurrentItem();
                return;
            }

            SoundDefOf.ClickReject.PlayOneShotOnCamera();
            string reason = PeCompat.NotAssignableReason(manager, pawn, role);
            if (string.IsNullOrEmpty(reason))
            {
                TolkHelper.Speak("RimWorldAccess.Compat.Pe.CannotAssign".Loc(
                    pawn.LabelShortCap, role.Label));
            }
            else
            {
                TolkHelper.SpeakData(CompatText.Flatten(reason));
            }
        }
    }
}
