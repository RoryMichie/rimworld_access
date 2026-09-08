using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    internal static partial class CmrCompat
    {
        /// <summary>
        /// Colony Manager Redux's Overview tab, as far as the tab's worker pawn table
        /// (<see cref="Shell.CmrOverviewDetails"/>, <see cref="Shell.CmrOverviewColumnHandlers"/>)
        /// reads it. Overview draws no job-specific controls of its own -- the row a job's own icon
        /// draws is a plain tab switch, and the table's cells are read-only current-activity text
        /// plus the work-priority cell the lead's shared <c>WorkPriorityColumnHandler</c> statics
        /// already back -- so this block is entirely read-only.
        ///
        /// <see cref="Table"/> and <see cref="WorkerWorkType"/> both answer null before the mod's own
        /// draw has run once: the table is built lazily, and the pawns-getter hijack that stamps a
        /// column worker's own <c>instance</c> field runs from inside that same draw
        /// (ManagerTab_Overview_PawnOverviewTable.cs:203-235). A null there is "no region yet", not a
        /// fault, and is never logged -- only a reflection call that actually throws is.
        /// </summary>
        internal static class Overview
        {
            private static readonly Type tabType;
            private static readonly Type workerBaseType;

            private static readonly FieldInfo pawnOverviewTableField;
            private static readonly MethodInfo workTypeGetter;
            private static readonly FieldInfo workerInstanceField;

            private static readonly bool ready;

            static Overview()
            {
                var surface = new ReflectionSurface("CmrCompat.Overview");

                tabType = surface.Type("ColonyManagerRedux.Managers.ManagerTab_Overview");
                workerBaseType =
                    surface.Type("ColonyManagerRedux.Managers.ManagerTab_Overview+PawnColumnWorker_Overview");

                pawnOverviewTableField = surface.Field(tabType, "pawnOverviewTable");
                workTypeGetter = Getter(surface.Property(tabType, "WorkTypeDef"));
                workerInstanceField = surface.Field(workerBaseType, "instance");

                ready = surface.Ready;
            }

            /// <summary>True when every member the Overview detail rows read resolved.</summary>
            public static bool Ready
            {
                get { return ready; }
            }

            /// <summary>Whether this tab is the mod's Overview tab, by the type resolved above.</summary>
            public static bool HandlesTab(object tab)
            {
                return ready && tab != null && tabType != null && tabType.IsInstanceOfType(tab);
            }

            /// <summary>The tab's own worker pawn table, or null before it has drawn once.</summary>
            public static PawnTable Table(object tab)
            {
                if (!ready || tab == null || pawnOverviewTableField == null)
                {
                    return null;
                }
                try
                {
                    return pawnOverviewTableField.GetValue(tab) as PawnTable;
                }
                catch (Exception ex)
                {
                    Fail("Table", ex);
                    return null;
                }
            }

            /// <summary>The work type the table's Current Activity/Work Priority columns are showing right now.</summary>
            public static WorkTypeDef CurrentWorkType(object tab)
            {
                if (!ready || tab == null || workTypeGetter == null)
                {
                    return null;
                }
                try
                {
                    return workTypeGetter.Invoke(tab, null) as WorkTypeDef;
                }
                catch (Exception ex)
                {
                    Fail("CurrentWorkType", ex);
                    return null;
                }
            }

            /// <summary>
            /// The work type behind an Overview column worker instance, read from the worker's own
            /// <c>instance</c> field (the tab the table's pawns-getter hijack stamps onto every column
            /// worker each draw). Null both when the facade declined and when the injection has not
            /// run yet for this worker -- the latter is ordinary for a column whose table has never
            /// drawn, so only an actual reflection failure is logged.
            /// </summary>
            public static WorkTypeDef WorkerWorkType(object workerInstance)
            {
                if (!ready || workerInstance == null || workerInstanceField == null
                    || !workerBaseType.IsInstanceOfType(workerInstance))
                {
                    return null;
                }
                object tab;
                try
                {
                    tab = workerInstanceField.GetValue(workerInstance);
                }
                catch (Exception ex)
                {
                    Fail("WorkerWorkType", ex);
                    return null;
                }
                return tab == null ? null : CurrentWorkType(tab);
            }

            // ------------------------------------------------------------------
            // Plumbing. Gated on this block's own Ready.
            // ------------------------------------------------------------------

            private static MethodInfo Getter(PropertyInfo property)
            {
                return property != null ? property.GetGetMethod(true) : null;
            }

            private static readonly HashSet<string> loggedFailures = new HashSet<string>();

            /// <summary>Reports a reflection call that threw, once per member for the session.</summary>
            private static void Fail(string member, Exception ex)
            {
                if (loggedFailures.Add(member))
                {
                    ModLogger.Error("CmrCompat.Overview." + member + " failed: " + ex.Message);
                }
            }
        }
    }
}
