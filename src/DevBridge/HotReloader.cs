#if DEBUG
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using HarmonyLib;
using Verse;

namespace RimWorldAccess.DevBridge
{
    /// <summary>
    /// Method-body hot reload for the dev bridge. Mono cannot unload assemblies, so a full
    /// reload is impossible: the originally loaded assembly keeps the mod's identity (types,
    /// statics, Harmony registrations, scribing) forever. What CAN change safely is method
    /// bodies. A freshly built DLL is loaded from bytes (anonymous load context — GenTypes
    /// only enumerates mod-list assemblies, so the twin stays invisible to the game), each
    /// method is diffed against the original's pristine metadata, and every changed body is
    /// transpiled onto the OLD method with all member references remapped back to the old
    /// types, so static state stays where it lives and nothing ever runs a duplicate cctor.
    ///
    /// Refused loudly rather than guessed at: types whose field sets changed, changed static
    /// initializers, generic definitions, new [HarmonyPatch] classes (patches apply at
    /// startup), and any operand that fails to remap. Those land in the report's
    /// restart-required list and their old bodies keep running. Reverting an edit restores
    /// the pristine body: a method that diffs clean against the original is unpatched.
    ///
    /// Diffing is against ORIGINAL metadata, never against a prior reload: GetMethodBody()
    /// reads the assembly's immutable metadata, which Harmony patching does not touch, so
    /// every reload is an idempotent function of (pristine assembly, newest DLL).
    /// </summary>
    internal static class HotReloader
    {
        private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        private static readonly Harmony harmony = new Harmony("aaronr7734.rimworldaccess.hotreload");

        // Old method -> newest body source, read by the transpiler. Doubles as the ledger of
        // currently swapped methods so a re-reload can unpatch reverted ones.
        private static readonly Dictionary<MethodBase, MethodBase> swapTargets =
            new Dictionary<MethodBase, MethodBase>();

        private static Dictionary<short, OpCode> opcodeTable;

        // The most recently loaded twin — the only assembly whose members remap. Stale twins
        // from earlier reloads linger unloadably but nothing references them again.
        private static Assembly currentTwin;

        private static int twinCounter;

        /// <summary>
        /// In-place rename of the assembly (and module) name in the raw DLL bytes, BEFORE
        /// loading. The twin must not share the running assembly's name: Harmony compiles
        /// patches through MonoMod's Cecil backend, which re-resolves member references BY
        /// ASSEMBLY NAME at emit time — a same-named twin shadows the original there, so every
        /// swapped body silently bound to the twin's types and split all static state (caught
        /// live: a swapped bridge method enqueued onto the twin's dispatcher queue, which
        /// nothing drains). Same-length replacement keeps every metadata heap offset valid;
        /// string literals are safe (the #US heap is UTF-16, this scans ASCII).
        /// </summary>
        private static int MangleAssemblyName(byte[] bytes, string oldName, out string twinName)
        {
            twinCounter++;
            if (oldName.Length < 6)
            {
                twinName = null;
                return -1;
            }
            twinName = oldName.Substring(0, oldName.Length - 4) + twinCounter.ToString("D4");
            byte[] find = Encoding.ASCII.GetBytes(oldName);
            byte[] replace = Encoding.ASCII.GetBytes(twinName);
            int count = 0;
            for (int i = 0; i <= bytes.Length - find.Length; i++)
            {
                bool hit = true;
                for (int j = 0; j < find.Length; j++)
                {
                    if (bytes[i + j] != find[j])
                    {
                        hit = false;
                        break;
                    }
                }
                if (!hit)
                {
                    continue;
                }
                for (int j = 0; j < replace.Length; j++)
                {
                    bytes[i + j] = replace[j];
                }
                count++;
                i += find.Length - 1;
            }
            return count;
        }

        /// <summary>Runs the whole reload on the caller's thread — dispatch to the main thread first.</summary>
        internal static string Reload(string dllPath)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Assembly oldAsm = typeof(HotReloader).Assembly;
            if (string.IsNullOrEmpty(dllPath))
            {
                dllPath = oldAsm.Location;
            }
            if (!File.Exists(dllPath))
            {
                return "ERROR\nDLL not found: " + dllPath; // l10n-exempt: dev bridge output
            }
            byte[] bytes = File.ReadAllBytes(dllPath);
            string twinName;
            int renamed = MangleAssemblyName(bytes, oldAsm.GetName().Name, out twinName);
            if (renamed <= 0)
            {
                return "ERROR\nCould not find the assembly name in " + dllPath + " — wrong DLL?"; // l10n-exempt: dev bridge output
            }
            Assembly newAsm = Assembly.Load(bytes);
            if (newAsm.GetName().Name != twinName)
            {
                return "ERROR\nTwin rename failed: loaded as " + newAsm.GetName().Name; // l10n-exempt: dev bridge output
            }
            currentTwin = newAsm;
            string oldConfig = BuildConfig(oldAsm);
            string newConfig = BuildConfig(newAsm);
            if (oldConfig != newConfig)
            {
                // Swapping Release bodies into a Debug session silently disables the DEBUG-only
                // wiring that shared methods call into; refuse rather than confuse.
                return $"ERROR\nBuild configuration mismatch: running {oldConfig}, DLL is {newConfig}. Restart instead."; // l10n-exempt: dev bridge output
            }

            var swapped = new List<string>();
            var reverted = new List<string>();
            var restartRequired = new List<string>();
            var queue = new List<KeyValuePair<MethodBase, MethodBase>>();
            int newTypes = 0;
            int newMembers = 0;
            int unchanged = 0;

            foreach (Type nt in newAsm.GetTypes())
            {
                Type ot = oldAsm.GetType(nt.FullName);
                if (ot == null)
                {
                    newTypes++;
                    if (HasHarmonyPatchAttribute(nt))
                    {
                        restartRequired.Add("new Harmony patch class " + nt.FullName + " (patches only apply at startup)");
                    }
                    continue;
                }
                // Compiler-generated types (closure classes, iterator state machines) gain and
                // lose fields on ordinary lambda edits; their methods rely on the per-operand
                // refusal instead, so one edited lambda never bans a whole type.
                if (!IsCompilerGenerated(nt))
                {
                    string fieldDiff;
                    if (!FieldSetsMatch(ot, nt, out fieldDiff))
                    {
                        restartRequired.Add(nt.FullName + ": field layout changed (" + fieldDiff + ")");
                        continue;
                    }
                }
                foreach (MethodBase nm in DeclaredBodies(nt))
                {
                    MethodBase om = FindMatch(ot, nm);
                    if (om == null)
                    {
                        newMembers++;
                        continue;
                    }
                    bool equal;
                    try
                    {
                        equal = BodiesEqual(om, nm);
                    }
                    catch (Exception ex)
                    {
                        restartRequired.Add(Describe(om) + ": body diff failed (" + ex.Message + ")");
                        continue;
                    }
                    if (equal)
                    {
                        unchanged++;
                        if (swapTargets.ContainsKey(om))
                        {
                            // Edit reverted since an earlier reload: drop the patch so the
                            // pristine body runs again.
                            harmony.Unpatch(om, HarmonyPatchType.Transpiler, harmony.Id);
                            swapTargets.Remove(om);
                            reverted.Add(Describe(om));
                        }
                        continue;
                    }
                    if (IsStaticConstructor(nm))
                    {
                        restartRequired.Add(Describe(om) + ": static initializer changed (already ran; restart to re-run)");
                        continue;
                    }
                    if (nt.IsGenericTypeDefinition || nm.IsGenericMethodDefinition)
                    {
                        restartRequired.Add(Describe(om) + ": generic definition changed (cannot body-swap)");
                        continue;
                    }
                    queue.Add(new KeyValuePair<MethodBase, MethodBase>(om, nm));
                }
            }

            foreach (var pair in queue)
            {
                MethodBase om = pair.Key;
                try
                {
                    if (swapTargets.ContainsKey(om))
                    {
                        harmony.Unpatch(om, HarmonyPatchType.Transpiler, harmony.Id);
                    }
                    swapTargets[om] = pair.Value;
                    harmony.Patch(om, transpiler: new HarmonyMethod(typeof(HotReloader), nameof(SwapTranspiler)));
                    swapped.Add(Describe(om));
                }
                catch (Exception ex)
                {
                    swapTargets.Remove(om);
                    restartRequired.Add(Describe(om) + ": swap failed (" + FirstLine(ex.Message) + ")");
                }
            }

            sw.Stop();
            var report = new StringBuilder();
            report.AppendLine(restartRequired.Count == 0 ? "OK" : "OK (with restart-required items)");
            report.AppendLine($"swapped={swapped.Count} reverted={reverted.Count} unchanged={unchanged} newTypes={newTypes} newMembers={newMembers} elapsedMs={sw.ElapsedMilliseconds}");
            AppendList(report, "swapped", swapped);
            AppendList(report, "reverted to original", reverted);
            AppendList(report, "RESTART REQUIRED", restartRequired);
            Log.Message("[RimWorld Access] Hot reload: " + swapped.Count + " method(s) swapped, "
                + restartRequired.Count + " restart-required item(s).");
            return report.ToString();
        }

        private static void AppendList(StringBuilder sb, string title, List<string> items)
        {
            if (items.Count == 0)
            {
                return;
            }
            sb.AppendLine(title + ":");
            foreach (string item in items)
            {
                sb.AppendLine("  " + item);
            }
        }

        private static string FirstLine(string text)
        {
            int nl = text.IndexOf('\n');
            return nl >= 0 ? text.Substring(0, nl) : text;
        }

        private static string BuildConfig(Assembly asm)
        {
            var attr = (AssemblyConfigurationAttribute)Attribute.GetCustomAttribute(asm, typeof(AssemblyConfigurationAttribute));
            return attr != null && !string.IsNullOrEmpty(attr.Configuration) ? attr.Configuration : "unknown";
        }

        private static bool HasHarmonyPatchAttribute(Type type)
        {
            foreach (object attr in type.GetCustomAttributes(inherit: false))
            {
                if (attr is HarmonyPatch)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsStaticConstructor(MethodBase m)
        {
            return m is ConstructorInfo && m.IsStatic;
        }

        private static bool IsCompilerGenerated(Type t)
        {
            return t.Name.IndexOf('<') >= 0
                || t.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), inherit: false);
        }

        /// <summary>Every declared method and instance constructor with an IL body.</summary>
        private static IEnumerable<MethodBase> DeclaredBodies(Type type)
        {
            foreach (MethodInfo m in type.GetMethods(All))
            {
                if (!m.IsAbstract && m.GetMethodBody() != null)
                {
                    yield return m;
                }
            }
            foreach (ConstructorInfo c in type.GetConstructors(All))
            {
                if (c.GetMethodBody() != null)
                {
                    yield return c;
                }
            }
        }

        /// <summary>The old method matching <paramref name="nm"/> by name, arity and parameter type names, or null.</summary>
        private static MethodBase FindMatch(Type ot, MethodBase nm)
        {
            ParameterInfo[] np = nm.GetParameters();
            foreach (MethodBase om in DeclaredBodies(ot))
            {
                if (om.Name != nm.Name || om is ConstructorInfo != nm is ConstructorInfo)
                {
                    continue;
                }
                if (om.IsStatic != nm.IsStatic)
                {
                    continue;
                }
                if (GenericArity(om) != GenericArity(nm))
                {
                    continue;
                }
                ParameterInfo[] op = om.GetParameters();
                if (op.Length != np.Length)
                {
                    continue;
                }
                bool match = true;
                for (int i = 0; i < op.Length; i++)
                {
                    if (TypeDesc(op[i].ParameterType) != TypeDesc(np[i].ParameterType))
                    {
                        match = false;
                        break;
                    }
                }
                if (match)
                {
                    return om;
                }
            }
            return null;
        }

        private static int GenericArity(MethodBase m)
        {
            return m.IsGenericMethodDefinition || m.IsGenericMethod ? m.GetGenericArguments().Length : 0;
        }

        /// <summary>
        /// Stable, assembly-agnostic name for a type across the running assembly and a renamed
        /// twin. Built recursively because FullName embeds ASSEMBLY-QUALIFIED generic argument
        /// names, which the twin's rename would falsely diff. Generic parameters have no
        /// FullName; Name suffices.
        /// </summary>
        private static string TypeDesc(Type t)
        {
            if (t == null)
            {
                return "";
            }
            if (t.IsGenericParameter)
            {
                return t.Name;
            }
            if (t.HasElementType)
            {
                string suffix = t.IsArray
                    ? (t.GetArrayRank() == 1 ? "[]" : "[" + new string(',', t.GetArrayRank() - 1) + "]")
                    : (t.IsByRef ? "&" : "*");
                return TypeDesc(t.GetElementType()) + suffix;
            }
            if (t.IsGenericType && !t.IsGenericTypeDefinition)
            {
                var sb = new StringBuilder(TypeDesc(t.GetGenericTypeDefinition()));
                sb.Append('[');
                Type[] args = t.GetGenericArguments();
                for (int i = 0; i < args.Length; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(',');
                    }
                    sb.Append(TypeDesc(args[i]));
                }
                sb.Append(']');
                return sb.ToString();
            }
            return t.FullName ?? t.Name;
        }

        private static string MemberDesc(MemberInfo m)
        {
            Type type = m as Type;
            if (type != null)
            {
                return "T|" + TypeDesc(type);
            }
            return m.MemberType + "|" + TypeDesc(m.DeclaringType) + "|" + m;
        }

        private static string Describe(MethodBase m)
        {
            return (m.DeclaringType != null ? m.DeclaringType.FullName : "?") + "." + m.Name;
        }

        /// <summary>
        /// Declared field sets (name, staticness, type name) must match before any of a type's
        /// methods may swap: a new body compiled against a different layout reads garbage, and a
        /// remapped reference to a field the old type lacks cannot resolve at all.
        /// </summary>
        private static bool FieldSetsMatch(Type ot, Type nt, out string diff)
        {
            var oldFields = new HashSet<string>();
            foreach (FieldInfo f in ot.GetFields(All))
            {
                oldFields.Add(FieldDesc(f));
            }
            var newFields = new HashSet<string>();
            foreach (FieldInfo f in nt.GetFields(All))
            {
                newFields.Add(FieldDesc(f));
            }
            if (oldFields.SetEquals(newFields))
            {
                diff = null;
                return true;
            }
            var only = new List<string>();
            foreach (string f in newFields)
            {
                if (!oldFields.Contains(f))
                {
                    only.Add("+" + f);
                }
            }
            foreach (string f in oldFields)
            {
                if (!newFields.Contains(f))
                {
                    only.Add("-" + f);
                }
            }
            diff = string.Join(", ", only.ToArray());
            return false;
        }

        private static string FieldDesc(FieldInfo f)
        {
            return (f.IsStatic ? "s:" : "i:") + f.Name + ":" + TypeDesc(f.FieldType);
        }

        // ---- Body diff: token-normalized IL comparison against pristine metadata ----

        /// <summary>
        /// Structural IL equality: same locals, same exception clauses, same opcode stream, and
        /// every metadata-token operand resolves to a same-named member. Token VALUES are never
        /// compared — rebuilding shifts every table index even for untouched methods.
        /// </summary>
        private static bool BodiesEqual(MethodBase om, MethodBase nm)
        {
            MethodBody ob = om.GetMethodBody();
            MethodBody nb = nm.GetMethodBody();
            if (ob == null || nb == null)
            {
                return ob == null && nb == null;
            }
            IList<LocalVariableInfo> ol = ob.LocalVariables;
            IList<LocalVariableInfo> nl = nb.LocalVariables;
            if (ol.Count != nl.Count)
            {
                return false;
            }
            for (int i = 0; i < ol.Count; i++)
            {
                if (ol[i].IsPinned != nl[i].IsPinned || TypeDesc(ol[i].LocalType) != TypeDesc(nl[i].LocalType))
                {
                    return false;
                }
            }
            if (!ClausesEqual(ob, nb))
            {
                return false;
            }
            byte[] oil = ob.GetILAsByteArray();
            byte[] nil = nb.GetILAsByteArray();
            if (oil.Length != nil.Length)
            {
                return false;
            }
            int p = 0;
            while (p < oil.Length)
            {
                if (oil[p] != nil[p])
                {
                    return false;
                }
                OpCode op;
                if (oil[p] == 0xFE)
                {
                    if (p + 1 >= oil.Length || oil[p + 1] != nil[p + 1])
                    {
                        return false;
                    }
                    op = LookupOpCode((short)(0xFE00 | oil[p + 1]));
                    p += 2;
                }
                else
                {
                    op = LookupOpCode(oil[p]);
                    p += 1;
                }
                switch (op.OperandType)
                {
                    case OperandType.InlineNone:
                        break;
                    case OperandType.ShortInlineBrTarget:
                    case OperandType.ShortInlineI:
                    case OperandType.ShortInlineVar:
                        if (oil[p] != nil[p]) return false;
                        p += 1;
                        break;
                    case OperandType.InlineVar:
                        if (!BytesEqual(oil, nil, p, 2)) return false;
                        p += 2;
                        break;
                    case OperandType.InlineI:
                    case OperandType.InlineBrTarget:
                    case OperandType.ShortInlineR:
                        if (!BytesEqual(oil, nil, p, 4)) return false;
                        p += 4;
                        break;
                    case OperandType.InlineI8:
                    case OperandType.InlineR:
                        if (!BytesEqual(oil, nil, p, 8)) return false;
                        p += 8;
                        break;
                    case OperandType.InlineSwitch:
                    {
                        if (!BytesEqual(oil, nil, p, 4)) return false;
                        int count = BitConverter.ToInt32(oil, p);
                        p += 4;
                        if (!BytesEqual(oil, nil, p, count * 4)) return false;
                        p += count * 4;
                        break;
                    }
                    case OperandType.InlineString:
                    {
                        int otok = BitConverter.ToInt32(oil, p);
                        int ntok = BitConverter.ToInt32(nil, p);
                        if (om.Module.ResolveString(otok) != nm.Module.ResolveString(ntok)) return false;
                        p += 4;
                        break;
                    }
                    case OperandType.InlineField:
                    case OperandType.InlineMethod:
                    case OperandType.InlineType:
                    case OperandType.InlineTok:
                    {
                        int otok = BitConverter.ToInt32(oil, p);
                        int ntok = BitConverter.ToInt32(nil, p);
                        MemberInfo omem = ResolveMember(om, otok);
                        MemberInfo nmem = ResolveMember(nm, ntok);
                        if (omem == null || nmem == null || MemberDesc(omem) != MemberDesc(nmem)) return false;
                        p += 4;
                        break;
                    }
                    default:
                        // InlineSig (calli) and anything unforeseen: never claim equality.
                        return false;
                }
            }
            return true;
        }

        private static bool BytesEqual(byte[] a, byte[] b, int start, int count)
        {
            for (int i = start; i < start + count; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }
            return true;
        }

        private static bool ClausesEqual(MethodBody ob, MethodBody nb)
        {
            IList<ExceptionHandlingClause> oc = ob.ExceptionHandlingClauses;
            IList<ExceptionHandlingClause> nc = nb.ExceptionHandlingClauses;
            if (oc.Count != nc.Count)
            {
                return false;
            }
            for (int i = 0; i < oc.Count; i++)
            {
                if (oc[i].Flags != nc[i].Flags
                    || oc[i].TryOffset != nc[i].TryOffset || oc[i].TryLength != nc[i].TryLength
                    || oc[i].HandlerOffset != nc[i].HandlerOffset || oc[i].HandlerLength != nc[i].HandlerLength)
                {
                    return false;
                }
                if (oc[i].Flags == ExceptionHandlingClauseOptions.Clause
                    && TypeDesc(oc[i].CatchType) != TypeDesc(nc[i].CatchType))
                {
                    return false;
                }
                if (oc[i].Flags == ExceptionHandlingClauseOptions.Filter
                    && oc[i].FilterOffset != nc[i].FilterOffset)
                {
                    return false;
                }
            }
            return true;
        }

        private static MemberInfo ResolveMember(MethodBase context, int token)
        {
            try
            {
                Type[] typeArgs = context.DeclaringType != null && context.DeclaringType.IsGenericType
                    ? context.DeclaringType.GetGenericArguments()
                    : null;
                Type[] methodArgs = context.IsGenericMethod || context.IsGenericMethodDefinition
                    ? context.GetGenericArguments()
                    : null;
                return context.Module.ResolveMember(token, typeArgs, methodArgs);
            }
            catch
            {
                return null;
            }
        }

        private static OpCode LookupOpCode(short value)
        {
            if (opcodeTable == null)
            {
                var table = new Dictionary<short, OpCode>();
                foreach (FieldInfo f in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
                {
                    var op = (OpCode)f.GetValue(null);
                    table[op.Value] = op;
                }
                opcodeTable = table;
            }
            return opcodeTable[value];
        }

        // ---- The swap: serve the new body with every operand remapped old-assembly-ward ----

        /// <summary>
        /// Ignores the original instructions and emits the new method's body. Every member
        /// reference whose type also exists in the old assembly is remapped to the OLD member,
        /// so state and identity stay with the running types; references to genuinely new types
        /// pass through and execute in the new assembly. Locals and labels are declared into
        /// this patch's own generator by GetOriginalInstructions.
        /// </summary>
        private static IEnumerable<CodeInstruction> SwapTranspiler(IEnumerable<CodeInstruction> instructions,
            ILGenerator generator, MethodBase original)
        {
            MethodBase nm;
            if (!swapTargets.TryGetValue(original, out nm))
            {
                return instructions;
            }
            List<CodeInstruction> body = PatchProcessor.GetOriginalInstructions(nm, generator);
            foreach (CodeInstruction ci in body)
            {
                ci.operand = RemapOperand(ci.operand);
                foreach (ExceptionBlock block in ci.blocks)
                {
                    if (block.catchType != null)
                    {
                        block.catchType = RemapType(block.catchType);
                    }
                }
            }
            return body;
        }

        private static object RemapOperand(object operand)
        {
            Type type = operand as Type;
            if (type != null)
            {
                return RemapType(type);
            }
            MethodBase method = operand as MethodBase;
            if (method != null)
            {
                return RemapMethod(method);
            }
            FieldInfo field = operand as FieldInfo;
            if (field != null)
            {
                return RemapField(field);
            }
            return operand;
        }

        private static Type RemapType(Type t)
        {
            if (t == null || t.IsGenericParameter)
            {
                return t;
            }
            if (t.IsArray)
            {
                Type elem = RemapType(t.GetElementType());
                int rank = t.GetArrayRank();
                return rank == 1 ? elem.MakeArrayType() : elem.MakeArrayType(rank);
            }
            if (t.IsByRef)
            {
                return RemapType(t.GetElementType()).MakeByRefType();
            }
            if (t.IsPointer)
            {
                return RemapType(t.GetElementType()).MakePointerType();
            }
            if (t.IsGenericType && !t.IsGenericTypeDefinition)
            {
                Type def = RemapType(t.GetGenericTypeDefinition());
                Type[] args = t.GetGenericArguments();
                bool changed = def != t.GetGenericTypeDefinition();
                for (int i = 0; i < args.Length; i++)
                {
                    Type mapped = RemapType(args[i]);
                    changed |= mapped != args[i];
                    args[i] = mapped;
                }
                return changed ? def.MakeGenericType(args) : t;
            }
            if (t.Assembly != currentTwin)
            {
                return t;
            }
            return typeof(HotReloader).Assembly.GetType(t.FullName) ?? t;
        }

        private static FieldInfo RemapField(FieldInfo f)
        {
            Type dt = RemapType(f.DeclaringType);
            if (dt == f.DeclaringType)
            {
                return f;
            }
            FieldInfo mapped = dt.GetField(f.Name, All | BindingFlags.FlattenHierarchy);
            if (mapped == null)
            {
                throw new MissingFieldException(dt.FullName, f.Name);
            }
            return mapped;
        }

        private static MethodBase RemapMethod(MethodBase m)
        {
            if (m.IsGenericMethod && !m.IsGenericMethodDefinition)
            {
                var def = (MethodInfo)RemapMethod(((MethodInfo)m).GetGenericMethodDefinition());
                Type[] args = ((MethodInfo)m).GetGenericArguments();
                for (int i = 0; i < args.Length; i++)
                {
                    args[i] = RemapType(args[i]);
                }
                return def.MakeGenericMethod(args);
            }
            Type dt = RemapType(m.DeclaringType);
            if (dt == m.DeclaringType)
            {
                return m;
            }
            ParameterInfo[] mp = m.GetParameters();
            foreach (MethodBase candidate in AllMethods(dt))
            {
                if (candidate.Name != m.Name
                    || candidate is ConstructorInfo != m is ConstructorInfo
                    || candidate.IsStatic != m.IsStatic
                    || GenericArity(candidate) != GenericArity(m))
                {
                    continue;
                }
                ParameterInfo[] cp = candidate.GetParameters();
                if (cp.Length != mp.Length)
                {
                    continue;
                }
                bool match = true;
                for (int i = 0; i < cp.Length; i++)
                {
                    if (TypeDesc(cp[i].ParameterType) != TypeDesc(RemapType(mp[i].ParameterType)))
                    {
                        match = false;
                        break;
                    }
                }
                if (match)
                {
                    return candidate;
                }
            }
            throw new MissingMethodException(dt.FullName, m.Name);
        }

        /// <summary>Every declared method and constructor, bodyless ones included — a call operand may target an abstract or extern member.</summary>
        private static IEnumerable<MethodBase> AllMethods(Type type)
        {
            foreach (MethodInfo m in type.GetMethods(All))
            {
                yield return m;
            }
            foreach (ConstructorInfo c in type.GetConstructors(All))
            {
                yield return c;
            }
        }
    }
}
#endif
