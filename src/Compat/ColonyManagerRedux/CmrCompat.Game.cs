using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Reflection-only surface for Colony Manager Redux (packageId
    /// <c>ilyvion.colonymanagerredux</c>): its manager window, the per-map <c>Manager</c>
    /// component, and the <c>ManagerTab</c>/<c>ManagerJob</c>/<c>Trigger</c>/<c>UpdateInterval</c>
    /// bases. The concrete types are internal to the mod and are never named here: every member is
    /// declared on a public base and dispatches virtually to whatever subclass is live, so one
    /// binding serves every manager type the mod or an add-on defines.
    ///
    /// A renamed member makes the whole surface decline with a single log line naming every miss,
    /// so the window keeps its generic reader rather than attaching a half-bound screen. Per-call
    /// reads never throw: a failure logs once per member and the caller gets an empty answer.
    ///
    /// Every write is the exact call the mod's own widget makes (doctrine A), through the
    /// property's own setter and never a backing field, so no gate is hand-copied.
    /// <see cref="GoTo"/> rides the tab icons' click path, which runs the PreClose/PreOpen
    /// bookkeeping a bare <c>CurrentTab</c> write would skip — which is why no setter for that
    /// property is bound at all. <see cref="SelectJob"/> writes <c>ManagerTab.Selected</c>, whose
    /// setter runs the mod's PreSelect/PostSelect hooks. <see cref="SetJobSuspended"/> is verbatim
    /// what the stamp button does, and that setter clears <c>CausedException</c> itself.
    /// <see cref="IncreasePriority"/> and <see cref="DecreasePriority"/> are the delegates the row
    /// arrows hand to the mod's reorder button, so priority bookkeeping stays the mod's own and
    /// <c>JobTracker</c> is never touched directly.
    /// </summary>
    internal static partial class CmrCompat
    {
        private static readonly Type managerWindowType;
        private static readonly Type managerTabType;

        private static readonly MethodInfo currentTabGetter;
        private static readonly MethodInfo goToMethod;
        private static readonly MethodInfo managerForMethod;
        private static readonly MethodInfo managerTabsGetter;
        private static readonly MethodInfo managerJobTrackerGetter;

        private static readonly MethodInfo tabLabelGetter;
        private static readonly MethodInfo tabDefGetter;
        private static readonly MethodInfo tabEnabledGetter;
        private static readonly MethodInfo tabDisabledReasonGetter;
        private static readonly MethodInfo tabShowGetter;
        private static readonly MethodInfo tabSelectedGetter;
        private static readonly MethodInfo tabSelectedSetter;
        private static readonly MethodInfo tabJobsGetter;
        private static readonly MethodInfo tabOrderBoundsMethod;
        private static readonly MethodInfo tabIncreasePriorityMethod;
        private static readonly MethodInfo tabDecreasePriorityMethod;

        private static readonly MethodInfo jobLabelGetter;
        private static readonly MethodInfo jobTabGetter;
        private static readonly MethodInfo tabGetSubLabelMethod;
        private static readonly MethodInfo jobTargetsLabelGetter;
        private static readonly MethodInfo jobIsSuspendedGetter;
        private static readonly MethodInfo jobIsSuspendedSetter;
        private static readonly MethodInfo jobIsCompletedGetter;
        private static readonly MethodInfo jobIsManagedGetter;
        private static readonly MethodInfo jobSuspendedTooltipGetter;
        private static readonly MethodInfo jobCompletedTooltipGetter;
        private static readonly MethodInfo jobSuspendedByErrorTooltipGetter;
        private static readonly MethodInfo jobCausedExceptionGetter;
        private static readonly MethodInfo jobTriggerGetter;
        private static readonly MethodInfo jobUpdateIntervalGetter;
        private static readonly MethodInfo jobTicksSinceLastUpdateGetter;
        private static readonly MethodInfo jobHasBeenUpdatedGetter;

        private static readonly MethodInfo triggerStatusTooltipGetter;
        private static readonly MethodInfo updateIntervalLabelGetter;

        private static readonly bool ready;

        static CmrCompat()
        {
            var surface = new ReflectionSurface("CmrCompat");

            managerWindowType = surface.Type("ColonyManagerRedux.MainTabWindow_Manager");
            Type managerType = surface.Type("ColonyManagerRedux.Manager");
            Type jobTrackerType = surface.Type("ColonyManagerRedux.JobTracker");
            managerTabType = surface.Type("ColonyManagerRedux.ManagerTab");
            Type managerJobType = surface.Type("ColonyManagerRedux.ManagerJob");
            Type triggerType = surface.Type("ColonyManagerRedux.Trigger");
            Type updateIntervalType = surface.Type("ColonyManagerRedux.UpdateInterval");

            currentTabGetter = Getter(surface.Property(managerWindowType, "CurrentTab"));
            managerForMethod = surface.Method(managerType, "For", new[] { typeof(Map) });
            managerTabsGetter = Getter(surface.Property(managerType, "Tabs"));
            managerJobTrackerGetter = Getter(surface.Property(managerType, "JobTracker"));

            tabLabelGetter = Getter(surface.Property(managerTabType, "Label"));
            tabDefGetter = Getter(surface.Property(managerTabType, "Def"));
            tabEnabledGetter = Getter(surface.Property(managerTabType, "Enabled"));
            tabDisabledReasonGetter = Getter(surface.Property(managerTabType, "DisabledReason"));
            tabShowGetter = Getter(surface.Property(managerTabType, "Show"));
            PropertyInfo selectedProperty = surface.Property(managerTabType, "Selected");
            tabSelectedGetter = Getter(selectedProperty);
            tabSelectedSetter = surface.Required("ManagerTab.Selected setter", Setter(selectedProperty));
            tabJobsGetter = Getter(surface.Property(managerTabType, "ManagerJobs"));

            // Overload lookup by signature needs every parameter type resolved: a null in the array
            // throws, and a missing type has already made the surface not-ready.
            if (managerTabType != null && managerJobType != null && jobTrackerType != null)
            {
                goToMethod = surface.Method(managerWindowType, "GoTo", new[] { managerTabType, managerJobType });
                tabOrderBoundsMethod = surface.Method(managerTabType, "GetJobOrderBounds",
                    new[] { managerJobType, jobTrackerType });
                tabIncreasePriorityMethod = surface.Method(managerTabType, "IncreasePriority",
                    new[] { jobTrackerType, managerJobType });
                tabDecreasePriorityMethod = surface.Method(managerTabType, "DecreasePriority",
                    new[] { jobTrackerType, managerJobType });
                tabGetSubLabelMethod = surface.Method(managerTabType, "GetSubLabel",
                    new[] { managerJobType });
            }

            jobLabelGetter = Getter(surface.Property(managerJobType, "Label"));
            jobTabGetter = Getter(surface.Property(managerJobType, "Tab"));
            jobTargetsLabelGetter = Getter(surface.Property(managerJobType, "TargetsLabel"));
            PropertyInfo isSuspendedProperty = surface.Property(managerJobType, "IsSuspended");
            jobIsSuspendedGetter = Getter(isSuspendedProperty);
            jobIsSuspendedSetter = surface.Required("ManagerJob.IsSuspended setter", Setter(isSuspendedProperty));
            jobIsCompletedGetter = Getter(surface.Property(managerJobType, "IsCompleted"));
            jobIsManagedGetter = Getter(surface.Property(managerJobType, "IsManaged"));
            jobSuspendedTooltipGetter = Getter(surface.Property(managerJobType, "IsSuspendedTooltip"));
            jobCompletedTooltipGetter = Getter(surface.Property(managerJobType, "IsCompletedTooltip"));
            jobSuspendedByErrorTooltipGetter =
                Getter(surface.Property(managerJobType, "IsSuspendedDueToExceptionTooltip"));
            jobCausedExceptionGetter = Getter(surface.Property(managerJobType, "CausedException"));
            jobTriggerGetter = Getter(surface.Property(managerJobType, "Trigger"));
            jobUpdateIntervalGetter = Getter(surface.Property(managerJobType, "UpdateInterval"));
            jobTicksSinceLastUpdateGetter = Getter(surface.Property(managerJobType, "TicksSinceLastUpdate"));
            jobHasBeenUpdatedGetter = Getter(surface.Property(managerJobType, "HasBeenUpdated"));

            triggerStatusTooltipGetter = Getter(surface.Property(triggerType, "StatusTooltip"));
            updateIntervalLabelGetter = Getter(surface.Property(updateIntervalType, "Label"));

            ready = surface.Ready;
        }

        /// <summary>True when every member this slice reads or writes resolved against the loaded mod.</summary>
        public static bool Ready
        {
            get { return ready; }
        }

        /// <summary>The manager window type, for the <c>ScopeForWindow</c> registration.</summary>
        public static Type ManagerWindowType
        {
            get { return managerWindowType; }
        }

        // ---- Reads ----

        /// <summary>The tab the window is showing, or null. Static on the window, so it answers even before the first draw.</summary>
        public static object CurrentTab
        {
            get { return Read(currentTabGetter, null, "CurrentTab"); }
        }

        /// <summary>The map's manager component, or null when the map has none yet.</summary>
        public static object ManagerFor(Map map)
        {
            if (!ready || map == null)
            {
                return null;
            }
            try
            {
                return managerForMethod.Invoke(null, new object[] { map });
            }
            catch (Exception ex)
            {
                Fail("ManagerFor", ex);
                return null;
            }
        }

        /// <summary>Every tab the manager built, in its own <c>ManagerDef.order</c> order.</summary>
        public static List<object> Tabs(object manager)
        {
            return Enumerate(managerTabsGetter, manager, "Tabs");
        }

        public static object JobTracker(object manager)
        {
            return Read(managerJobTrackerGetter, manager, "JobTracker");
        }

        public static string TabLabel(object tab)
        {
            return ReadString(tabLabelGetter, tab, "TabLabel");
        }

        /// <summary>The tab's def name, for diagnostics only -- never matched against.</summary>
        public static string TabDefName(object tab)
        {
            Def def = Read(tabDefGetter, tab, "TabDefName") as Def;
            return def != null ? def.defName : "";
        }

        /// <summary>False for a tab the game blocks right now (Power before its research); such tabs still draw, greyed.</summary>
        public static bool TabEnabled(object tab)
        {
            return ReadBool(tabEnabledGetter, tab, "TabEnabled");
        }

        public static string TabDisabledReason(object tab)
        {
            return ReadString(tabDisabledReasonGetter, tab, "TabDisabledReason");
        }

        /// <summary>False for a manager the player switched off in mod settings; the window draws no icon for it.</summary>
        public static bool TabShow(object tab)
        {
            return ReadBool(tabShowGetter, tab, "TabShow");
        }

        public static object TabSelectedJob(object tab)
        {
            return Read(tabSelectedGetter, tab, "TabSelectedJob");
        }

        /// <summary>The tab's own jobs, in the mod's enumeration order (priority order).</summary>
        public static List<object> TabJobs(object tab)
        {
            return Enumerate(tabJobsGetter, tab, "TabJobs(" + TabDefName(tab) + ")");
        }

        public static string JobLabel(object job)
        {
            return ReadString(jobLabelGetter, job, "JobLabel");
        }

        /// <summary>The tab that owns a job, whichever tab is currently shown -- Overview's rows span all tabs.</summary>
        public static object JobTab(object job)
        {
            return Read(jobTabGetter, job, "JobTab");
        }

        /// <summary>
        /// The job's sub-label in ITS OWN tab's words, through the same virtual the sighted rows
        /// call, so a row never falls back to wording its tab would not draw.
        /// </summary>
        public static string TabGetSubLabel(object tab, object job)
        {
            if (!ready || tab == null || job == null || tabGetSubLabelMethod == null)
            {
                return "";
            }
            try
            {
                return tabGetSubLabelMethod.Invoke(tab, new[] { job }) as string ?? "";
            }
            catch (Exception ex)
            {
                Fail("TabGetSubLabel", ex);
                return "";
            }
        }

        /// <summary>The job's targets as the mod's own comma-separated sub-label, or its "none" placeholder.</summary>
        public static string JobTargetsLabel(object job)
        {
            return ReadString(jobTargetsLabelGetter, job, "JobTargetsLabel");
        }

        public static bool JobIsSuspended(object job)
        {
            return ReadBool(jobIsSuspendedGetter, job, "JobIsSuspended");
        }

        public static bool JobIsCompleted(object job)
        {
            return ReadBool(jobIsCompletedGetter, job, "JobIsCompleted");
        }

        /// <summary>False for a job that exists but was never handed to the manager (a freshly created, unsaved one).</summary>
        public static bool JobIsManaged(object job)
        {
            return ReadBool(jobIsManagedGetter, job, "JobIsManaged");
        }

        /// <summary>True when the job was suspended by an error rather than by the player.</summary>
        public static bool JobCausedException(object job)
        {
            return Read(jobCausedExceptionGetter, job, "JobCausedException") != null;
        }

        public static string JobSuspendedTooltip(object job)
        {
            return ReadString(jobSuspendedTooltipGetter, job, "JobSuspendedTooltip");
        }

        public static string JobCompletedTooltip(object job)
        {
            return ReadString(jobCompletedTooltipGetter, job, "JobCompletedTooltip");
        }

        public static string JobSuspendedByErrorTooltip(object job)
        {
            return ReadString(jobSuspendedByErrorTooltipGetter, job, "JobSuspendedByErrorTooltip");
        }

        /// <summary>The job's trigger, or null -- the handle the per-job-type Threshold job APIs resolve from a bare job.</summary>
        public static object JobTrigger(object job)
        {
            return Read(jobTriggerGetter, job, "JobTrigger");
        }

        /// <summary>
        /// The job's progress toward its trigger in the trigger's own words, the text its progress
        /// bar carries as a tooltip. Empty when the job has no trigger, or one reporting no status.
        /// </summary>
        public static string JobProgressTooltip(object job)
        {
            object trigger = Read(jobTriggerGetter, job, "JobProgressTooltip");
            return trigger == null ? "" : ReadString(triggerStatusTooltipGetter, trigger, "TriggerStatusTooltip");
        }

        /// <summary>The job's check interval, in the mod's own words ("daily", "hourly").</summary>
        public static string JobUpdateIntervalLabel(object job)
        {
            object interval = Read(jobUpdateIntervalGetter, job, "JobUpdateIntervalLabel");
            return interval == null ? "" : ReadString(updateIntervalLabelGetter, interval, "UpdateIntervalLabel");
        }

        public static int JobTicksSinceLastUpdate(object job)
        {
            object value = Read(jobTicksSinceLastUpdateGetter, job, "JobTicksSinceLastUpdate");
            return value is int ticks ? ticks : 0;
        }

        public static bool JobHasBeenUpdated(object job)
        {
            return ReadBool(jobHasBeenUpdatedGetter, job, "JobHasBeenUpdated");
        }

        /// <summary>
        /// The tab's own answer for whether a job already sits at the top or bottom of its type's
        /// priority range. The arrow buttons read the same pair to decide whether to draw, so the
        /// two gates cannot drift.
        /// </summary>
        public static bool TryGetJobOrderBounds(object tab, object job, object jobTracker,
            out bool atTop, out bool atBottom)
        {
            atTop = false;
            atBottom = false;
            if (!ready || tab == null || job == null || jobTracker == null || tabOrderBoundsMethod == null)
            {
                return false;
            }
            try
            {
                object bounds = tabOrderBoundsMethod.Invoke(tab, new[] { job, jobTracker });
                if (!(bounds is ValueTuple<bool, bool> pair))
                {
                    return false;
                }
                atTop = pair.Item1;
                atBottom = pair.Item2;
                return true;
            }
            catch (Exception ex)
            {
                Fail("TryGetJobOrderBounds", ex);
                return false;
            }
        }

        // ---- Mutators; see the class remarks for each one's vehicle ----

        /// <summary>Switches the window to another tab through the tab icons' own click path.</summary>
        public static void GoTo(object tab)
        {
            GoTo(tab, null);
        }

        /// <summary>
        /// The jump-to-job form of the same click path: Overview's per-row tab icon calls
        /// <c>GoTo(tab, job)</c>, which switches the tab AND selects the job there.
        /// </summary>
        public static void GoTo(object tab, object job)
        {
            if (!ready || tab == null || goToMethod == null)
            {
                return;
            }
            try
            {
                goToMethod.Invoke(null, new[] { tab, job });
            }
            catch (Exception ex)
            {
                Fail("GoTo", ex);
            }
        }

        /// <summary>Selects a job in its tab through the job row's own selection setter.</summary>
        public static void SelectJob(object tab, object job)
        {
            Write(tabSelectedSetter, tab, job, "SelectJob");
        }

        /// <summary>Suspends or resumes a job exactly as the row's stamp button does.</summary>
        public static void SetJobSuspended(object job, bool suspended)
        {
            Write(jobIsSuspendedSetter, job, suspended, "SetJobSuspended");
        }

        /// <summary>Moves a job one step up its tab's priority order, through the up arrow's own delegate.</summary>
        public static void IncreasePriority(object tab, object jobTracker, object job)
        {
            Reprioritize(tabIncreasePriorityMethod, tab, jobTracker, job, "IncreasePriority");
        }

        /// <summary>Moves a job one step down its tab's priority order, through the down arrow's own delegate.</summary>
        public static void DecreasePriority(object tab, object jobTracker, object job)
        {
            Reprioritize(tabDecreasePriorityMethod, tab, jobTracker, job, "DecreasePriority");
        }

        private static void Reprioritize(MethodInfo method, object tab, object jobTracker, object job,
            string caller)
        {
            if (!ready || method == null || tab == null || jobTracker == null || job == null)
            {
                return;
            }
            try
            {
                method.Invoke(tab, new[] { jobTracker, job });
            }
            catch (Exception ex)
            {
                Fail(caller, ex);
            }
        }

        // ---- Binding and invocation plumbing ----

        private static MethodInfo Getter(PropertyInfo property)
        {
            return property != null ? property.GetGetMethod(true) : null;
        }

        private static MethodInfo Setter(PropertyInfo property)
        {
            return property != null ? property.GetSetMethod(true) : null;
        }

        private static object Read(MethodInfo getter, object instance, string member)
        {
            if (!ready || getter == null || (instance == null && !getter.IsStatic))
            {
                return null;
            }
            try
            {
                return getter.Invoke(instance, null);
            }
            catch (Exception ex)
            {
                Fail(member, ex);
                return null;
            }
        }

        private static string ReadString(MethodInfo getter, object instance, string member)
        {
            return Read(getter, instance, member) as string ?? "";
        }

        private static bool ReadBool(MethodInfo getter, object instance, string member)
        {
            return Read(getter, instance, member) is bool value && value;
        }

        private static List<object> Enumerate(MethodInfo getter, object instance, string member)
        {
            var result = new List<object>();
            var items = Read(getter, instance, member) as IEnumerable;
            if (items == null)
            {
                return result;
            }
            try
            {
                foreach (object item in items)
                {
                    if (item != null)
                    {
                        result.Add(item);
                    }
                }
            }
            catch (Exception ex)
            {
                Fail(member, ex);
            }
            return result;
        }

        private static void Write(MethodInfo setter, object instance, object value, string member)
        {
            if (!ready || setter == null || instance == null)
            {
                return;
            }
            try
            {
                setter.Invoke(instance, new[] { value });
            }
            catch (Exception ex)
            {
                Fail(member, ex);
            }
        }

        private static readonly HashSet<string> loggedFailures = new HashSet<string>();

        /// <summary>
        /// Reports a reflection call that threw, once per member per session: several readers run
        /// from the screen's model refresh, so an unguarded log would bury everything else.
        /// </summary>
        private static void Fail(string member, Exception ex)
        {
            if (loggedFailures.Add(member))
            {
                ModLogger.Error("CmrCompat." + member + " failed: " + ex.Message);
            }
        }
    }
}
