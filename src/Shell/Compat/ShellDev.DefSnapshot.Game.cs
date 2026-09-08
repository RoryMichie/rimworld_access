#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// ShellDev's def-safety snapshot/diff surface: a stable, deterministic text dump of a def's entire
    /// field state, and a line-set diff between two such dumps. Built so bridge scripts can snapshot a
    /// def before a mutation, snapshot it again after, and get a readable report of exactly what
    /// changed instead of trusting the mutation did what it claimed.
    /// </summary>
    public static partial class ShellDev
    {
        private const int DefSnapshotMaxDepth = 8;
        private const int DefSnapshotMaxCollection = 512;
        private const int DefSnapshotMaxLines = 100000;

        private static readonly Type[] DefSnapshotUndumpableUnityTypes =
        {
            typeof(Texture), typeof(Material), typeof(GameObject), typeof(Shader), typeof(AudioClip), typeof(Mesh)
        };

        /// <summary>
        /// Walks every instance field of <paramref name="def"/> (public and non-public, declared
        /// and inherited) by reflection and returns a deterministic text dump: a header line
        /// <c>def &lt;TypeFullName&gt; &lt;defName&gt;</c> followed by one sorted leaf line per
        /// path, <c>path = value</c>. Referenced defs are printed as <c>Def:&lt;defName&gt;</c> and
        /// never walked into (only the root def's own insides are walked); a reference back to an
        /// ANCESTOR on the current walk path prints <c>&lt;cycle&gt;</c>, while an object shared
        /// between sibling paths is dumped fully under each path so no mutation can hide behind a
        /// cycle marker; recursion past depth 8 prints <c>&lt;depth-cap&gt;</c>; collections past
        /// 512 elements print a trailing <c>&lt;truncated N&gt;</c> line; a walk past the global
        /// line budget emits one <c>&lt;line-budget-exceeded&gt;</c> sentinel and stops.
        /// </summary>
        public static string SnapshotDef(Def def)
        {
            if (def == null)
            {
                return "def is null"; // l10n-exempt: dev-bridge-only diagnostic text, never player-facing.
            }

            var lines = new List<string>();
            var visited = new HashSet<object>(DefSnapshotReferenceComparer.Instance);
            visited.Add(def);
            WalkDefSnapshotObject(def, "", 0, visited, lines);
            lines.Sort(CompareDefSnapshotLinesByPath);

            var sb = new StringBuilder();
            sb.Append("def ").Append(def.GetType().FullName).Append(' ').Append(def.defName);
            for (int i = 0; i < lines.Count; i++)
            {
                sb.Append('\n').Append(lines[i]);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Line-set diff of two <see cref="SnapshotDef"/> outputs, keyed on the path (the text
        /// before the first <c> = </c> on each line; the header line has no such separator and is
        /// excluded from the comparison). Reports changed paths, then added lines, then removed
        /// lines, then a summary count. Returns <c>IDENTICAL</c> if nothing differs.
        /// </summary>
        public static string DiffSnapshots(string before, string after)
        {
            Dictionary<string, string> beforeMap = ParseDefSnapshotLines(before);
            Dictionary<string, string> afterMap = ParseDefSnapshotLines(after);

            var changed = new List<string>();
            var removed = new List<string>();
            foreach (KeyValuePair<string, string> kv in beforeMap)
            {
                string afterLine;
                if (!afterMap.TryGetValue(kv.Key, out afterLine))
                {
                    removed.Add("removed: " + kv.Value);
                    continue;
                }
                if (afterLine != kv.Value)
                {
                    string oldValue = ValueAfterEquals(kv.Value);
                    string newValue = ValueAfterEquals(afterLine);
                    changed.Add("changed: " + kv.Key + " | " + oldValue + " -> " + newValue);
                }
            }

            var added = new List<string>();
            foreach (KeyValuePair<string, string> kv in afterMap)
            {
                if (!beforeMap.ContainsKey(kv.Key))
                {
                    added.Add("added: " + kv.Value);
                }
            }

            if (changed.Count == 0 && added.Count == 0 && removed.Count == 0)
            {
                return "IDENTICAL";
            }

            changed.Sort(StringComparer.Ordinal);
            added.Sort(StringComparer.Ordinal);
            removed.Sort(StringComparer.Ordinal);

            var sb = new StringBuilder();
            for (int i = 0; i < changed.Count; i++)
            {
                sb.Append(changed[i]).Append('\n');
            }
            for (int i = 0; i < added.Count; i++)
            {
                sb.Append(added[i]).Append('\n');
            }
            for (int i = 0; i < removed.Count; i++)
            {
                sb.Append(removed[i]).Append('\n');
            }
            sb.Append(changed.Count).Append(" changed, ").Append(added.Count).Append(" added, ").Append(removed.Count).Append(" removed");
            return sb.ToString();
        }

        private static string ValueAfterEquals(string line)
        {
            int sep = line.IndexOf(" = ", StringComparison.Ordinal);
            return sep < 0 ? line : line.Substring(sep + 3);
        }

        private static Dictionary<string, string> ParseDefSnapshotLines(string dump)
        {
            var map = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(dump))
            {
                return map;
            }
            string[] rawLines = dump.Split('\n');
            foreach (string rawLine in rawLines)
            {
                int sep = rawLine.IndexOf(" = ", StringComparison.Ordinal);
                if (sep < 0)
                {
                    continue;
                }
                string path = rawLine.Substring(0, sep);
                map[path] = rawLine;
            }
            return map;
        }

        private static int CompareDefSnapshotLinesByPath(string a, string b)
        {
            string pathA = ValueBeforeEquals(a);
            string pathB = ValueBeforeEquals(b);
            return string.CompareOrdinal(pathA, pathB);
        }

        private static string ValueBeforeEquals(string line)
        {
            int sep = line.IndexOf(" = ", StringComparison.Ordinal);
            return sep < 0 ? line : line.Substring(0, sep);
        }

        private static void WalkDefSnapshotObject(object obj, string pathPrefix, int depth, HashSet<object> visited, List<string> lines)
        {
            foreach (FieldInfo field in GetAllDefSnapshotInstanceFields(obj.GetType()))
            {
                string path = pathPrefix.Length == 0 ? field.Name : pathPrefix + "." + field.Name;
                object value;
                try
                {
                    value = field.GetValue(obj);
                }
                catch (Exception ex)
                {
                    lines.Add(path + " = <unreadable:" + ex.GetType().Name + ">");
                    continue;
                }
                AppendDefSnapshotValue(value, path, depth, visited, lines);
            }
        }

        private static readonly Dictionary<Type, List<FieldInfo>> defSnapshotFieldCache = new Dictionary<Type, List<FieldInfo>>();

        private static List<FieldInfo> GetAllDefSnapshotInstanceFields(Type type)
        {
            List<FieldInfo> cached;
            if (defSnapshotFieldCache.TryGetValue(type, out cached))
            {
                return cached;
            }

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            var seenNames = new HashSet<string>();
            var result = new List<FieldInfo>();
            for (Type t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                foreach (FieldInfo f in t.GetFields(flags))
                {
                    if (seenNames.Add(f.Name))
                    {
                        result.Add(f);
                    }
                }
            }
            defSnapshotFieldCache[type] = result;
            return result;
        }

        private static void AppendDefSnapshotValue(object value, string path, int depth, HashSet<object> visited, List<string> lines)
        {
            if (lines.Count >= DefSnapshotMaxLines)
            {
                if (lines.Count == DefSnapshotMaxLines)
                {
                    lines.Add("<line-budget-exceeded> = true");
                }
                return;
            }
            if (value == null)
            {
                lines.Add(path + " = null");
                return;
            }

            Type valueType = value.GetType();

            if (value is Def)
            {
                lines.Add(path + " = Def:" + ((Def)value).defName);
                return;
            }
            if (value is string)
            {
                lines.Add(path + " = \"" + ((string)value).Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"");
                return;
            }
            if (value is Type)
            {
                lines.Add(path + " = " + ((Type)value).FullName);
                return;
            }
            if (value is Delegate)
            {
                lines.Add(path + " = <delegate>");
                return;
            }
            if (IsDefSnapshotUndumpableUnityType(valueType))
            {
                lines.Add(path + " = <unity:" + valueType.Name + ">");
                return;
            }
            if (valueType.IsEnum || value is bool)
            {
                lines.Add(path + " = " + value.ToString());
                return;
            }
            if (value is float)
            {
                lines.Add(path + " = " + ((float)value).ToString("R", CultureInfo.InvariantCulture));
                return;
            }
            if (value is double)
            {
                lines.Add(path + " = " + ((double)value).ToString("R", CultureInfo.InvariantCulture));
                return;
            }
            if (valueType.IsPrimitive || value is decimal)
            {
                string formatted;
                try
                {
                    formatted = Convert.ToString(value, CultureInfo.InvariantCulture);
                }
                catch (Exception)
                {
                    formatted = value.ToString();
                }
                lines.Add(path + " = " + formatted);
                return;
            }
            if (value is IDictionary)
            {
                AppendDefSnapshotDictionary((IDictionary)value, path, depth, visited, lines);
                return;
            }
            if (value is IEnumerable)
            {
                AppendDefSnapshotEnumerable((IEnumerable)value, path, depth, visited, lines);
                return;
            }

            // Complex reference/value object: recurse into its own fields.
            if (!valueType.IsValueType)
            {
                if (visited.Contains(value))
                {
                    lines.Add(path + " = <cycle>");
                    return;
                }
                if (depth + 1 > DefSnapshotMaxDepth)
                {
                    lines.Add(path + " = <depth-cap>");
                    return;
                }
                // Ancestor-stack semantics: remove after the subtree finishes so an object shared
                // between sibling paths is dumped under each path, not masked as a cycle. Only a
                // genuine ancestor reference (a true cycle) still prints <cycle>.
                visited.Add(value);
                WalkDefSnapshotObject(value, path, depth + 1, visited, lines);
                visited.Remove(value);
            }
            else
            {
                if (depth + 1 > DefSnapshotMaxDepth)
                {
                    lines.Add(path + " = <depth-cap>");
                    return;
                }
                WalkDefSnapshotObject(value, path, depth + 1, visited, lines);
            }
        }

        private static void AppendDefSnapshotDictionary(IDictionary dict, string path, int depth, HashSet<object> visited, List<string> lines)
        {
            var keys = new List<string>();
            var keyLookup = new Dictionary<string, object>();
            foreach (object key in dict.Keys)
            {
                string keyText = key == null ? "null" : key.ToString();
                keys.Add(keyText);
                keyLookup[keyText] = key;
            }
            keys.Sort(StringComparer.Ordinal);

            int emitted = 0;
            foreach (string keyText in keys)
            {
                if (emitted >= DefSnapshotMaxCollection)
                {
                    lines.Add(path + " = <truncated " + keys.Count + ">");
                    return;
                }
                object entryValue = dict[keyLookup[keyText]];
                AppendDefSnapshotValue(entryValue, path + "[" + keyText + "]", depth + 1, visited, lines);
                emitted++;
            }
        }

        private static void AppendDefSnapshotEnumerable(IEnumerable enumerable, string path, int depth, HashSet<object> visited, List<string> lines)
        {
            IList list = enumerable as IList;
            if (list == null)
            {
                var materialized = new List<object>();
                foreach (object item in enumerable)
                {
                    materialized.Add(item);
                }
                list = materialized;
            }

            int count = list.Count;
            int emitCount = Math.Min(count, DefSnapshotMaxCollection);
            for (int i = 0; i < emitCount; i++)
            {
                AppendDefSnapshotValue(list[i], path + "[" + i.ToString(CultureInfo.InvariantCulture) + "]", depth + 1, visited, lines);
            }
            if (count > DefSnapshotMaxCollection)
            {
                lines.Add(path + " = <truncated " + count + ">");
            }
        }

        private static bool IsDefSnapshotUndumpableUnityType(Type type)
        {
            for (int i = 0; i < DefSnapshotUndumpableUnityTypes.Length; i++)
            {
                if (DefSnapshotUndumpableUnityTypes[i].IsAssignableFrom(type))
                {
                    return true;
                }
            }
            return false;
        }

        private sealed class DefSnapshotReferenceComparer : IEqualityComparer<object>
        {
            public static readonly DefSnapshotReferenceComparer Instance = new DefSnapshotReferenceComparer();

            bool IEqualityComparer<object>.Equals(object x, object y)
            {
                return ReferenceEquals(x, y);
            }

            int IEqualityComparer<object>.GetHashCode(object obj)
            {
                return RuntimeHelpers.GetHashCode(obj);
            }
        }
    }
}
#endif
