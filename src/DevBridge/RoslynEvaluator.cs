#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RimWorldAccess.DevBridge
{
    /// <summary>
    /// Compiles and executes arbitrary C# script text against the live game using Roslyn
    /// scripting. The script body runs with the game's assemblies referenced and common
    /// namespaces imported, so it can call <c>Find.*</c>, <c>DefDatabase&lt;T&gt;</c>,
    /// <c>"key".Translate()</c>, etc. directly. The trailing expression's value is returned.
    ///
    /// Roslyn is driven ENTIRELY through reflection: this type must carry no typeref to any
    /// Microsoft.CodeAnalysis type, not even in a field or a lambda closure. The Roslyn DLLs
    /// live in DevBridgeLibs/ (outside Assemblies/, whose boot sweep chokes on them — see
    /// RoslynRuntimeLoader) and load on first eval, while our own assembly is GetTypes-swept
    /// during startup (CompatBootstrap, vanilla's ReportProbablyMissingAttributes). Mono
    /// resolves field types when a type first loads and caches a failure permanently, so a
    /// single typed Roslyn field here poisons this type for the whole session.
    ///
    /// MUST be invoked on the main thread (see <see cref="MainThreadDispatcher"/>) because the
    /// script touches non-thread-safe game state. Compilation also happens here, briefly
    /// blocking the frame - acceptable for a dev-only tool.
    /// </summary>
    internal static class RoslynEvaluator
    {
        private static object options;              // Microsoft.CodeAnalysis.Scripting.ScriptOptions
        private static MethodInfo evaluateAsync;    // CSharpScript.EvaluateAsync(string, ScriptOptions, object, Type, CancellationToken)
        private static Type compilationErrorExceptionType;
        private static bool initialized;
        private static string initError;

        private static void EnsureInit()
        {
            if (initialized) return;
            initialized = true;
            try
            {
                RoslynRuntimeLoader.EnsureLoaded();

                Assembly scriptingAsm = FindLoaded("Microsoft.CodeAnalysis.Scripting");
                Assembly csharpScriptingAsm = FindLoaded("Microsoft.CodeAnalysis.CSharp.Scripting");
                if (scriptingAsm == null || csharpScriptingAsm == null)
                {
                    initError = "Roslyn scripting assemblies are not loaded; is the mod's DevBridgeLibs folder missing?";
                    return;
                }

                Type scriptOptionsType = scriptingAsm.GetType("Microsoft.CodeAnalysis.Scripting.ScriptOptions", throwOnError: true);
                compilationErrorExceptionType = scriptingAsm.GetType("Microsoft.CodeAnalysis.Scripting.CompilationErrorException", throwOnError: true);
                Type csharpScriptType = csharpScriptingAsm.GetType("Microsoft.CodeAnalysis.CSharp.Scripting.CSharpScript", throwOnError: true);

                // Reference every loaded assembly that lives on disk. This pulls in
                // Assembly-CSharp (RimWorld), the Unity assemblies, Harmony, and our own
                // mod DLL, so scripts can reach anything the running game can.
                var references = AppDomain.CurrentDomain.GetAssemblies()
                    .Where(a => !a.IsDynamic)
                    .Where(a =>
                    {
                        try { return !string.IsNullOrEmpty(a.Location); }
                        catch { return false; }
                    })
                    .ToList();

                object opts = scriptOptionsType
                    .GetProperty("Default", BindingFlags.Public | BindingFlags.Static)
                    .GetValue(null);
                opts = scriptOptionsType
                    .GetMethod("WithReferences", new[] { typeof(IEnumerable<Assembly>) })
                    .Invoke(opts, new object[] { references });
                opts = scriptOptionsType
                    .GetMethod("WithImports", new[] { typeof(string[]) })
                    .Invoke(opts, new object[]
                    {
                        new[]
                        {
                            "System",
                            "System.Linq",
                            "System.Collections",
                            "System.Collections.Generic",
                            "System.Text",
                            "Verse",
                            "RimWorld",
                            "RimWorld.Planet",
                            "UnityEngine",
                            "RimWorldAccess"
                        }
                    });

                evaluateAsync = csharpScriptType
                    .GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .Single(m => m.Name == "EvaluateAsync" && !m.IsGenericMethod);

                options = opts;
            }
            catch (Exception e)
            {
                initError = e.ToString();
            }
        }

        private static Assembly FindLoaded(string simpleName)
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == simpleName);
        }

        internal static bool IsReady
        {
            get { EnsureInit(); return options != null; }
        }

        internal static string InitError
        {
            get { EnsureInit(); return initError; }
        }

        /// <summary>
        /// Evaluate a C# script. Returns a result string prefixed with "OK\n" on success or
        /// "ERROR\n" on failure (compile or runtime). Any text the script writes to
        /// Console.Out/Error during evaluation is captured and included.
        /// </summary>
        internal static string Eval(string code)
        {
            EnsureInit();
            if (options == null)
                return "ERROR\nRoslyn failed to initialize:\n" + initError;

            var captured = new StringWriter();
            TextWriter prevOut = Console.Out;
            TextWriter prevErr = Console.Error;
            try
            {
                Console.SetOut(captured);
                Console.SetError(captured);

                object task = evaluateAsync.Invoke(null,
                    new object[] { code, options, null, null, default(CancellationToken) });
                object result = ((Task<object>)task).GetAwaiter().GetResult();

                var sb = new StringBuilder("OK\n");
                string output = captured.ToString();
                if (output.Length > 0)
                    sb.Append(output).Append(output.EndsWith("\n") ? "" : "\n");
                sb.Append(FormatResult(result));
                return sb.ToString();
            }
            catch (Exception e)
            {
                Exception inner = UnwrapToInnermost(e);

                // Compilation errors keep their full diagnostic output — those are already
                // good and are not a runtime exception this method needs to unwrap. They
                // arrive wrapped in TargetInvocationException from the reflective Invoke.
                if (compilationErrorExceptionType.IsInstanceOfType(inner))
                    return "ERROR (compile)\n" + FormatDiagnostics(inner);

                string output = captured.ToString();
                string summary = inner.GetType().FullName + ": " + inner.Message + "\n" + FormatFilteredStackTrace(inner);
                return "ERROR\n" + (output.Length > 0 ? output + "\n" : "") + summary;
            }
            finally
            {
                Console.SetOut(prevOut);
                Console.SetError(prevErr);
            }
        }

        private static string FormatDiagnostics(Exception compilationError)
        {
            object diagnostics = compilationError.GetType()
                .GetProperty("Diagnostics")
                .GetValue(compilationError);
            var lines = new List<string>();
            foreach (object d in (IEnumerable)diagnostics)
                lines.Add(d.ToString());
            return string.Join("\n", lines);
        }

        /// <summary>
        /// Render a result value for the HTTP response. Enumerables (other than strings) are
        /// expanded one-per-line and capped so a huge DefDatabase dump can't flood the wire.
        /// </summary>
        private static string FormatResult(object result)
        {
            if (result == null)
                return "(null)";

            if (result is string s)
                return s;

            if (result is IEnumerable en && !(result is IDictionary))
            {
                const int cap = 500;
                var sb = new StringBuilder();
                int count = 0;
                foreach (object item in en)
                {
                    if (count >= cap)
                    {
                        sb.Append("... (truncated at ").Append(cap).Append(" items)\n");
                        break;
                    }
                    sb.Append(item == null ? "(null)" : item.ToString()).Append('\n');
                    count++;
                }
                if (count == 0)
                    return "(empty sequence)";
                return sb.ToString().TrimEnd('\n');
            }

            return result.ToString();
        }

        /// <summary>
        /// A script's own exception almost always arrives wrapped (script host invocation, awaited
        /// Task, reflective Invoke) — unwrap TargetInvocationException and AggregateException chains
        /// down to the exception that actually explains the failure.
        /// </summary>
        private static Exception UnwrapToInnermost(Exception e)
        {
            Exception current = e;
            while (true)
            {
                if (current is TargetInvocationException tie && tie.InnerException != null)
                {
                    current = tie.InnerException;
                    continue;
                }
                if (current is AggregateException ae && ae.InnerException != null)
                {
                    current = ae.InnerException;
                    continue;
                }
                return current;
            }
        }

        /// <summary>
        /// A raw script exception's stack trace is mostly Roslyn scripting host frames and framework
        /// noise the caller can't act on — keep only frames whose declaring assembly is this mod or the
        /// game (Assembly-CSharp), capped at 6, with a count of what got hidden.
        /// </summary>
        private static string FormatFilteredStackTrace(Exception e)
        {
            const int cap = 6;
            StackTrace trace = new StackTrace(e, fNeedFileInfo: true);
            StackFrame[] frames = trace.GetFrames();
            if (frames == null || frames.Length == 0)
            {
                string noTrace = "(no stack trace)";
                return noTrace;
            }

            string modAssemblyName = typeof(RoslynEvaluator).Assembly.GetName().Name;
            var sb = new StringBuilder();
            int shown = 0;
            int hidden = 0;
            for (int i = 0; i < frames.Length; i++)
            {
                MethodBase method = frames[i].GetMethod();
                if (method == null)
                    continue;
                string asmName = method.Module.Assembly.GetName().Name;
                if (asmName != modAssemblyName && asmName != "Assembly-CSharp")
                {
                    hidden++;
                    continue;
                }
                if (shown >= cap)
                {
                    hidden++;
                    continue;
                }
                string typeName = method.DeclaringType != null ? method.DeclaringType.FullName + "." : "";
                sb.Append("  at ").Append(typeName).Append(method.Name).Append('\n');
                shown++;
            }
            if (hidden > 0)
                sb.Append('(').Append(hidden).Append(" framework frames hidden)");
            return sb.ToString().TrimEnd('\n');
        }
    }
}
#endif
