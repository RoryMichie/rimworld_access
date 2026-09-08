#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// ShellDev's def field envelope reporter. For the ~80 def-editing fields going into the Character
    /// Editor mod compat layer, this answers "what values does this field actually take across every
    /// loaded def" empirically, instead of guessing a validation range from a decompile read: sample
    /// the field across the live def database and report its numeric range or its distinct-value
    /// distribution.
    /// </summary>
    public static partial class ShellDev
    {
        private struct DefEnvelopePathSegment
        {
            public string Name;
            public bool HasIndex;
            public int Index;
        }

        /// <summary>
        /// Empirically samples <paramref name="fieldPath"/> (dot-separated field names, a segment
        /// may carry a single index like <c>verbs[0]</c>) across every loaded def of
        /// <paramref name="defTypeFullName"/>. Numeric fields (int, float, double, enum) report
        /// count/min/max/mean plus the 5 most common values; everything else reports up to 20
        /// distinct values sorted by count. A def is skipped (and counted) when any segment
        /// resolves to null or an out-of-range index, and skipped under a separate counter when a
        /// segment's field does not exist on that def's runtime type (legitimate for polymorphic
        /// chains like comps whose subclass varies per def). Only when the field resolves on NO
        /// sampled def at all is it a hard error, since that almost always means a typo in
        /// <paramref name="fieldPath"/> rather than per-def variance.
        /// </summary>
        public static string FieldEnvelope(string defTypeFullName, string fieldPath)
        {
            Type defType = Type.GetType(defTypeFullName);
            if (defType == null)
            {
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    defType = asm.GetType(defTypeFullName);
                    if (defType != null)
                    {
                        break;
                    }
                }
            }
            if (defType == null)
            {
                return "type not found: " + defTypeFullName; // l10n-exempt: dev-bridge-only diagnostic text, never player-facing.
            }
            if (!typeof(Def).IsAssignableFrom(defType))
            {
                return "type not a Def: " + defTypeFullName; // l10n-exempt: dev-bridge-only diagnostic text, never player-facing.
            }

            List<Def> allDefs;
            try
            {
                allDefs = GenDefDatabase.GetAllDefsInDatabaseForDef(defType).ToList();
            }
            catch (Exception ex)
            {
                return "failed to enumerate defs for " + defTypeFullName + ": " + ex.GetType().Name + ": " + ex.Message; // l10n-exempt: dev-bridge-only diagnostic text, never player-facing.
            }
            if (allDefs.Count == 0)
            {
                return "no defs in database for " + defTypeFullName; // l10n-exempt: dev-bridge-only diagnostic text, never player-facing.
            }

            List<DefEnvelopePathSegment> segments = ParseDefEnvelopePath(fieldPath);
            if (segments.Count == 0)
            {
                return "empty field path"; // l10n-exempt: dev-bridge-only diagnostic text, never player-facing.
            }

            var values = new List<object>();
            int skipped = 0;
            int fieldMissing = 0;
            string firstMissingDescription = null;
            foreach (Def def in allDefs)
            {
                object current = def;
                bool skip = false;
                for (int i = 0; i < segments.Count; i++)
                {
                    if (current == null)
                    {
                        skip = true;
                        break;
                    }
                    DefEnvelopePathSegment seg = segments[i];
                    FieldInfo field = FindDefEnvelopeField(current.GetType(), seg.Name);
                    if (field == null)
                    {
                        fieldMissing++;
                        if (firstMissingDescription == null)
                        {
                            firstMissingDescription = "'" + seg.Name + "' on " + current.GetType().FullName;
                        }
                        skip = true;
                        break;
                    }
                    object value = field.GetValue(current);
                    if (seg.HasIndex)
                    {
                        IList list = value as IList;
                        if (list == null || seg.Index < 0 || seg.Index >= list.Count)
                        {
                            skip = true;
                            break;
                        }
                        value = list[seg.Index];
                    }
                    current = value;
                }

                if (skip || current == null)
                {
                    skipped++;
                    continue;
                }
                values.Add(current);
            }

            if (values.Count == 0 && fieldMissing > 0)
            {
                return "field segment not found on any sampled def: " + firstMissingDescription; // l10n-exempt: dev-bridge-only diagnostic text, never player-facing.
            }
            if (values.Count == 0)
            {
                return "count sampled: 0\ncount skipped: " + skipped; // l10n-exempt: dev-bridge-only diagnostic text, never player-facing.
            }

            bool numeric = values.All(IsDefEnvelopeNumeric);
            string report = numeric
                ? BuildDefEnvelopeNumericReport(values, skipped)
                : BuildDefEnvelopeDistinctReport(values, skipped);
            if (fieldMissing > 0)
            {
                report = "count field-missing: " + fieldMissing + " (first: " + firstMissingDescription + ")\n" + report;
            }
            return report;
        }

        private static List<DefEnvelopePathSegment> ParseDefEnvelopePath(string fieldPath)
        {
            var segments = new List<DefEnvelopePathSegment>();
            if (string.IsNullOrEmpty(fieldPath))
            {
                return segments;
            }
            string[] parts = fieldPath.Split('.');
            foreach (string part in parts)
            {
                string name = part;
                bool hasIndex = false;
                int index = -1;
                int bracket = part.IndexOf('[');
                if (bracket >= 0 && part.EndsWith("]", StringComparison.Ordinal))
                {
                    name = part.Substring(0, bracket);
                    string idxText = part.Substring(bracket + 1, part.Length - bracket - 2);
                    if (int.TryParse(idxText, NumberStyles.Integer, CultureInfo.InvariantCulture, out index))
                    {
                        hasIndex = true;
                    }
                }
                var seg = new DefEnvelopePathSegment();
                seg.Name = name;
                seg.HasIndex = hasIndex;
                seg.Index = index;
                segments.Add(seg);
            }
            return segments;
        }

        private static FieldInfo FindDefEnvelopeField(Type type, string name)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            for (Type t = type; t != null; t = t.BaseType)
            {
                FieldInfo f = t.GetField(name, flags);
                if (f != null)
                {
                    return f;
                }
            }
            return null;
        }

        private static bool IsDefEnvelopeNumeric(object value)
        {
            if (value == null)
            {
                return false;
            }
            Type t = value.GetType();
            return t.IsEnum || t == typeof(int) || t == typeof(float) || t == typeof(double);
        }

        private static string BuildDefEnvelopeNumericReport(List<object> values, int skipped)
        {
            double[] doubles = values.Select(v => Convert.ToDouble(v, CultureInfo.InvariantCulture)).ToArray();
            double min = doubles.Min();
            double max = doubles.Max();
            double mean = doubles.Average();

            var topValues = values.GroupBy(v => v.ToString())
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key, StringComparer.Ordinal)
                .Take(5);

            var sb = new StringBuilder();
            sb.Append("count sampled: ").Append(values.Count).Append('\n');
            sb.Append("count skipped: ").Append(skipped).Append('\n');
            sb.Append("min: ").Append(min.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("max: ").Append(max.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("mean: ").Append(mean.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("top values:");
            foreach (IGrouping<string, object> g in topValues)
            {
                sb.Append("\n  ").Append(g.Key).Append(": ").Append(g.Count());
            }
            return sb.ToString();
        }

        private static string BuildDefEnvelopeDistinctReport(List<object> values, int skipped)
        {
            var distinct = values.GroupBy(DescribeDefEnvelopeValue)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key, StringComparer.Ordinal)
                .Take(20);

            var sb = new StringBuilder();
            sb.Append("count sampled: ").Append(values.Count).Append('\n');
            sb.Append("count skipped: ").Append(skipped).Append('\n');
            sb.Append("distinct values:");
            foreach (IGrouping<string, object> g in distinct)
            {
                sb.Append("\n  ").Append(g.Key).Append(": ").Append(g.Count());
            }
            return sb.ToString();
        }

        private static string DescribeDefEnvelopeValue(object value)
        {
            Def asDef = value as Def;
            return asDef != null ? asDef.defName : value.ToString();
        }
    }
}
#endif
