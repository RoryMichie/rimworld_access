#if DEBUG
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using HarmonyLib;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// ShellDev's MUTATION-C drift detector. A MUTATION-C hand-copied gate mirrors a third-party mod's
    /// private method because no vanilla A/B vehicle exists to ride; the risk is the mirror silently
    /// going stale when the mod updates. This registers an expected IL hash per mirrored method and
    /// reports OK/DRIFT/FAULT so a bridge script (or a future CI step) can catch a stale mirror instead
    /// of a compat layer quietly reimplementing behavior the mod no longer has.
    /// </summary>
    public static partial class ShellDev
    {
        private sealed class MirrorSentinelEntry
        {
            public string Key;
            public Type DeclaringType;
            public string MethodName;
            public Type[] ParameterTypes;
            public string ExpectedIlHash;
        }

        private static readonly List<MirrorSentinelEntry> mirrorSentinelEntries = new List<MirrorSentinelEntry>();

        /// <summary>SHA256 hex digest of a method's IL bytes; <c>&lt;no-body&gt;</c> if the method has no IL body (abstract, extern, interface, etc.).</summary>
        public static string ComputeIlHash(MethodBase method)
        {
            if (method == null)
            {
                return "<no-body>";
            }
            MethodBody body = method.GetMethodBody();
            if (body == null)
            {
                return "<no-body>";
            }
            byte[] il = body.GetILAsByteArray();
            if (il == null)
            {
                return "<no-body>";
            }
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(il);
                var sb = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                {
                    sb.Append(hash[i].ToString("x2"));
                }
                return sb.ToString();
            }
        }

        /// <summary>
        /// Registers a MUTATION-C mirror to watch: the declaring type and method to hash, and the
        /// IL hash recorded when the mirror was written (get it from <see cref="HashOf"/>). Pass
        /// <paramref name="parameterTypes"/> as null to resolve the method by name alone; that
        /// requires exactly one method with that name on the declaring type, or the entry faults
        /// as ambiguous when <see cref="CheckMirrors"/> resolves it.
        /// </summary>
        public static void RegisterMirror(string key, Type declaringType, string methodName, Type[] parameterTypes, string expectedIlHash)
        {
            mirrorSentinelEntries.Add(new MirrorSentinelEntry
            {
                Key = key,
                DeclaringType = declaringType,
                MethodName = methodName,
                ParameterTypes = parameterTypes,
                ExpectedIlHash = expectedIlHash
            });
        }

        /// <summary>
        /// Resolves and re-hashes every registered mirror, reporting one line each: <c>OK
        /// &lt;key&gt;</c>, <c>DRIFT &lt;key&gt;: expected &lt;hash8&gt; got &lt;hash8&gt;</c>, or
        /// <c>FAULT &lt;key&gt;: &lt;reason&gt;</c>. Any DRIFT or FAULT is also logged via
        /// <see cref="Log.Warning"/> prefixed <c>[RimWorldAccess] mirror sentinel:</c> so it lands
        /// alongside <see cref="RecentErrors"/>. Ends with a summary line.
        /// </summary>
        public static string CheckMirrors()
        {
            var sb = new StringBuilder();
            int okCount = 0;
            int driftCount = 0;
            int faultCount = 0;

            foreach (MirrorSentinelEntry entry in mirrorSentinelEntries)
            {
                string faultReason;
                MethodBase method = ResolveMirrorSentinelMethod(entry, out faultReason);
                string line;
                if (method == null)
                {
                    faultCount++;
                    line = "FAULT " + entry.Key + ": " + faultReason;
                    sb.Append(line).Append('\n');
                    Log.Warning("[RimWorldAccess] mirror sentinel: " + line);
                    continue;
                }

                string actualHash = ComputeIlHash(method);
                if (actualHash == "<no-body>")
                {
                    faultCount++;
                    line = "FAULT " + entry.Key + ": method has no IL body";
                    sb.Append(line).Append('\n');
                    Log.Warning("[RimWorldAccess] mirror sentinel: " + line);
                    continue;
                }

                if (string.Equals(actualHash, entry.ExpectedIlHash, StringComparison.OrdinalIgnoreCase))
                {
                    okCount++;
                    sb.Append("OK ").Append(entry.Key).Append('\n');
                }
                else
                {
                    driftCount++;
                    line = "DRIFT " + entry.Key + ": expected " + ShortHash(entry.ExpectedIlHash) + " got " + ShortHash(actualHash);
                    sb.Append(line).Append('\n');
                    Log.Warning("[RimWorldAccess] mirror sentinel: " + line);
                }
            }

            sb.Append(mirrorSentinelEntries.Count).Append(" mirror(s): ").Append(okCount).Append(" OK, ")
                .Append(driftCount).Append(" drift, ").Append(faultCount).Append(" fault");
            return sb.ToString();
        }

        /// <summary>Resolves a type (by full name, scanning loaded assemblies) and a method on it by name alone, returning its IL hash. Used to record the expected hash when first registering a mirror.</summary>
        public static string HashOf(string typeFullName, string methodName)
        {
            Type type = Type.GetType(typeFullName);
            if (type == null)
            {
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    type = asm.GetType(typeFullName);
                    if (type != null)
                    {
                        break;
                    }
                }
            }
            if (type == null)
            {
                return "type not found: " + typeFullName; // l10n-exempt: dev-bridge-only diagnostic text, never player-facing.
            }

            MethodInfo[] matches = type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(m => m.Name == methodName).ToArray();
            if (matches.Length == 0)
            {
                return "no method named '" + methodName + "' found on " + typeFullName; // l10n-exempt: dev-bridge-only diagnostic text, never player-facing.
            }
            if (matches.Length > 1)
            {
                return "ambiguous: " + matches.Length + " overloads named '" + methodName + "' on " + typeFullName;
            }
            return ComputeIlHash(matches[0]);
        }

        private static string ShortHash(string hash)
        {
            if (string.IsNullOrEmpty(hash))
            {
                return "(none)";
            }
            return hash.Length <= 8 ? hash : hash.Substring(0, 8);
        }

        private static MethodBase ResolveMirrorSentinelMethod(MirrorSentinelEntry entry, out string faultReason)
        {
            faultReason = null;
            if (entry.DeclaringType == null)
            {
                faultReason = "declaring type is null";
                return null;
            }

            if (entry.ParameterTypes != null)
            {
                MethodBase method = AccessTools.Method(entry.DeclaringType, entry.MethodName, entry.ParameterTypes);
                if (method == null)
                {
                    faultReason = "method not found: " + entry.DeclaringType.FullName + "." + entry.MethodName
                        + "(" + string.Join(", ", entry.ParameterTypes.Select(t => t.Name)) + ")";
                }
                return method;
            }

            MethodInfo[] matches = entry.DeclaringType
                .GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(m => m.Name == entry.MethodName).ToArray();
            if (matches.Length == 0)
            {
                faultReason = "no method named '" + entry.MethodName + "' found on " + entry.DeclaringType.FullName;
                return null;
            }
            if (matches.Length > 1)
            {
                faultReason = "ambiguous: " + matches.Length + " overloads named '" + entry.MethodName + "' on " + entry.DeclaringType.FullName;
                return null;
            }
            return matches[0];
        }
    }
}
#endif
