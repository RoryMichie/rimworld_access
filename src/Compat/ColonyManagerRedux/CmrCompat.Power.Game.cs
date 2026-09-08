using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Verse;

namespace RimWorldAccess
{
    internal static partial class CmrCompat
    {
        /// <summary>
        /// Colony Manager Redux's Power tab, its job's two <c>History</c> instances (overall and
        /// trading), and the <c>History.Chapter</c> entries their legends walk
        /// (<see cref="Shell.CmrPowerDetails"/>). The graphs themselves are excluded: a line chart
        /// has no non-visual reading, so only their legend rows and the period control are read
        /// here. <c>History</c> and
        /// its members are public, but <c>Chapter</c> is an internal type nested inside a second
        /// partial declaration of <c>History</c> itself (<c>History+Chapter</c>), so every Chapter
        /// reader below is reflected even though the surrounding type is not.
        ///
        /// <c>ThingDefCountClass</c> (a Chapter's own <see cref="ChapterThingDef"/> holder) is a
        /// vanilla <c>Verse</c> type, so once its instance is read off the mod's field it is cast
        /// directly with no further reflection.
        ///
        /// MUTATION VEHICLES. <see cref="SetPeriodShown"/> is the property's own setter -- doctrine
        /// A, the exact write the period float-menu's option body performs on both histories
        /// (ManagerTab_Power.cs:261-266); the caller is expected to call it twice, once per history,
        /// the same way that option body does. Every other member here is read-only.
        /// </summary>
        internal static class Power
        {
            private const BindingFlags GenericMethodFlags =
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

            private static readonly Type tabType;
            private static readonly Type jobType;
            private static readonly Type managerJobType;
            private static readonly Type compHistoryType;
            private static readonly Type historyType;
            private static readonly Type chapterType;
            private static readonly Type historyLabelType;
            private static readonly Type periodType;
            private static readonly Type utilsType;

            private static readonly FieldInfo tradingHistoryField;
            private static readonly MethodInfo compOfTypeHistoryMethod;
            private static readonly MethodInfo historyGetter;

            private static readonly FieldInfo periodsField;
            private static readonly MethodInfo periodShownGetter;
            private static readonly MethodInfo periodShownSetter;
            private static readonly MethodInfo yAxisSuffixGetter;
            private static readonly FieldInfo chaptersField;

            private static readonly FieldInfo chapterLabelField;
            private static readonly MethodInfo historyLabelGetter;
            private static readonly MethodInfo chapterLastMethod;
            private static readonly MethodInfo chapterTrueMaxGetter;
            private static readonly MethodInfo chapterSuffixGetter;
            private static readonly FieldInfo chapterThingDefCountField;
            private static readonly FieldInfo chapterCountsField;

            private static readonly MethodInfo formatCountMethod;

            private static readonly bool ready;

            static Power()
            {
                var surface = new ReflectionSurface("CmrCompat.Power");

                tabType = surface.Type("ColonyManagerRedux.Managers.ManagerTab_Power");
                jobType = surface.Type("ColonyManagerRedux.Managers.ManagerJob_Power");
                managerJobType = surface.Type("ColonyManagerRedux.ManagerJob");
                compHistoryType = surface.Type("ColonyManagerRedux.CompManagerJobHistory");
                historyType = surface.Type("ColonyManagerRedux.History");
                chapterType = surface.Type("ColonyManagerRedux.History+Chapter");
                historyLabelType = surface.Type("ColonyManagerRedux.HistoryLabel");
                periodType = surface.Type("ColonyManagerRedux.Period");
                utilsType = surface.Type("ilyvion.Laboratory.Utils");

                tradingHistoryField = surface.Field(jobType, "tradingHistory");
                if (managerJobType != null && compHistoryType != null)
                {
                    compOfTypeHistoryMethod = surface.Required("ManagerJob.CompOfType<CompManagerJobHistory>() closed",
                        CloseGeneric(managerJobType, "CompOfType", compHistoryType));
                }
                historyGetter = Getter(surface.Property(compHistoryType, "History"));

                periodsField = surface.Field(historyType, "Periods");
                PropertyInfo periodShownProperty = surface.Property(historyType, "PeriodShown");
                periodShownGetter = Getter(periodShownProperty);
                periodShownSetter = surface.Required("History.PeriodShown setter", Setter(periodShownProperty));
                yAxisSuffixGetter = Getter(surface.Property(historyType, "YAxisSuffix"));
                chaptersField = surface.Field(historyType, "_chapters");

                chapterLabelField = surface.Field(chapterType, "label");
                historyLabelGetter = Getter(surface.Property(historyLabelType, "Label"));
                if (periodType != null)
                {
                    chapterLastMethod = surface.Method(chapterType, "Last", new[] { periodType });
                }
                chapterTrueMaxGetter = Getter(surface.Property(chapterType, "TrueMax"));
                chapterSuffixGetter = Getter(surface.Property(chapterType, "ChapterSuffix"));
                chapterThingDefCountField = surface.Field(chapterType, "ThingDefCount");
                chapterCountsField = surface.Field(chapterType, "counts");

                formatCountMethod = surface.Method(utilsType, "FormatCount",
                    new[] { typeof(float), typeof(string), typeof(int), typeof(string[]) });

                ready = surface.Ready;
            }

            /// <summary>True when every member the Power detail rows read resolved.</summary>
            public static bool Ready
            {
                get { return ready; }
            }

            /// <summary>Whether this tab is the mod's Power tab, by the type resolved above.</summary>
            public static bool HandlesTab(object tab)
            {
                return ready && tab != null && tabType != null && tabType.IsInstanceOfType(tab);
            }

            // ------------------------------------------------------------------
            // The job's two histories.
            // ------------------------------------------------------------------

            /// <summary>The job's trading (consumption/production) history (ManagerJob_Power.cs:204).</summary>
            public static object TradingHistory(object job)
            {
                return FieldValue(tradingHistoryField, job, "TradingHistory");
            }

            /// <summary>The job's overall history, from its own history comp (ManagerTab_Power.cs:201).</summary>
            public static object OverallHistory(object job)
            {
                if (!ready || job == null || compOfTypeHistoryMethod == null)
                {
                    return null;
                }
                object comp;
                try
                {
                    comp = compOfTypeHistoryMethod.Invoke(job, null);
                }
                catch (Exception ex)
                {
                    Fail("OverallHistory", ex);
                    return null;
                }
                return comp == null ? null : Get(historyGetter, comp, "OverallHistory");
            }

            // ------------------------------------------------------------------
            // Period.
            // ------------------------------------------------------------------

            /// <summary>How many periods the mod's own <c>Period</c> enum offers (History.cs:24).</summary>
            public static int PeriodCount()
            {
                if (!ready || periodsField == null)
                {
                    return 0;
                }
                try
                {
                    return periodsField.GetValue(null) is Array periods ? periods.Length : 0;
                }
                catch (Exception ex)
                {
                    Fail("PeriodCount", ex);
                    return 0;
                }
            }

            /// <summary>The mod's own enum member name for a period index, for the provider's own label key.</summary>
            public static string PeriodName(int index)
            {
                if (!ready || periodType == null)
                {
                    return "";
                }
                try
                {
                    return Enum.GetName(periodType, index) ?? "";
                }
                catch (Exception ex)
                {
                    Fail("PeriodName", ex);
                    return "";
                }
            }

            public static int PeriodShown(object history)
            {
                object value = Get(periodShownGetter, history, "PeriodShown");
                if (value == null)
                {
                    return 0;
                }
                try
                {
                    return Convert.ToInt32(value, CultureInfo.InvariantCulture);
                }
                catch (Exception ex)
                {
                    Fail("PeriodShown", ex);
                    return 0;
                }
            }

            /// <summary>The period float-menu option's own write (ManagerTab_Power.cs:261-266) -- the property's own setter, doctrine A. Call once per history.</summary>
            public static void SetPeriodShown(object history, int index)
            {
                if (!ready || history == null || periodShownSetter == null || periodType == null)
                {
                    return;
                }
                try
                {
                    object value = Enum.ToObject(periodType, index);
                    periodShownSetter.Invoke(history, new[] { value });
                }
                catch (Exception ex)
                {
                    Fail("SetPeriodShown", ex);
                }
            }

            public static string YAxisSuffix(object history)
            {
                return Get(yAxisSuffixGetter, history, "YAxisSuffix") as string ?? "";
            }

            /// <summary>The history's own chapters, in its own storage order (History.cs:100).</summary>
            public static List<object> Chapters(object history)
            {
                var result = new List<object>();
                var items = FieldValue(chaptersField, history, "Chapters") as IEnumerable;
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
                    Fail("Chapters", ex);
                }
                return result;
            }

            // ------------------------------------------------------------------
            // Chapter readers.
            // ------------------------------------------------------------------

            public static string ChapterLabel(object chapter)
            {
                object label = FieldValue(chapterLabelField, chapter, "ChapterLabel");
                return label == null ? "" : Get(historyLabelGetter, label, "ChapterLabel") as string ?? "";
            }

            /// <summary>The chapter's own last-recorded count for a period (Chapter.cs:187); extracts the boxed tuple's first element.</summary>
            public static int ChapterLastCount(object chapter, int periodIndex)
            {
                if (!ready || chapter == null || chapterLastMethod == null || periodType == null)
                {
                    return 0;
                }
                try
                {
                    object period = Enum.ToObject(periodType, periodIndex);
                    object boxed = chapterLastMethod.Invoke(chapter, new[] { period });
                    if (boxed == null)
                    {
                        return 0;
                    }
                    Type tupleType = boxed.GetType();
                    FieldInfo item1 = tupleType.GetField("Item1");
                    return item1 != null && item1.GetValue(boxed) is int count ? count : 0;
                }
                catch (Exception ex)
                {
                    Fail("ChapterLastCount", ex);
                    return 0;
                }
            }

            public static int ChapterTrueMax(object chapter)
            {
                object value = Get(chapterTrueMaxGetter, chapter, "ChapterTrueMax");
                return value is int max ? max : 0;
            }

            public static string ChapterSuffix(object chapter)
            {
                return Get(chapterSuffixGetter, chapter, "ChapterSuffix") as string;
            }

            /// <summary>The per-building def a trading chapter tracks, or null for a translation-label overall chapter.</summary>
            public static ThingDef ChapterThingDef(object chapter)
            {
                object holder = FieldValue(chapterThingDefCountField, chapter, "ChapterThingDef");
                return (holder as ThingDefCountClass)?.thingDef;
            }

            /// <summary>
            /// True when any of the chapter's per-period counts times <paramref name="sign"/> is
            /// positive -- DetailedLegendRenderer.cs:102-107's positiveOnly/negativeOnly filter
            /// verbatim (sign = +1 tests "any count &gt; 0", sign = -1 tests "any count &lt; 0").
            /// </summary>
            public static bool ChapterHasSign(object chapter, int periodIndex, int sign)
            {
                if (!ready || chapter == null || chapterCountsField == null)
                {
                    return false;
                }
                try
                {
                    var buffers = chapterCountsField.GetValue(chapter) as Array;
                    if (buffers == null || periodIndex < 0 || periodIndex >= buffers.Length)
                    {
                        return false;
                    }
                    var counts = buffers.GetValue(periodIndex) as IEnumerable;
                    if (counts == null)
                    {
                        return false;
                    }
                    foreach (object value in counts)
                    {
                        if (value is int count && count * sign > 0)
                        {
                            return true;
                        }
                    }
                    return false;
                }
                catch (Exception ex)
                {
                    Fail("ChapterHasSign", ex);
                    return false;
                }
            }

            // ------------------------------------------------------------------
            // Count formatting.
            // ------------------------------------------------------------------

            /// <summary>
            /// The legend's own count formatting (ilyvion.Laboratory.Utils.FormatCount,
            /// Utils.cs:25-42); every optional parameter is passed explicitly since reflection
            /// invocation ignores C# default-parameter metadata. Falls back to a plain
            /// number-plus-suffix string on decline so the row still reads.
            /// </summary>
            public static string FormatCount(float value, string suffix)
            {
                if (ready && formatCountMethod != null)
                {
                    try
                    {
                        return formatCountMethod.Invoke(null,
                            new object[] { value, suffix, 1000, null }) as string;
                    }
                    catch (Exception ex)
                    {
                        Fail("FormatCount", ex);
                    }
                }
                return value.ToString("0.#", CultureInfo.InvariantCulture) + " " + suffix;
            }

            // ------------------------------------------------------------------
            // Plumbing. Gated on this block's own Ready.
            // ------------------------------------------------------------------

            private static MethodInfo Getter(PropertyInfo property)
            {
                return property != null ? property.GetGetMethod(true) : null;
            }

            private static MethodInfo Setter(PropertyInfo property)
            {
                return property != null ? property.GetSetMethod(true) : null;
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

            private static object FieldValue(FieldInfo field, object instance, string member)
            {
                if (!ready || field == null || instance == null)
                {
                    return null;
                }
                try
                {
                    return field.GetValue(instance);
                }
                catch (Exception ex)
                {
                    Fail(member, ex);
                    return null;
                }
            }

            private static readonly HashSet<string> loggedFailures = new HashSet<string>();

            /// <summary>Reports a reflection call that threw, once per member for the session.</summary>
            private static void Fail(string member, Exception ex)
            {
                if (loggedFailures.Add(member))
                {
                    ModLogger.Error("CmrCompat.Power." + member + " failed: " + ex.Message);
                }
            }
        }
    }
}
