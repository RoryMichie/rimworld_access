using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Verse;

namespace RimWorldAccess
{
    internal static partial class CmrCompat
    {
        /// <summary>
        /// Colony Manager Redux's Logs tab, its per-map log buffer comp, and the public
        /// <c>ManagerLog</c>/<c>LogDetails</c> entries the tab's own row list walks
        /// (<see cref="Shell.CmrLogsDetails"/>). <c>ManagerTab_Logs</c> and its nested
        /// <c>LogsComp</c> are internal to the mod; <c>ManagerLog</c> and <c>LogDetails</c> are
        /// public, so their readers below are plain reflected getters with no enum boxing.
        ///
        /// MUTATION VEHICLES.
        /// <list type="bullet">
        /// <item><see cref="SetSelectedLog"/>: MUTATION-C, mirrors the log row's own click body
        /// (ManagerTab_Logs.cs:79-90) -- a write to the private <c>selectedLog</c> field, which has
        /// no setter, followed by the private <c>RecacheLookTargets</c> the mod's own click handler
        /// calls right after selecting or deselecting.</item>
        /// <item><see cref="GoToJobTab"/> invokes <c>ManagerLog.GoToJobTab</c>, the log icon's own
        /// click path (ManagerTab_Logs.cs:222, ManagerLog.cs:97-103) -- doctrine A.</item>
        /// </list>
        ///
        /// <see cref="TryGetNextTarget"/> reads <c>LogDetails.NextTargetIndex</c>, which
        /// self-increments on every read (ManagerLog.cs:175-186); callers must read it exactly once
        /// per activation, never from a describe path, or the cycle skips an entry no player asked
        /// to skip.
        /// </summary>
        internal static class Logs
        {
            private const BindingFlags GenericMethodFlags =
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

            private static readonly Type tabType;
            private static readonly Type managerType;
            private static readonly Type settingsType;
            private static readonly Type logType;
            private static readonly Type logDetailsType;

            private static readonly FieldInfo selectedLogField;
            private static readonly MethodInfo recacheLookTargetsMethod;
            private static readonly MethodInfo managerSettingsGetter;
            private static readonly FieldInfo showLogsWithNoWorkDoneField;
            private static readonly MethodInfo compOfTypeLogsMethod;
            private static readonly MethodInfo logsGetter;

            private static readonly MethodInfo jobLabelCapGetter;
            private static readonly MethodInfo jobLabelGetter;
            private static readonly MethodInfo logDateGetter;
            private static readonly MethodInfo workDoneGetter;
            private static readonly MethodInfo hasJobGetter;
            private static readonly MethodInfo tabGetter;
            private static readonly MethodInfo isForJobMethod;
            private static readonly MethodInfo goToJobTabMethod;
            private static readonly MethodInfo detailsGetter;

            private static readonly MethodInfo textGetter;
            private static readonly MethodInfo targetsGetter;
            private static readonly MethodInfo nextTargetIndexGetter;

            private static readonly bool ready;

            static Logs()
            {
                var surface = new ReflectionSurface("CmrCompat.Logs");

                tabType = surface.Type("ColonyManagerRedux.Managers.ManagerTab_Logs");
                Type logsCompType = surface.Type("ColonyManagerRedux.Managers.ManagerTab_Logs+LogsComp");
                managerType = surface.Type("ColonyManagerRedux.Manager");
                settingsType = surface.Type("ColonyManagerRedux.Managers.ManagerSettings_Logs");
                Type managerJobType = surface.Type("ColonyManagerRedux.ManagerJob");
                logType = surface.Type("ColonyManagerRedux.ManagerLog");
                logDetailsType = surface.Type("ColonyManagerRedux.LogDetails");

                selectedLogField = surface.Field(tabType, "selectedLog");
                recacheLookTargetsMethod = surface.Method(tabType, "RecacheLookTargets", new Type[0]);
                managerSettingsGetter = Getter(surface.Property(tabType, "ManagerSettings"));
                showLogsWithNoWorkDoneField = surface.Field(settingsType, "ShowLogsWithNoWorkDone");

                if (logsCompType != null && managerType != null)
                {
                    compOfTypeLogsMethod = surface.Required("Manager.CompOfType<LogsComp>() closed",
                        CloseGeneric(managerType, "CompOfType", logsCompType));
                    logsGetter = Getter(surface.Property(logsCompType, "Logs"));
                }

                jobLabelCapGetter = Getter(surface.Property(logType, "JobLabelCap"));
                jobLabelGetter = Getter(surface.Property(logType, "JobLabel"));
                logDateGetter = Getter(surface.Property(logType, "LogDate"));
                workDoneGetter = Getter(surface.Property(logType, "WorkDone"));
                hasJobGetter = Getter(surface.Property(logType, "HasJob"));
                tabGetter = Getter(surface.Property(logType, "Tab"));
                if (managerJobType != null)
                {
                    isForJobMethod = surface.Method(logType, "IsForJob", new[] { managerJobType });
                }
                goToJobTabMethod = surface.Method(logType, "GoToJobTab", new Type[0]);
                detailsGetter = Getter(surface.Property(logType, "Details"));

                textGetter = Getter(surface.Property(logDetailsType, "Text"));
                targetsGetter = Getter(surface.Property(logDetailsType, "Targets"));
                nextTargetIndexGetter = Getter(surface.Property(logDetailsType, "NextTargetIndex"));

                ready = surface.Ready;
            }

            /// <summary>True when every member the Logs detail rows read or write resolved.</summary>
            public static bool Ready
            {
                get { return ready; }
            }

            /// <summary>Whether this tab is the mod's Logs tab, by the type resolved above.</summary>
            public static bool HandlesTab(object tab)
            {
                return ready && tab != null && tabType != null && tabType.IsInstanceOfType(tab);
            }

            // ------------------------------------------------------------------
            // The log buffer.
            // ------------------------------------------------------------------

            /// <summary>
            /// Every log the map's comp buffer holds, oldest-first exactly as the buffer enumerates
            /// (the row provider reverses, matching the tab's own <c>logs.Reverse()</c>,
            /// ManagerTab_Logs.cs:57).
            /// </summary>
            public static List<object> LogsFor(object manager)
            {
                var result = new List<object>();
                if (!ready || manager == null || compOfTypeLogsMethod == null || logsGetter == null)
                {
                    return result;
                }
                object comp;
                try
                {
                    comp = compOfTypeLogsMethod.Invoke(manager, null);
                }
                catch (Exception ex)
                {
                    Fail("LogsFor", ex);
                    return result;
                }
                if (comp == null)
                {
                    return result;
                }
                var items = Get(logsGetter, comp, "LogsFor") as IEnumerable;
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
                    Fail("LogsFor", ex);
                }
                return result;
            }

            /// <summary>The tab's own setting for whether a log with no work done still shows. Defaults to the mod's own default (true) on decline.</summary>
            public static bool ShowLogsWithNoWorkDone(object tab)
            {
                object settings = Get(managerSettingsGetter, tab, "ShowLogsWithNoWorkDone");
                if (settings == null || showLogsWithNoWorkDoneField == null)
                {
                    return true;
                }
                try
                {
                    object value = showLogsWithNoWorkDoneField.GetValue(settings);
                    return !(value is bool flag) || flag;
                }
                catch (Exception ex)
                {
                    Fail("ShowLogsWithNoWorkDone", ex);
                    return true;
                }
            }

            /// <summary>The log the tab's own row list is showing expanded, or null.</summary>
            public static object SelectedLog(object tab)
            {
                if (!ready || tab == null || selectedLogField == null)
                {
                    return null;
                }
                try
                {
                    return selectedLogField.GetValue(tab);
                }
                catch (Exception ex)
                {
                    Fail("SelectedLog", ex);
                    return null;
                }
            }

            /// <summary>
            /// MUTATION-C: mirrors the log row's own click body (ManagerTab_Logs.cs:79-90): writes
            /// the private <c>selectedLog</c> store (null collapses), then unconditionally calls the
            /// mod's own <c>RecacheLookTargets</c> -- correct even for a null log, since that method
            /// clears its cache first thing (ManagerTab_Logs.cs:26-34). Neither step has a gated
            /// setter to invoke instead.
            /// </summary>
            public static void SetSelectedLog(object tab, object log)
            {
                if (!ready || tab == null || selectedLogField == null)
                {
                    return;
                }
                try
                {
                    // MUTATION-C: mirrors ManagerTab_Logs.cs:79-90 (the log row's click body:
                    // selectedLog = log or null, then RecacheLookTargets); private view state with
                    // no gated setter to invoke instead.
                    selectedLogField.SetValue(tab, log);
                }
                catch (Exception ex)
                {
                    Fail("SetSelectedLog", ex);
                    return;
                }
                if (recacheLookTargetsMethod == null)
                {
                    return;
                }
                try
                {
                    recacheLookTargetsMethod.Invoke(tab, null);
                }
                catch (Exception ex)
                {
                    Fail("SetSelectedLog", ex);
                }
            }

            // ------------------------------------------------------------------
            // ManagerLog readers.
            // ------------------------------------------------------------------

            public static string LogLabelCap(object log)
            {
                return GetString(jobLabelCapGetter, log, "LogLabelCap");
            }

            public static string LogJobLabel(object log)
            {
                return GetString(jobLabelGetter, log, "LogJobLabel");
            }

            public static string LogDate(object log)
            {
                return GetString(logDateGetter, log, "LogDate");
            }

            public static bool LogWorkDone(object log)
            {
                return GetBool(workDoneGetter, log, "LogWorkDone");
            }

            public static bool LogHasJob(object log)
            {
                return GetBool(hasJobGetter, log, "LogHasJob");
            }

            /// <summary>The log's originating job's tab, for <see cref="CmrCompat.TabEnabled"/> etc.</summary>
            public static object LogTab(object log)
            {
                return Get(tabGetter, log, "LogTab");
            }

            public static bool LogIsForJob(object log, object job)
            {
                if (!ready || log == null || job == null || isForJobMethod == null)
                {
                    return false;
                }
                try
                {
                    return isForJobMethod.Invoke(log, new[] { job }) is bool value && value;
                }
                catch (Exception ex)
                {
                    Fail("LogIsForJob", ex);
                    return false;
                }
            }

            /// <summary>The log icon's own click path (ManagerTab_Logs.cs:222, ManagerLog.cs:97-103) -- doctrine A.</summary>
            public static void GoToJobTab(object log)
            {
                if (!ready || log == null || goToJobTabMethod == null)
                {
                    return;
                }
                try
                {
                    goToJobTabMethod.Invoke(log, null);
                }
                catch (Exception ex)
                {
                    Fail("GoToJobTab", ex);
                }
            }

            /// <summary>The log's own detail entries, including its synthetic no-work-done line (ManagerLog.cs:41-54).</summary>
            public static List<object> LogDetails(object log)
            {
                var result = new List<object>();
                var items = Get(detailsGetter, log, "LogDetails") as IEnumerable;
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
                    Fail("LogDetails", ex);
                }
                return result;
            }

            // ------------------------------------------------------------------
            // LogDetails readers.
            // ------------------------------------------------------------------

            public static string DetailText(object details)
            {
                return GetString(textGetter, details, "DetailText");
            }

            public static int DetailTargetCount(object details)
            {
                var targets = Get(targetsGetter, details, "DetailTargetCount") as IList;
                return targets != null ? targets.Count : 0;
            }

            /// <summary>
            /// Reads <c>NextTargetIndex</c> -- which self-increments and wraps on every read
            /// (ManagerLog.cs:175-186) -- exactly once, then indexes <c>Targets</c> with it. Call
            /// only from an activation, never a describe path: a second read anywhere else steals
            /// the row's own next cycle position.
            /// </summary>
            public static bool TryGetNextTarget(object details, out LocalTargetInfo target)
            {
                target = LocalTargetInfo.Invalid;
                if (!ready || details == null || nextTargetIndexGetter == null || targetsGetter == null)
                {
                    return false;
                }
                try
                {
                    object indexValue = nextTargetIndexGetter.Invoke(details, null);
                    var targets = targetsGetter.Invoke(details, null) as IList;
                    if (!(indexValue is int index) || targets == null || index < 0 || index >= targets.Count)
                    {
                        return false;
                    }
                    target = (LocalTargetInfo)targets[index];
                    return true;
                }
                catch (Exception ex)
                {
                    Fail("TryGetNextTarget", ex);
                    return false;
                }
            }

            // ------------------------------------------------------------------
            // Plumbing. Gated on this block's own Ready.
            // ------------------------------------------------------------------

            private static MethodInfo Getter(PropertyInfo property)
            {
                return property != null ? property.GetGetMethod(true) : null;
            }

            /// <summary>Finds the single one-type-parameter generic method <paramref name="name"/> and closes it over <paramref name="argument"/>.</summary>
            private static MethodInfo CloseGeneric(Type declaring, string name, Type argument)
            {
                if (declaring == null || argument == null)
                {
                    return null;
                }
                foreach (MethodInfo m in declaring.GetMethods(GenericMethodFlags))
                {
                    if (m.Name == name && m.IsGenericMethodDefinition && m.GetGenericArguments().Length == 1)
                    {
                        return m.MakeGenericMethod(argument);
                    }
                }
                return null;
            }

            private static object Get(MethodInfo getter, object instance, string member)
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

            private static string GetString(MethodInfo getter, object instance, string member)
            {
                return Get(getter, instance, member) as string ?? "";
            }

            private static bool GetBool(MethodInfo getter, object instance, string member)
            {
                return Get(getter, instance, member) is bool value && value;
            }

            private static readonly HashSet<string> loggedFailures = new HashSet<string>();

            /// <summary>Reports a reflection call that threw, once per member for the session.</summary>
            private static void Fail(string member, Exception ex)
            {
                if (loggedFailures.Add(member))
                {
                    ModLogger.Error("CmrCompat.Logs." + member + " failed: " + ex.Message);
                }
            }
        }
    }
}
