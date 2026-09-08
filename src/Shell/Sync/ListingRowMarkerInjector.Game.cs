using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace RimWorldAccess.Shell
{
    /// <summary>What a marker site pushes for the row it precedes.</summary>
    internal enum MarkerPayload
    {
        /// <summary>The untranslated translation key vanilla's own IL carries in an ldstr just before the row call.</summary>
        HarvestedKey,
        /// <summary>A "&lt;Type&gt;.&lt;Method&gt;#&lt;n&gt;" site token, for row calls whose IL carries no literal at all.</summary>
        SyntheticKey,
        /// <summary>The reference-typed local vanilla's own loop stored the row's object into.</summary>
        LoopLocal,
        /// <summary>A static field holding the row's object.</summary>
        StaticField,
    }

    /// <summary>One expected row call in a patched method, and what identity it carries.</summary>
    internal sealed class MarkerSite
    {
        /// <summary>The call/callvirt to inject before.</summary>
        public MethodInfo RowCall { get; set; }

        public MarkerPayload Payload { get; set; }

        /// <summary>HarvestedKey: the ldstr the site must carry. SyntheticKey: the token to emit.</summary>
        public string ExpectedKey { get; set; }

        /// <summary>LoopLocal: the call whose stloc names the local (get_Item, get_Current).</summary>
        public MethodInfo LocalSource { get; set; }

        /// <summary>StaticField: the field to ldsfld.</summary>
        public FieldInfo StaticSource { get; set; }
    }

    /// <summary>The manifest for one patched method: what defines a row call there, and the sites expected in IL order.</summary>
    internal sealed class MarkerPlan
    {
        public MethodInfo[] RowCallTargets { get; set; }

        public MarkerSite[] Sites { get; set; }
    }

    /// <summary>Per-method injection outcome, for DEBUG assertions and the QA trace.</summary>
    internal sealed class InjectionReport
    {
        public string Method;
        public int Expected;
        public int Found;
        public int Injected;
        public readonly List<string> Skips = new List<string>();
    }

    /// <summary>
    /// Injects a stack-neutral identity marker immediately before each row call
    /// named by a <see cref="MarkerPlan"/>, so <see cref="ListingRowCapture"/>'s
    /// Listing.GetRect postfix can ring that row by object reference or by a
    /// token vanilla itself built the row from, with no draw-order arithmetic
    /// anywhere.
    ///
    /// The injected pair pushes one value and calls a void method that consumes
    /// it, so it is legal at any instruction boundary with a valid stack —
    /// including mid-argument-evaluation, which is where it lands — and it
    /// carries no branches or locals, so exception blocks and the compiler's
    /// local layout are untouched.
    ///
    /// ASSUMPTION: row calls do not nest. A marker is consumed by the very next
    /// GetRect, so a row call that drew another row call's rect first would ring
    /// the wrong row.
    ///
    /// Unlike the house transpiler style (GizmoHotkeyShiftPatch, which has no
    /// failure handling at all), every failure here is caught: a throwing
    /// transpiler is a HarmonyException at patch time that can take unrelated
    /// patches, or the game, down with it. A site that fails its manifest is
    /// skipped and logged; skipping injects no marker, which disables exactly
    /// that row's ring and nothing else.
    /// </summary>
    internal static class ListingRowMarkerInjector
    {
        private static readonly MethodInfo SetPendingRowObjectMethod =
            AccessTools.Method(typeof(ListingRowCapture), nameof(ListingRowCapture.SetPendingRowObject));
        private static readonly MethodInfo SetPendingRowKeyMethod =
            AccessTools.Method(typeof(ListingRowCapture), nameof(ListingRowCapture.SetPendingRowKey));

        /// <summary>Every method this injector has processed, newest last.</summary>
        internal static readonly List<InjectionReport> Reports = new List<InjectionReport>();

        internal static IEnumerable<CodeInstruction> Inject(IEnumerable<CodeInstruction> instructions, MethodBase original, MarkerPlan plan)
        {
            List<CodeInstruction> source = null;
            try
            {
                source = new List<CodeInstruction>(instructions);
                return InjectCore(source, original, plan);
            }
            catch (Exception ex)
            {
                ModLogger.Error("Listing row marker injection failed for " + Describe(original) + ": " + ex);
                return source ?? instructions;
            }
        }

        private static List<CodeInstruction> InjectCore(List<CodeInstruction> source, MethodBase original, MarkerPlan plan)
        {
            InjectionReport report = new InjectionReport { Method = Describe(original) };
            if (plan == null || plan.RowCallTargets == null || plan.Sites == null
                || SetPendingRowObjectMethod == null || SetPendingRowKeyMethod == null)
            {
                report.Skips.Add("unusable plan");
                Record(report);
                return source;
            }
            report.Expected = plan.Sites.Length;

            List<int> found = new List<int>();
            for (int i = 0; i < source.Count; i++)
            {
                if (CallsAny(source[i], plan.RowCallTargets))
                {
                    found.Add(i);
                }
            }
            report.Found = found.Count;
            if (found.Count != plan.Sites.Length)
            {
                report.Skips.Add("expected " + plan.Sites.Length + " row calls, found " + found.Count);
            }

            // Phase one decides every marker without touching the stream, so a
            // failure mid-plan cannot leave a half-rewritten method behind.
            List<KeyValuePair<int, CodeInstruction[]>> accepted = new List<KeyValuePair<int, CodeInstruction[]>>();
            int pairs = Math.Min(found.Count, plan.Sites.Length);
            for (int s = 0; s < pairs; s++)
            {
                int scanFrom = s > 0 ? found[s - 1] + 1 : 0;
                CodeInstruction[] marker = BuildMarker(source, found[s], scanFrom, plan.Sites[s], original, s, report);
                if (marker != null)
                {
                    accepted.Add(new KeyValuePair<int, CodeInstruction[]>(found[s], marker));
                }
            }

            List<CodeInstruction> working = new List<CodeInstruction>(source);
            for (int a = accepted.Count - 1; a >= 0; a--)
            {
                int at = accepted[a].Key;
                CodeInstruction[] marker = accepted[a].Value;
                CodeInstruction anchor = working[at];
                // A branch into the site must still run the marker, so the
                // anchor's labels move onto it; blocks stay on the anchor.
                marker[0].labels.AddRange(anchor.labels);
                anchor.labels.Clear();
                working.InsertRange(at, marker);
            }
            report.Injected = accepted.Count;
            Record(report);
            return working;
        }

        private static CodeInstruction[] BuildMarker(List<CodeInstruction> codes, int at, int scanFrom, MarkerSite site,
            MethodBase original, int siteIndex, InjectionReport report)
        {
            if (site == null || site.RowCall == null || !codes[at].Calls(site.RowCall))
            {
                report.Skips.Add("site " + siteIndex + ": row call is " + Describe(codes[at].operand as MethodBase)
                    + ", manifest says " + Describe(site == null ? null : site.RowCall));
                return null;
            }

            switch (site.Payload)
            {
                case MarkerPayload.HarvestedKey:
                {
                    string harvested = NearestLiteral(codes, at, scanFrom);
                    if (!string.Equals(harvested, site.ExpectedKey, StringComparison.Ordinal))
                    {
                        report.Skips.Add("site " + siteIndex + ": expected literal \"" + site.ExpectedKey
                            + "\", harvested " + (harvested == null ? "none" : "\"" + harvested + "\""));
                        return null;
                    }
                    return KeyMarker(harvested);
                }
                case MarkerPayload.SyntheticKey:
                    if (string.IsNullOrEmpty(site.ExpectedKey))
                    {
                        report.Skips.Add("site " + siteIndex + ": synthetic site carries no token");
                        return null;
                    }
                    return KeyMarker(site.ExpectedKey);
                case MarkerPayload.LoopLocal:
                {
                    if (site.LocalSource == null)
                    {
                        report.Skips.Add("site " + siteIndex + ": no LocalSource in the manifest");
                        return null;
                    }
                    int local = NearestLocalStore(codes, at, site.LocalSource);
                    if (local < 0)
                    {
                        report.Skips.Add("site " + siteIndex + ": no local stored from " + Describe(site.LocalSource));
                        return null;
                    }
                    Type localType = LocalTypeOf(original, local);
                    if (localType == null || localType.IsValueType)
                    {
                        report.Skips.Add("site " + siteIndex + ": local " + local + " is "
                            + (localType == null ? "of unknown type" : "the value type " + localType.Name));
                        return null;
                    }
                    return new CodeInstruction[]
                    {
                        CodeInstruction.LoadLocal(local),
                        new CodeInstruction(OpCodes.Call, SetPendingRowObjectMethod),
                    };
                }
                case MarkerPayload.StaticField:
                    if (site.StaticSource == null)
                    {
                        report.Skips.Add("site " + siteIndex + ": no StaticSource in the manifest");
                        return null;
                    }
                    return new CodeInstruction[]
                    {
                        new CodeInstruction(OpCodes.Ldsfld, site.StaticSource),
                        new CodeInstruction(OpCodes.Call, SetPendingRowObjectMethod),
                    };
            }
            report.Skips.Add("site " + siteIndex + ": unknown payload " + site.Payload);
            return null;
        }

        private static CodeInstruction[] KeyMarker(string key)
        {
            return new CodeInstruction[]
            {
                new CodeInstruction(OpCodes.Ldstr, key),
                new CodeInstruction(OpCodes.Call, SetPendingRowKeyMethod),
            };
        }

        private static bool CallsAny(CodeInstruction code, MethodInfo[] targets)
        {
            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] != null && code.Calls(targets[i]))
                {
                    return true;
                }
            }
            return false;
        }

        private static string NearestLiteral(List<CodeInstruction> codes, int at, int scanFrom)
        {
            for (int i = at - 1; i >= scanFrom; i--)
            {
                if (codes[i].opcode == OpCodes.Ldstr)
                {
                    return codes[i].operand as string;
                }
            }
            return null;
        }

        private static int NearestLocalStore(List<CodeInstruction> codes, int at, MethodInfo localSource)
        {
            for (int i = at - 1; i > 0; i--)
            {
                if (codes[i].IsStloc() && codes[i - 1].Calls(localSource))
                {
                    return LocalIndexOf(codes[i]);
                }
            }
            return -1;
        }

        private static int LocalIndexOf(CodeInstruction code)
        {
            OpCode op = code.opcode;
            if (op == OpCodes.Stloc_0) return 0;
            if (op == OpCodes.Stloc_1) return 1;
            if (op == OpCodes.Stloc_2) return 2;
            if (op == OpCodes.Stloc_3) return 3;
            if (code.operand is LocalVariableInfo local) return local.LocalIndex;
            if (code.operand is IConvertible index) return index.ToInt32(null);
            return -1;
        }

        private static Type LocalTypeOf(MethodBase original, int index)
        {
            MethodBody body = original == null ? null : original.GetMethodBody();
            if (body == null)
            {
                return null;
            }
            IList<LocalVariableInfo> locals = body.LocalVariables;
            for (int i = 0; i < locals.Count; i++)
            {
                if (locals[i].LocalIndex == index)
                {
                    return locals[i].LocalType;
                }
            }
            return null;
        }

        private static string Describe(MemberInfo member)
        {
            if (member == null)
            {
                return "<none>";
            }
            return (member.DeclaringType == null ? "" : member.DeclaringType.FullName + ".") + member.Name;
        }

        private static void Record(InjectionReport report)
        {
            Reports.Add(report);
            for (int i = 0; i < report.Skips.Count; i++)
            {
                ModLogger.Error("Listing row marker, " + report.Method + ": " + report.Skips[i]);
            }
#if DEBUG
            ShellDev.QARecord("capture", "marker inject " + report.Method + " expected=" + report.Expected
                + " found=" + report.Found + " injected=" + report.Injected);
#endif
        }
    }
}
