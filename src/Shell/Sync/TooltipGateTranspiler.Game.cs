using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The Harmony bridge for <see cref="TooltipGateAnalysis"/>: reduces a
    /// method body to the abstract instruction list the analyzer works on, and
    /// swaps the call operand at every certified site for
    /// <see cref="TooltipHoverGate.IsOverOrHarvesting"/>.
    ///
    /// An operand swap and nothing else — never an insert, a removal or a
    /// reorder — so a method whose analysis comes back with no eligible site is
    /// byte-identical to vanilla, and a method with one is identical apart from
    /// which static it calls.
    /// </summary>
    public static class TooltipGateTranspiler
    {
        private static readonly MethodInfo GateMethod =
            AccessTools.Method(typeof(TooltipHoverGate), nameof(TooltipHoverGate.IsOverOrHarvesting));

        /// <summary>
        /// The patch both installers apply. Last priority so any other mod's
        /// transpiler on the same method sees vanilla-shaped IL: ours re-runs
        /// the analysis over whatever body reaches it, so a method another mod
        /// reshaped simply certifies fewer sites instead of two rewrites racing.
        /// </summary>
        internal static HarmonyMethod Patch()
        {
            return new HarmonyMethod(AccessTools.Method(typeof(TooltipGateTranspiler), nameof(Transpiler)))
            {
                priority = Priority.Last,
            };
        }

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            var body = new List<CodeInstruction>(instructions);
            try
            {
                IReadOnlyList<GateSiteResult> sites = Analyze(body, original);
                for (int i = 0; i < sites.Count; i++)
                {
                    if (sites[i].Verdict == GateVerdict.Eligible)
                    {
                        body[sites[i].SiteIndex].operand = GateMethod;
                    }
                }
            }
            catch (Exception ex)
            {
                // A broken analysis degrades to vanilla behaviour, never to a
                // corrupted method body.
                ModLogger.LimitedError("Tooltip gate transpiler (" + Describe(original) + ")", ex);
                return new List<CodeInstruction>(instructions);
            }
            return body;
        }

        internal static IReadOnlyList<GateSiteResult> Analyze(List<CodeInstruction> body, MethodBase original)
        {
            var labelIndices = new Dictionary<Label, int>();
            for (int i = 0; i < body.Count; i++)
            {
                List<Label> labels = body[i].labels;
                for (int j = 0; j < labels.Count; j++)
                {
                    labelIndices[labels[j]] = i;
                }
            }

            var abstracted = new List<GateInstruction>(body.Count);
            for (int i = 0; i < body.Count; i++)
            {
                CodeInstruction instruction = body[i];
                int target = -1;
                if (instruction.operand is Label label && labelIndices.TryGetValue(label, out int resolved))
                {
                    target = resolved;
                }
                abstracted.Add(new GateInstruction(i, instruction.opcode.Name, MemberName(instruction.operand),
                    target, SlotOf(instruction, original), PushedType(instruction, original)));
            }
            return TooltipGateAnalysis.Analyze(abstracted);
        }

        private static string MemberName(object operand)
        {
            var member = operand as MemberInfo;
            if ((!(member is MethodBase) && !(member is FieldInfo)) || member.DeclaringType == null)
            {
                return null;
            }
            return member.DeclaringType.FullName + "::" + member.Name;
        }

        private static int SlotOf(CodeInstruction instruction, MethodBase original)
        {
            string name = instruction.opcode.Name;
            if (!name.StartsWith("ldloc", StringComparison.Ordinal) && !name.StartsWith("stloc", StringComparison.Ordinal))
            {
                return -1;
            }
            if (instruction.operand is LocalBuilder builder)
            {
                return builder.LocalIndex;
            }
            if (instruction.operand is LocalVariableInfo local)
            {
                return local.LocalIndex;
            }
            return TrailingIndex(name);
        }

        /// <summary>
        /// The static type this instruction leaves on the stack, for the
        /// analysis's freshness rule. Returning null is always safe: the
        /// analysis refuses any span whose stores it cannot type.
        /// </summary>
        private static string PushedType(CodeInstruction instruction, MethodBase original)
        {
            string name = instruction.opcode.Name;
            object operand = instruction.operand;
            if (name.StartsWith("ldloc", StringComparison.Ordinal))
            {
                return NameOf(LocalType(instruction, original));
            }
            if (name.StartsWith("ldarg", StringComparison.Ordinal))
            {
                return NameOf(ArgumentType(instruction, original));
            }
            if (operand is FieldInfo field)
            {
                return NameOf(field.FieldType);
            }
            if (name == "newobj")
            {
                var constructor = operand as ConstructorInfo;
                return constructor == null ? null : NameOf(constructor.DeclaringType);
            }
            if (name == "call" || name == "callvirt")
            {
                var method = operand as MethodInfo;
                return method == null ? null : NameOf(method.ReturnType);
            }
            if (operand is Type type)
            {
                return name == "newarr" ? NameOf(type) + "[]" : NameOf(type);
            }
            return null;
        }

        private static Type LocalType(CodeInstruction instruction, MethodBase original)
        {
            if (instruction.operand is LocalBuilder builder)
            {
                return builder.LocalType;
            }
            if (instruction.operand is LocalVariableInfo local)
            {
                return local.LocalType;
            }
            int slot = TrailingIndex(instruction.opcode.Name);
            MethodBody body = original == null ? null : original.GetMethodBody();
            IList<LocalVariableInfo> locals = body == null ? null : body.LocalVariables;
            return locals != null && slot >= 0 && slot < locals.Count ? locals[slot].LocalType : null;
        }

        private static Type ArgumentType(CodeInstruction instruction, MethodBase original)
        {
            if (instruction.operand is ParameterInfo parameter)
            {
                return parameter.ParameterType;
            }
            if (original == null)
            {
                return null;
            }
            int slot = TrailingIndex(instruction.opcode.Name);
            if (slot < 0)
            {
                return null;
            }
            if (!original.IsStatic)
            {
                if (slot == 0)
                {
                    return original.DeclaringType;
                }
                slot--;
            }
            ParameterInfo[] parameters = original.GetParameters();
            return slot < parameters.Length ? parameters[slot].ParameterType : null;
        }

        private static string NameOf(Type type)
        {
            return type == null ? null : type.FullName;
        }

        /// <summary>Index baked into a short-form opcode name, as in "ldloc.2".</summary>
        private static int TrailingIndex(string opCodeName)
        {
            int dot = opCodeName.LastIndexOf('.');
            int value;
            return dot > 0 && int.TryParse(opCodeName.Substring(dot + 1), out value) ? value : -1;
        }

        internal static string Describe(MethodBase method)
        {
            if (method == null)
            {
                return "unknown method"; // l10n-exempt: log text for the gate's install report
            }
            return (method.DeclaringType != null ? method.DeclaringType.FullName : "?") + "." + method.Name;
        }
    }
}
