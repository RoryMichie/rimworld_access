using System;
using System.Collections.Generic;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// One IL instruction reduced to what tooltip-gate eligibility depends on. <see cref="Index"/> is
    /// its position in the method's instruction list, and <see cref="BranchTarget"/> uses the same
    /// numbering.
    /// </summary>
    public readonly struct GateInstruction
    {
        public readonly int Index;
        /// <summary>Harmony's <c>OpCode.Name</c>, e.g. "call", "brfalse.s", "stsfld".</summary>
        public readonly string OpCode;
        /// <summary><c>Namespace.Type::Method</c> for a call, otherwise null.</summary>
        public readonly string Member;
        /// <summary>Instruction index this branch jumps to, or -1 when this is not a resolved branch.</summary>
        public readonly int BranchTarget;
        /// <summary>Local slot for <c>ldloc*</c>/<c>ldloca*</c>/<c>stloc*</c>, otherwise -1.</summary>
        public readonly int Slot;
        /// <summary>
        /// Full name of the type this instruction leaves on the stack, null where none applies. Null
        /// where the freshness rule needs it refuses the span, so a bridge that cannot resolve a type
        /// costs coverage rather than soundness.
        /// </summary>
        public readonly string PushedType;

        public GateInstruction(int index, string opCode, string member = null, int branchTarget = -1,
            int slot = -1, string pushedType = null)
        {
            Index = index;
            OpCode = opCode;
            Member = member;
            BranchTarget = branchTarget;
            Slot = slot;
            PushedType = pushedType;
        }
    }

    public enum GateVerdict
    {
        Eligible,
        NotAConditional,
        NoTooltipInBranch,
        DangerousOperation,
        MalformedBranch,
    }

    public sealed class GateSiteResult
    {
        /// <summary>Index of the <c>Verse.Mouse::IsOver</c> call instruction.</summary>
        public int SiteIndex;
        public GateVerdict Verdict;
        /// <summary>The opcode or member that caused a refusal, for the DEBUG coverage report.</summary>
        public string Reason;
    }

    /// <summary>
    /// Decides, per <c>Verse.Mouse::IsOver</c> call site, whether the branch it guards exists solely
    /// to register a tooltip. A certified site gets its call operand swapped for
    /// <c>TooltipHoverGate.IsOverOrHarvesting</c>, so a keyboard-driven harvest pass reaches tooltip
    /// strings the game only builds under a real pointer; every other site stays byte-identical.
    ///
    /// The analysis is deliberately narrow. It certifies ONE shape — a call consumed immediately by a
    /// forward conditional branch whose guarded span registers a tooltip and does nothing else
    /// observable — and refuses everything it cannot bound: the guard-clause shape
    /// <c>if (!Mouse.IsOver(rect)) return;</c> (whose span is a lone <c>ret</c>, leaving the method
    /// unforced), hover-follow state writes, and any span that could swallow an event, play a sound,
    /// open a window, return a click or move IMGUI's layout state. The one store it permits is
    /// Roslyn's delegate cache (<see cref="IsLambdaCacheField"/>). Refusals are the mechanism, not a
    /// failure: an uncertified site keeps vanilla behaviour exactly.
    ///
    /// Pure by design, with no Unity or Verse types, so every census shape is testable without the
    /// game; TooltipGateTranspiler.Game.cs owns the Harmony bridge.
    /// </summary>
    public static class TooltipGateAnalysis
    {
        private const string MouseIsOver = "Verse.Mouse::IsOver";
        private const string TipRegion = "Verse.TooltipHandler::TipRegion";
        private const string TipRegionByKey = "Verse.TooltipHandler::TipRegionByKey";

        private static readonly HashSet<string> BlockedMembers = new HashSet<string>
        {
            "UnityEngine.Event::Use",
            "UnityEngine.Input::GetMouseButton",
            "UnityEngine.Input::GetMouseButtonDown",
            "UnityEngine.Input::GetMouseButtonUp",
            "Verse.WindowStack::Add",
            "RimWorld.TargetHighlighter::Highlight",
            "Verse.Selector::Select",
            "Verse.Selector::Deselect",
            "Verse.Selector::ClearSelection",
            // A control id taken on Layout but not on Repaint shifts the whole id stream for that
            // pass, which is how IMGUI loses focus and hot control.
            "UnityEngine.GUIUtility::GetControlID",
        };

        private static readonly string[] BlockedTypes =
        {
            "Verse.SoundStarter",
            "Verse.Sound.SoundDef",
            "Verse.SoundDef",
            // Every GUILayout call appends a layout entry, and IMGUI requires Layout and Repaint to
            // produce identical lists; the gate forces a branch on Layout only, so one such call
            // desyncs the pair for the rest of the surface.
            "UnityEngine.GUILayout",
            "UnityEngine.GUILayoutUtility",
        };

        // A Widgets member that can report a click or write through a ref parameter is an
        // interaction, not a tooltip.
        private static readonly string[] BlockedWidgetPrefixes =
        {
            "Button",
            "Checkbox",
            "Draggable",
            "RadioButton",
            "HorizontalSlider",
        };

        // Group, clip and scroll brackets push onto IMGUI stacks that must unwind in the same order
        // every pass, so a Begin whose End sits outside the span unbalances the stack for the rest of
        // the frame; the whole family is refused wherever declared.
        private static readonly string[] LayoutBracketTypes =
        {
            "UnityEngine.GUI",
            "Verse.Widgets",
        };

        private const string DisplayClass = "<>c__DisplayClass";

        // Everything an array value is assignable to: a push of one could be a stelem's receiver, so
        // the freshness rule refuses a span that both stores elements and produces one.
        private static readonly string[] ArrayCapableTypes =
        {
            "System.Array",
            "System.ICloneable",
            "System.Collections.IList",
            "System.Collections.ICollection",
            "System.Collections.IEnumerable",
            "System.Collections.IStructuralComparable",
            "System.Collections.IStructuralEquatable",
            "System.Collections.Generic.IList`1",
            "System.Collections.Generic.ICollection`1",
            "System.Collections.Generic.IEnumerable`1",
            "System.Collections.Generic.IReadOnlyList`1",
            "System.Collections.Generic.IReadOnlyCollection`1",
        };

        // Opcodes that push nothing, or push a value no store in a span can use as its receiver.
        // Anything absent from here that cannot be typed refuses the span, so an unlisted opcode
        // costs coverage rather than soundness.
        private static readonly HashSet<string> HarmlessOpCodes = new HashSet<string>
        {
            "nop", "break", "dup", "pop", "ldstr", "ldnull", "ldftn", "ldvirtftn", "ldtoken",
            "sizeof", "ldlen", "initobj", "ret", "throw", "rethrow", "endfinally", "endfilter",
            "switch", "arglist", "ckfinite",
            "constrained.", "readonly.", "volatile.", "tail.", "unaligned.",
            "neg", "not", "add", "sub", "mul", "div", "rem", "and", "or", "xor", "shl", "shr",
            "ceq", "cgt", "clt",
            // Stores push nothing; whether each is allowed is decided above.
            "stsfld", "stfld",
        };

        private static readonly string[] HarmlessOpCodePrefixes =
        {
            "ldc.", "conv", "stloc", "stelem",
            "br", "beq", "bne", "bge", "bgt", "ble", "blt", "leave",
            "add.", "sub.", "mul.", "div.", "rem.", "shr.", "cgt.", "clt.",
        };

        public static IReadOnlyList<GateSiteResult> Analyze(IReadOnlyList<GateInstruction> body)
        {
            var results = new List<GateSiteResult>();
            if (body == null)
            {
                return results;
            }
            var positions = new List<int>();
            var spans = new List<int>();
            for (int i = 0; i < body.Count; i++)
            {
                if (!IsCallTo(body[i], MouseIsOver))
                {
                    continue;
                }
                int spanStart, spanEnd;
                results.Add(Judge(body, i, out spanStart, out spanEnd));
                positions.Add(i);
                spans.Add(spanStart);
                spans.Add(spanEnd);
            }
            // Overlapping guards: the outer site already refused on the nested call it contains, and
            // the inner one goes with it rather than opening inside an uncertified branch.
            for (int i = 0; i < results.Count; i++)
            {
                if (results[i].Verdict != GateVerdict.Eligible)
                {
                    continue;
                }
                for (int j = 0; j < results.Count; j++)
                {
                    if (j != i && positions[i] > spans[j * 2] && positions[i] < spans[j * 2 + 1])
                    {
                        results[i].Verdict = GateVerdict.DangerousOperation;
                        results[i].Reason = MouseIsOver;
                        break;
                    }
                }
            }
            return results;
        }

        private static GateSiteResult Judge(IReadOnlyList<GateInstruction> body, int site, out int spanStart, out int spanEnd)
        {
            spanStart = -1;
            spanEnd = -1;
            var result = new GateSiteResult { SiteIndex = body[site].Index };

            // The result must feed a conditional branch directly, optionally through the C# negation
            // idiom; anything that stores or reuses it puts effects beyond this span's reach.
            int branch = site + 1;
            if (branch < body.Count && body[branch].OpCode == "ldc.i4.0"
                && branch + 1 < body.Count && body[branch + 1].OpCode == "ceq")
            {
                branch += 2;
            }
            if (branch >= body.Count || !IsConditionalBranch(body[branch].OpCode))
            {
                result.Verdict = GateVerdict.NotAConditional;
                result.Reason = branch < body.Count ? body[branch].OpCode : "end of method";
                return result;
            }

            // The guarded span is what the branch skips over.
            int target = body[branch].BranchTarget;
            if (target <= branch || target > body.Count)
            {
                result.Verdict = GateVerdict.MalformedBranch;
                result.Reason = body[branch].OpCode + " -> " + target;
                return result;
            }
            spanStart = branch;
            spanEnd = target;

            bool registersTip = false;
            for (int i = branch + 1; i < target; i++)
            {
                if (IsCallTo(body[i], TipRegion) || IsCallTo(body[i], TipRegionByKey))
                {
                    registersTip = true;
                }
            }
            if (!registersTip)
            {
                result.Verdict = GateVerdict.NoTooltipInBranch;
                return result;
            }

            string refusal = SpanRefusal(body, branch, target);
            if (refusal != null)
            {
                result.Verdict = GateVerdict.DangerousOperation;
                result.Reason = refusal;
                return result;
            }

            result.Verdict = GateVerdict.Eligible;
            return result;
        }

        /// <summary>
        /// The offender that refuses this span, or null when it is safe to force. A span whose every
        /// offender is a field or element write gets a second chance from
        /// <see cref="StoresAreAllFresh"/>.
        /// </summary>
        private static string SpanRefusal(IReadOnlyList<GateInstruction> body, int branch, int target)
        {
            string first = null;
            bool storesOnly = true;
            for (int i = branch + 1; i < target; i++)
            {
                string offender = DangerIn(body[i]);
                if (offender == null)
                {
                    continue;
                }
                if (first == null)
                {
                    first = offender;
                }
                string op = body[i].OpCode;
                if (op != "stfld" && !StartsWith(op, "stelem"))
                {
                    storesOnly = false;
                }
            }
            if (first == null || !storesOnly)
            {
                return first;
            }
            return StoresAreAllFresh(body, branch, target) ? null : first;
        }

        private static string DangerIn(GateInstruction instruction)
        {
            string op = instruction.OpCode;
            if (op == "stsfld" || op == "stfld")
            {
                return IsLambdaCacheField(instruction.Member)
                    ? null
                    : (instruction.Member != null ? op + " " + instruction.Member : op);
            }
            if (op == "starg" || op == "starg.s"
                || StartsWith(op, "stind") || StartsWith(op, "stelem"))
            {
                return instruction.Member != null ? op + " " + instruction.Member : op;
            }

            // Member is populated for field opcodes too, so the blocklists apply to calls only.
            string member = instruction.Member;
            if (member == null || !IsCall(op))
            {
                return null;
            }
            if (BlockedMembers.Contains(member))
            {
                return member;
            }
            // Refuse both rather than reason about which one a forced outer branch would open.
            if (member == MouseIsOver)
            {
                return member;
            }

            int split = member.IndexOf("::");
            if (split < 0)
            {
                return null;
            }
            string type = member.Substring(0, split);
            string name = member.Substring(split + 2);

            if (name == "Invoke")
            {
                return member;
            }
            for (int i = 0; i < BlockedTypes.Length; i++)
            {
                if (type == BlockedTypes[i])
                {
                    return member;
                }
            }
            if (type == "Verse.Widgets")
            {
                for (int i = 0; i < BlockedWidgetPrefixes.Length; i++)
                {
                    if (StartsWith(name, BlockedWidgetPrefixes[i]))
                    {
                        return member;
                    }
                }
            }
            if (StartsWith(name, "Begin") || StartsWith(name, "End"))
            {
                for (int i = 0; i < LayoutBracketTypes.Length; i++)
                {
                    if (type == LayoutBracketTypes[i])
                    {
                        return member;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// The freshness rule: a write into an object the span allocated after the branch was taken
        /// cannot be observed by anything the harvest did not already force, because no other
        /// reference to it exists yet.
        ///
        /// The receiver is proved fresh in the NEGATIVE, which is what lets a flat instruction list
        /// decide it without a stack model: a Roslyn display class is sealed, derives straight from
        /// object and implements nothing, so if no instruction in the span can push a value
        /// statically typed as that class or as object, every value of that class on the stack came
        /// from the span's own <c>newobj</c>. <see cref="ArrayCapableTypes"/> carries the same
        /// argument for <c>stelem</c>. The claim quantifies over the whole span, so it is
        /// path-insensitive and inner branches cost nothing — except slot freshness, which does
        /// depend on control flow reaching the allocation and is guarded accordingly. Anything the
        /// classifier cannot type refuses the span.
        /// </summary>
        private static bool StoresAreAllFresh(IReadOnlyList<GateInstruction> body, int branch, int target)
        {
            var storedTypes = new HashSet<string>();
            bool anyElementStore = false;
            for (int i = branch + 1; i < target; i++)
            {
                string op = body[i].OpCode;
                if (op == "stfld" && !IsLambdaCacheField(body[i].Member))
                {
                    string owner = DeclaringTypeOf(body[i].Member);
                    if (owner == null || owner.IndexOf(DisplayClass, StringComparison.Ordinal) < 0)
                    {
                        return false;
                    }
                    storedTypes.Add(owner);
                }
                else if (StartsWith(op, "stelem"))
                {
                    anyElementStore = true;
                }
            }
            foreach (string owner in storedTypes)
            {
                if (!AllocatesIn(body, branch, target, owner))
                {
                    return false;
                }
            }

            HashSet<int> freshSlots = FreshSlots(body, branch, target);
            if (freshSlots == null)
            {
                return false;
            }
            if (freshSlots.Count > 0 && !AllocationAlwaysReached(body, branch, target))
            {
                return false;
            }

            for (int i = branch + 1; i < target; i++)
            {
                if (!PushIsHarmless(body[i], storedTypes, anyElementStore, freshSlots))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool AllocatesIn(IReadOnlyList<GateInstruction> body, int branch, int target, string type)
        {
            for (int i = branch + 1; i < target; i++)
            {
                if (body[i].OpCode == "newobj" && DeclaringTypeOf(body[i].Member) == type)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Slots holding an allocation made inside the span: written exactly once straight from a
        /// <c>newobj</c>/<c>newarr</c>, never read ahead of that write or across an edge jumping back
        /// over it. Null when a slot could not be resolved.
        /// </summary>
        private static HashSet<int> FreshSlots(IReadOnlyList<GateInstruction> body, int branch, int target)
        {
            var fresh = new HashSet<int>();
            var writtenAt = new Dictionary<int, int>();
            for (int i = branch + 1; i < target; i++)
            {
                if (!StartsWith(body[i].OpCode, "stloc"))
                {
                    continue;
                }
                int slot = body[i].Slot;
                if (slot < 0)
                {
                    return null;
                }
                if (writtenAt.ContainsKey(slot))
                {
                    fresh.Remove(slot);
                    continue;
                }
                writtenAt[slot] = i;
                string source = body[i - 1].OpCode;
                if (source == "newobj" || source == "newarr")
                {
                    fresh.Add(slot);
                }
            }

            var dominated = new HashSet<int>();
            foreach (int slot in fresh)
            {
                int at = writtenAt[slot];
                bool safe = true;
                for (int i = branch + 1; i < target && safe; i++)
                {
                    string op = body[i].OpCode;
                    if (i < at && (StartsWith(op, "ldloc") || StartsWith(op, "ldloca")) && body[i].Slot == slot)
                    {
                        safe = false;
                    }
                    if (body[i].BranchTarget > branch && body[i].BranchTarget <= at)
                    {
                        safe = false;
                    }
                }
                if (safe)
                {
                    dominated.Add(slot);
                }
            }
            return dominated;
        }

        /// <summary>
        /// True when no edge carries control into the middle of the span. A <c>switch</c> anywhere in
        /// the method disqualifies it outright: this abstraction carries one branch target per
        /// instruction, so a jump table's other arms are edges it cannot see.
        /// </summary>
        private static bool AllocationAlwaysReached(IReadOnlyList<GateInstruction> body, int branch, int target)
        {
            for (int i = 0; i < body.Count; i++)
            {
                if (body[i].OpCode == "switch")
                {
                    return false;
                }
                if (i > branch && i < target)
                {
                    continue;
                }
                if (body[i].BranchTarget > branch && body[i].BranchTarget < target)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>False when this instruction could push a value a store elsewhere in the span might use as its receiver.</summary>
        private static bool PushIsHarmless(GateInstruction instruction, HashSet<string> storedTypes,
            bool anyElementStore, HashSet<int> freshSlots)
        {
            string op = instruction.OpCode;
            if (op == "newobj" || op == "newarr")
            {
                return true;
            }

            string pushed;
            if (StartsWith(op, "ldloc"))
            {
                if (instruction.Slot >= 0 && freshSlots.Contains(instruction.Slot))
                {
                    return true;
                }
                pushed = instruction.PushedType;
            }
            else if (IsTypedPush(op))
            {
                pushed = instruction.PushedType;
            }
            else
            {
                return IsHarmlessOpCode(op);
            }

            if (pushed == null)
            {
                return false;
            }
            if (pushed == "System.Void")
            {
                return true;
            }
            if (pushed == "System.Object" || storedTypes.Contains(pushed))
            {
                return false;
            }
            if (anyElementStore && (EndsWithArray(pushed) || IsArrayCapable(pushed)))
            {
                return false;
            }
            return true;
        }

        private static bool IsTypedPush(string op)
        {
            return op == "call" || op == "callvirt"
                || op == "ldfld" || op == "ldsfld" || op == "ldflda" || op == "ldsflda"
                || op == "box" || op == "castclass" || op == "isinst"
                || op == "unbox" || op == "unbox.any" || op == "ldobj"
                || StartsWith(op, "ldarg");
        }

        private static bool IsHarmlessOpCode(string op)
        {
            if (HarmlessOpCodes.Contains(op))
            {
                return true;
            }
            for (int i = 0; i < HarmlessOpCodePrefixes.Length; i++)
            {
                if (StartsWith(op, HarmlessOpCodePrefixes[i]))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsArrayCapable(string type)
        {
            int generic = type.IndexOf('<');
            string bare = generic < 0 ? type : type.Substring(0, generic);
            for (int i = 0; i < ArrayCapableTypes.Length; i++)
            {
                if (bare == ArrayCapableTypes[i])
                {
                    return true;
                }
            }
            return false;
        }

        private static bool EndsWithArray(string type)
        {
            return type.Length >= 2 && type[type.Length - 2] == '[' && type[type.Length - 1] == ']';
        }

        private static string DeclaringTypeOf(string member)
        {
            if (member == null)
            {
                return null;
            }
            int split = member.IndexOf("::", StringComparison.Ordinal);
            return split < 0 ? null : member.Substring(0, split);
        }

        /// <summary>
        /// True for Roslyn's cached-delegate field. Filling that cache is the compiler memoizing a
        /// delegate it is about to pass to <c>TipRegion</c>, with no observable game effect, so it is
        /// the one store this analysis lets through.
        /// </summary>
        private static bool IsLambdaCacheField(string member)
        {
            if (member == null)
            {
                return false;
            }
            int split = member.IndexOf("::");
            return split >= 0 && member.IndexOf("<>9__", split + 2) >= 0;
        }

        private static bool IsCallTo(GateInstruction instruction, string member)
        {
            return instruction.Member == member && IsCall(instruction.OpCode);
        }

        private static bool IsCall(string opCode)
        {
            return opCode == "call" || opCode == "callvirt" || opCode == "newobj";
        }

        private static bool IsConditionalBranch(string opCode)
        {
            return opCode == "brtrue" || opCode == "brtrue.s"
                || opCode == "brfalse" || opCode == "brfalse.s";
        }

        private static bool StartsWith(string value, string prefix)
        {
            return value != null && value.Length >= prefix.Length
                && string.CompareOrdinal(value, 0, prefix, 0, prefix.Length) == 0;
        }
    }
}
