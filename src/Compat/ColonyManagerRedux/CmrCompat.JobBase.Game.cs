using System;
using System.Collections.Generic;
using System.Reflection;
using Verse;

namespace RimWorldAccess
{
    internal static partial class CmrCompat
    {
        /// <summary>
        /// Colony Manager Redux's <c>ManagerJob</c> base members every job type shares: the map a
        /// job's manager lives on, and the reachability / path-based-distance toggles every tab's own
        /// <c>Utilities</c> helper writes by ref. Its own block for the same reason
        /// <see cref="Hunting"/> and the other per-tab facades keep theirs: a rename here must not
        /// disturb the manager window's tab and job lists or any tab's own detail rows -- and, since
        /// every job-type facade used to bind these three itself against the very same base type,
        /// collapsing them here is what let Hunting/Forestry/Foraging/Mining each drop their own copy.
        ///
        /// MUTATION VEHICLES.
        /// <list type="bullet">
        /// <item><see cref="SetShouldCheckReachable"/> and <see cref="SetUsePathBasedDistance"/> write
        /// the private field each base property returns a <c>ref bool</c> to
        /// (<c>_shouldCheckReachable</c>, ManagerJob.cs:133/138; <c>_usePathBasedDistance</c>,
        /// ManagerJob.cs:168/173) -- the storage every tab's own toggle passes by ref
        /// (<c>Utilities.DrawReachabilityToggle</c> / <c>Utilities.DrawToggle</c>, Utilities.cs:253-265;
        /// e.g. ManagerTab_Hunting.cs:506-514) -- through <see cref="SetField"/>: doctrine C, since a
        /// by-ref-returning getter is not reliably invokable through reflection and no setter exists to
        /// invoke instead.</item>
        /// </list>
        /// </summary>
        internal static class JobBase
        {
            private static readonly MethodInfo managerGetter;
            private static readonly FieldInfo shouldCheckReachableField;
            private static readonly FieldInfo usePathBasedDistanceField;

            private static readonly bool ready;

            static JobBase()
            {
                var surface = new ReflectionSurface("CmrCompat.JobBase");

                Type baseJobType = surface.Type("ColonyManagerRedux.ManagerJob");

                managerGetter = Getter(surface.Property(baseJobType, "Manager"));
                shouldCheckReachableField = surface.Field(baseJobType, "_shouldCheckReachable");
                usePathBasedDistanceField = surface.Field(baseJobType, "_usePathBasedDistance");

                ready = surface.Ready;
            }

            /// <summary>True when every member this shared job-base slice reads or writes resolved.</summary>
            public static bool Ready
            {
                get { return ready; }
            }

            /// <summary>The map the job manages, reached the way every tab reaches it: the job's Manager property, then that manager's map.</summary>
            public static Map MapOf(object job)
            {
                var manager = Get(managerGetter, job, "MapOf") as MapComponent;
                return manager == null ? null : manager.map;
            }

            public static bool ShouldCheckReachable(object job)
            {
                return FieldFlag(shouldCheckReachableField, job, "ShouldCheckReachable");
            }

            /// <summary>Writes the storage every tab's own Utilities.DrawReachabilityToggle passes by ref (Utilities.cs:253-265; ManagerJob.cs:133/138).</summary>
            public static void SetShouldCheckReachable(object job, bool value)
            {
                SetField(shouldCheckReachableField, job, value, "SetShouldCheckReachable");
            }

            public static bool UsePathBasedDistance(object job)
            {
                return FieldFlag(usePathBasedDistanceField, job, "UsePathBasedDistance");
            }

            /// <summary>Writes the storage every tab's own path-based-distance toggle passes by ref (e.g. ManagerTab_Hunting.cs:506-513; ManagerJob.cs:168/173).</summary>
            public static void SetUsePathBasedDistance(object job, bool value)
            {
                SetField(usePathBasedDistanceField, job, value, "SetUsePathBasedDistance");
            }

            // ------------------------------------------------------------------
            // Plumbing. Gated on this block's own Ready.
            // ------------------------------------------------------------------

            private static MethodInfo Getter(PropertyInfo property)
            {
                return property != null ? property.GetGetMethod(true) : null;
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

            private static bool FieldFlag(FieldInfo field, object instance, string member)
            {
                if (!ready || field == null || instance == null)
                {
                    return false;
                }
                try
                {
                    return field.GetValue(instance) is bool value && value;
                }
                catch (Exception ex)
                {
                    Fail(member, ex);
                    return false;
                }
            }

            /// <summary>
            /// Shared write primitive for the ref-bool-property backing field the mod's own toggle
            /// widgets write through their ref parameter. Each public wrapper above carries the
            /// citation for the draw call it reproduces.
            /// </summary>
            private static void SetField(FieldInfo field, object instance, object value, string member)
            {
                if (!ready || field == null || instance == null)
                {
                    return;
                }
                try
                {
                    // MUTATION-C: the write the mod's own toggle performs through its ref parameter --
                    // see this class's remarks and each wrapper's cited draw call. Neither backing
                    // field has a gated setter to invoke instead.
                    field.SetValue(instance, value);
                }
                catch (Exception ex)
                {
                    Fail(member, ex);
                }
            }

            private static readonly HashSet<string> loggedFailures = new HashSet<string>();

            /// <summary>Reports a reflection call that threw, once per member for the session.</summary>
            private static void Fail(string member, Exception ex)
            {
                if (loggedFailures.Add(member))
                {
                    ModLogger.Error("CmrCompat.JobBase." + member + " failed: " + ex.Message);
                }
            }
        }
    }
}
