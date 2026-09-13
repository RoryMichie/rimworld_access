using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The keyboard focus scope for Progression: Education's Education main tab, registered by
    /// <see cref="PeModule"/>; all reflection lives in <see cref="PeCompat"/>. The tab's rows
    /// are composites (portrait, icon buttons with hover tooltips, invisible click regions, a
    /// raw-event jump on the teacher portrait), so this scope reads the same data the tab draws
    /// and routes every action through the float menu Enter opens — each option opening the
    /// same dialog or calling the same method as the sighted control it mirrors.
    ///
    /// Two content regions: classes (empty-state text, else one row per study group) and
    /// classrooms (the tab's description, then one row each). Declared actions mirror the tab's
    /// two buttons: create class (behind its bell/classroom checks) and the scheduling shortcut.
    /// </summary>
    internal sealed class PeEducationTabScope : ScreenScope
    {
        private const int ClassesRegion = 0;
        private const int ClassroomsRegion = 1;

        private const int ClassroomsDescriptionRow = 0;
        private const int ClassroomRowOffset = 1;

        private readonly Window window;
        private readonly List<object> studyGroups = new List<object>();
        private readonly List<object> classrooms = new List<object>();

        public PeEducationTabScope(Window w)
        {
            window = w;
        }

        public override string Name => "pe-education-tab";

        protected internal override Window OwnedWindow => window;

        protected override bool EnableTypeahead => true;

        /// <summary>The tab's own buttons are mirrored as declared actions; its only ButtonText
        /// is the scheduling shortcut, already declared below.</summary>
        protected override bool CaptureWindowButtons => false;

        protected override IReadOnlyList<ScreenAction> DeclaredActions => new[]
        {
            new ScreenAction(CompatText.ModText("PE_CreateClass"), CreateClass),
            new ScreenAction(CompatText.ModText("PE_ClassScheduling"), OpenScheduling),
        };

        protected override void RefreshContent()
        {
            studyGroups.Clear();
            studyGroups.AddRange(PeCompat.StudyGroups());
            classrooms.Clear();
            classrooms.AddRange(PeCompat.Classrooms());
        }

        protected override int ContentRegionCount => 2;

        protected override string ContentRegionName(int region)
        {
            return CompatText.ModText(region == ClassesRegion ? "PE_Classes" : "PE_Classrooms");
        }

        protected override int ContentItemCount(int region)
        {
            if (region == ClassesRegion)
            {
                return studyGroups.Count == 0 ? 1 : studyGroups.Count;
            }
            return ClassroomRowOffset + classrooms.Count;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            var d = new ElementDescription();
            if (region == ClassesRegion)
            {
                if (studyGroups.Count == 0)
                {
                    d.Label = CompatText.ModText("PE_NoClassesScheduled");
                    d.ReadOnly = true;
                    return d;
                }
                if (index < 0 || index >= studyGroups.Count)
                {
                    return d;
                }
                object sg = studyGroups[index];
                object logic = PeCompat.SubjectLogicOf(sg);
                var parts = new List<string> { PeCompat.SubjectLabel(logic) };
                if (PeCompat.Suspended(sg))
                {
                    parts.Add(CompatText.ModText("PE_Suspended"));
                }
                Pawn teacher = PeCompat.TeacherOf(sg);
                if (teacher != null)
                {
                    parts.Add("RimWorldAccess.Compat.Pe.TeacherFragment".Translate(teacher.LabelShortCap));
                }
                string progress = ProgressText(sg, logic);
                if (!string.IsNullOrEmpty(progress))
                {
                    parts.Add(progress);
                }
                parts.Add(CompatText.ModArgs("PE_ScheduleTime",
                    PeCompat.StartHour(sg), PeCompat.EndHour(sg)));
                d.Label = PeCompat.ClassName(sg);
                d.Role = ElementRole.Button;
                d.Value = CompatText.JoinSentences(parts);
                d.Extras = CompatText.Flatten(PeCompat.SubjectDescription(logic));
                return d;
            }

            if (index == ClassroomsDescriptionRow)
            {
                d.Label = CompatText.Flatten(CompatText.ModText("PE_ClassroomsDescription"));
                d.ReadOnly = true;
                return d;
            }
            object classroom = ClassroomAt(index);
            if (classroom == null)
            {
                return d;
            }
            d.Label = PeCompat.ClassroomName(classroom);
            d.Role = ElementRole.Button;
            var values = new List<string>
            {
                "RimWorldAccess.Compat.Pe.ClassroomColorFragment".Translate(
                    ColorNameHelper.NameForColor(PeCompat.ClassroomColor(classroom))),
            };
            float speed = PeCompat.ClassroomSpeed(classroom);
            if (speed > 0f)
            {
                values.Add("RimWorldAccess.Compat.Pe.ClassSpeedFragment".Translate(
                    speed.ToStringPercent()));
            }
            d.Value = CompatText.JoinSentences(values);
            return d;
        }

        private object ClassroomAt(int index)
        {
            int i = index - ClassroomRowOffset;
            return i >= 0 && i < classrooms.Count ? classrooms[i] : null;
        }

        /// <summary>The class row's progress bar text, mirroring the tab's own three formats.</summary>
        private string ProgressText(object sg, object logic)
        {
            if (!PeCompat.SubjectIsInfinite(logic))
            {
                if (PeCompat.SubjectIsSkillClass(logic))
                {
                    return CompatText.ModArgs("PE_ProgressFormat",
                        PeCompat.CurrentProgress(sg).ToString("F0"), PeCompat.SemesterGoal(sg));
                }
                return Mathf.Clamp01(PeCompat.ProgressPercentage(sg)).ToStringPercent();
            }
            if (PeCompat.SubjectShowsAttendance(logic))
            {
                ILordJobRole studentRole = PeCompat.StudentRoleOfGroup(sg);
                int seats = Mathf.Min(studentRole?.MaxCount ?? 0, PeCompat.SubjectBenchCount(logic));
                return CompatText.ModArgs("PE_ProgressFormat", PeCompat.StudentCount(sg), seats);
            }
            return null;
        }

        protected override void ActivateContentItem(int region, int index)
        {
            var options = new List<FloatMenuOption>();
            if (region == ClassesRegion)
            {
                if (index < 0 || index >= studyGroups.Count)
                {
                    return;
                }
                object sg = studyGroups[index];
                options.Add(new FloatMenuOption(CompatText.ModText("PE_ToggleStudentList"),
                    () => PeCompat.OpenEditClassDialog(sg)));
                options.Add(new FloatMenuOption(
                    CompatText.ModText(PeCompat.Suspended(sg) ? "PE_ResumeClass" : "PE_SuspendClass"),
                    delegate
                    {
                        PeCompat.SetSuspended(sg, !PeCompat.Suspended(sg));
                        RefreshModel();
                    }));
                // MUTATION-C: mirrors MainTabWindow_Education.DrawClassHeader's delete button —
                // the same vanilla Dialog_Confirm around the manager's own RemoveStudyGroup.
                options.Add(new FloatMenuOption(CompatText.ModText("PE_DeleteClass"),
                    () => Find.WindowStack.Add(new Dialog_Confirm(
                        CompatText.ModText("PE_ConfirmDeleteClass"),
                        delegate
                        {
                            PeCompat.RemoveStudyGroup(sg);
                            RefreshModel();
                        }))));
                Pawn teacher = PeCompat.TeacherOf(sg);
                if (teacher != null)
                {
                    options.Add(new FloatMenuOption(
                        "RimWorldAccess.Compat.Pe.JumpToTeacher".Translate(teacher.LabelShortCap),
                        () => CameraJumper.TryJumpAndSelect(teacher)));
                }
            }
            else
            {
                object classroom = ClassroomAt(index);
                if (classroom == null)
                {
                    return;
                }
                options.Add(new FloatMenuOption("Rename".Translate(),
                    () => PeCompat.OpenRenameClassroomDialog(classroom)));
                options.Add(new FloatMenuOption(CompatText.ModText("PE_ClassroomSettings"),
                    () => PeCompat.OpenClassroomSettingsDialog(classroom)));
                options.Add(new FloatMenuOption(CompatText.ModText("PE_ChangeColor"),
                    () => PeCompat.OpenClassroomColorPicker(classroom)));
                Thing board = PeCompat.ClassroomBoardThing(classroom);
                if (board != null && board.Spawned)
                {
                    options.Add(new FloatMenuOption(
                        "RimWorldAccess.Compat.Pe.JumpToLearningBoard".Translate(),
                        () => CameraJumper.TryJumpAndSelect(board)));
                }
            }
            KeyboardFloatMenu.Open(options, givesColonistOrders: false);
        }

        /// <summary>Mirrors the tab's plus-button handler: the same two reject messages, then
        /// the same create dialog. MUTATION-C: mirrors MainTabWindow_Education.DrawBanner; the
        /// gate lives inline in that button's handler with no callable method behind it.</summary>
        private void CreateClass()
        {
            if (!PeCompat.HasBellOnMap(Find.CurrentMap))
            {
                Messages.Message(CompatText.ModText("PE_NoBellToCreateClass"),
                    MessageTypeDefOf.RejectInput, historical: false);
                return;
            }
            if (classrooms.Count == 0)
            {
                Messages.Message(CompatText.ModText("PE_CreateClassroomFirst"),
                    MessageTypeDefOf.RejectInput, historical: false);
                return;
            }
            PeCompat.OpenCreateClassDialog(Find.CurrentMap);
        }

        private void OpenScheduling()
        {
            MainButtonDef schedule = DefDatabase<MainButtonDef>.GetNamedSilentFail("Schedule");
            if (schedule != null)
            {
                Find.MainTabsRoot.SetCurrentTab(schedule);
            }
        }
    }
}
