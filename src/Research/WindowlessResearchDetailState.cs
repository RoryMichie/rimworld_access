using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Data/lifecycle facade for the detail view of a specific research project: lifecycle,
    /// the drill-down navigation stack, activate/info-card dispatch, and the research-start path.
    /// Stateless content and announcement builders live on
    /// <see cref="WindowlessResearchDetailHelper"/>; cursor and typeahead machinery on
    /// <see cref="RimWorldAccess.Shell.ResearchDetailScope"/>, which this facade drives through
    /// bridge callbacks wired once by ResearchScopeMirror. The callbacks fire on every Open, since
    /// drilling from one project into another leaves the scope continuously pushed and offers no
    /// "just became visible" hook to rebuild on.
    /// </summary>
    public static class WindowlessResearchDetailState
    {
        private static bool isActive = false;
        private static ResearchProjectDef currentProject = null;
        private static Stack<ResearchProjectDef> navigationStack = new Stack<ResearchProjectDef>();

        // Vanilla's own Research-button click branch (MainTabWindow_Research.cs:631-654).
        private static readonly MethodInfo mi_attemptBeginResearch =
            AccessTools.Method(typeof(MainTabWindow_Research), "AttemptBeginResearch");

        public static bool IsActive => isActive;

        /// <summary>The project the detail view is currently showing.</summary>
        internal static ResearchProjectDef CurrentProject
        {
            get { return currentProject; }
        }

        /// <summary>Wired once by ResearchScopeMirror's static constructor to ResearchDetailScope.BuildAndAnnounce.</summary>
        internal static Action<ResearchProjectDef> BuildAndAnnounceCallback;

        /// <summary>Wired once by ResearchScopeMirror's static constructor to ResearchDetailScope.RefreshTreePreservingCursor.</summary>
        internal static Action<ResearchProjectDef> RefreshTreeCallback;

        /// <summary>
        /// Opens the detail view for a project, always rebuilding the tree and re-announcing even
        /// when another project was already showing, so drilling between projects works.
        /// </summary>
        public static void Open(ResearchProjectDef project)
        {
            // Vanilla's tile click handler is gated behind !IsHidden
            // (MainTabWindow_Research.cs:997), so a hidden project has no reachable detail pane.
            if (project != null && project.IsHidden)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.SpeakData(WindowlessResearchDetailHelper.HiddenResearchLabel(), SpeechPriority.High);
                return;
            }

            currentProject = project;
            isActive = true;
            BuildAndAnnounceCallback?.Invoke(project);
        }

        /// <summary>
        /// Opens the detail view for a project, pushing the current one onto the navigation stack.
        /// </summary>
        public static void OpenWithBackNavigation(ResearchProjectDef project)
        {
            if (currentProject != null)
            {
                navigationStack.Push(currentProject);
            }
            Open(project);
        }

        /// <summary>
        /// Closes the detail view. If there's a project in the stack, goes back to it.
        /// </summary>
        public static void Close()
        {
            if (navigationStack.Count > 0)
            {
                var previousProject = navigationStack.Pop();
                Open(previousProject);
                TolkHelper.Speak("RimWorldAccess.Research.Detail.BackTo".Loc(previousProject.LabelCap));
            }
            else
            {
                isActive = false;
                currentProject = null;
                navigationStack.Clear();
                TolkHelper.Speak("RimWorldAccess.Research.Detail.ReturnedToMenu".Loc());
            }
        }

        /// <summary>
        /// Silent session-boundary reset: unlike <see cref="Close"/> it announces nothing and
        /// clears the navigation stack outright rather than reopening the previous project.
        /// </summary>
        internal static void ResetHard()
        {
            isActive = false;
            currentProject = null;
            navigationStack.Clear();
        }

        #region Custom Actions

        /// <summary>
        /// Enter on the given tree node. Category nodes are NOT handled here: the scope's own
        /// fallback toggles them through the shared expand/collapse machinery.
        /// </summary>
        internal static void HandleActivate(InspectionTreeItem item)
        {
            if (currentProject == null) return;

            var nodeType = item.Data is DetailNodeType dt ? dt : DetailNodeType.Info;

            switch (nodeType)
            {
                case DetailNodeType.ResearchItem:
                    if (item.LinkedDef is ResearchProjectDef linkedProject)
                    {
                        OpenWithBackNavigation(linkedProject);
                    }
                    break;

                case DetailNodeType.UnlockedItem:
                    TolkHelper.SpeakData(item.Label);
                    break;

                case DetailNodeType.Action:
                    ExecuteResearchAction();
                    break;

                case DetailNodeType.DebugFinish:
                    Find.ResearchManager.SetCurrentProject(currentProject);
                    Find.ResearchManager.FinishProject(currentProject);
                    TolkHelper.Speak("RimWorldAccess.Research.Action.DebugFinished".Loc(currentProject.LabelCap));
                    RefreshTreeCallback?.Invoke(currentProject);
                    break;

                case DetailNodeType.DebugApplyTechprint:
                    Find.ResearchManager.ApplyTechprint(currentProject, null);
                    SoundDefOf.TechprintApplied.PlayOneShotOnCamera();
                    TolkHelper.Speak("RimWorldAccess.Research.Action.DebugTechprintApplied".Loc(
                        currentProject.TechprintsApplied, currentProject.TechprintCount));
                    RefreshTreeCallback?.Invoke(currentProject);
                    break;

                case DetailNodeType.Info:
                    if (!string.IsNullOrEmpty(item.Description))
                    {
                        TolkHelper.SpeakData(item.Description);
                    }
                    break;
            }
        }

        /// <summary>
        /// Alt+I on the given tree node. Same spoiler gate as Open(): never let a hidden project's
        /// real name/description reach the info card.
        /// </summary>
        internal static void HandleInfoCard(InspectionTreeItem item)
        {
            if (currentProject == null) return;

            var nodeType = item.Data is DetailNodeType dt ? dt : DetailNodeType.Info;

            switch (nodeType)
            {
                case DetailNodeType.ResearchItem:
                    if (item.LinkedDef is ResearchProjectDef linkedProject)
                    {
                        if (linkedProject.IsHidden)
                        {
                            SoundDefOf.ClickReject.PlayOneShotOnCamera();
                            TolkHelper.SpeakData(WindowlessResearchDetailHelper.HiddenResearchLabel(), SpeechPriority.High);
                            return;
                        }
                        Find.WindowStack.Add(new Dialog_InfoCard(linkedProject));
                    }
                    break;

                case DetailNodeType.UnlockedItem:
                    InfoCardState.TryOpenInfoCardForDef(item.LinkedDef);
                    break;

                case DetailNodeType.Info:
                case DetailNodeType.Action:
                case DetailNodeType.DebugFinish:
                case DetailNodeType.DebugApplyTechprint:
                    Find.WindowStack.Add(new Dialog_InfoCard(currentProject));
                    break;

                case DetailNodeType.Category:
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    TolkHelper.Speak("RimWorldAccess.Research.Detail.NoInfoCardForSection".Loc());
                    break;
            }
        }

        #endregion

        #region Research Actions

        /// <summary>
        /// Starts or stops research on the current project.
        /// </summary>
        private static void ExecuteResearchAction()
        {
            if (currentProject == null) return;

            if (Find.ResearchManager.IsCurrentProject(currentProject))
            {
                Find.ResearchManager.StopProject(currentProject);
                TolkHelper.Speak("RimWorldAccess.Research.Action.Stopped".Loc(currentProject.LabelCap));
                RefreshTreeCallback?.Invoke(currentProject);
                return;
            }

            if (currentProject.IsFinished)
            {
                TolkHelper.Speak("RimWorldAccess.Research.Action.AlreadyCompleted".Loc(currentProject.LabelCap));
                return;
            }

            // CanStartNow is the full vanilla gate — prerequisites, techprints, bench, mechanitor,
            // grav-engine inspection, study requirements, hidden — so never hand-copy a subset.
            if (!currentProject.CanStartNow)
            {
                TolkHelper.Speak("RimWorldAccess.Research.Action.Locked".Loc(
                    WindowlessResearchDetailHelper.BuildLockedReasons(currentProject)), SpeechPriority.High);
                return;
            }

            // Regular research and each anomaly knowledge category occupy independent slots, so
            // only the same track's project is displaced.
            var previousProject = Find.ResearchManager.GetProject(currentProject.knowledgeCategory);

            // Rides vanilla's AttemptBeginResearch on a transient, never-opened window instance so
            // the missing-memes confirmation, sound, tutor event, and no-bench caution all fire.
            var researchWindow = new MainTabWindow_Research();
            mi_attemptBeginResearch.Invoke(researchWindow, new object[] { currentProject });

            if (Find.ResearchManager.IsCurrentProject(currentProject))
            {
                // Started synchronously — no Ideology missing-memes confirmation was needed.
                if (previousProject != null && previousProject != currentProject)
                {
                    float previousProgress = previousProject.ProgressPercent * 100f;
                    TolkHelper.Speak("RimWorldAccess.Research.Action.StartedReplacing".Loc(
                        currentProject.LabelCap, previousProject.LabelCap, previousProgress.ToString("F0")));
                }
                else
                {
                    TolkHelper.Speak("RimWorldAccess.Research.Action.Started".Loc(currentProject.LabelCap));
                }
                RefreshTreeCallback?.Invoke(currentProject);
            }
            // Else AttemptBeginResearch opened the missing-memes confirmation instead; the shell's
            // dialog interception announces it, and vanilla starts the project on confirm.
        }

        #endregion
    }

    /// <summary>Type of detail node.</summary>
    public enum DetailNodeType
    {
        Info,           // Description section
        Category,       // Prerequisites, Unlocks, Dependents headers
        ResearchItem,   // A research project that can be drilled into
        UnlockedItem,   // A building/recipe that can be inspected
        Action,         // Start/Stop research button
        DebugFinish,            // DEV: finish the project now
        DebugApplyTechprint     // DEV: apply one techprint
    }
}
