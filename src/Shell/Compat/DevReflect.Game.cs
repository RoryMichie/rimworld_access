#if DEBUG
using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Dev-bridge reflection helper. Every Roslyn eval
    /// (<see cref="RimWorldAccess.DevBridge.RoslynEvaluator"/>) compiles into its own throwaway
    /// assembly with no access to our internals — private/internal fields, properties, and methods — so
    /// bridge eval scripts were re-inventing the same hand-rolled reflection walker inline every time.
    /// This does the walking once, and forgivingly: every member here returns null/default on a miss
    /// instead of throwing, recording what went wrong in
    /// <see cref="LastMiss"/> so a null result over the bridge is diagnosable rather than a dead
    /// end.
    /// </summary>
    public static class DevReflect
    {
        private const BindingFlags AllInstanceDeclared = BindingFlags.Instance | BindingFlags.Public
            | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        private const BindingFlags AllStaticDeclared = BindingFlags.Static | BindingFlags.Public
            | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        /// <summary>
        /// One-line description of the most recent lookup that found nothing; cleared (set to
        /// null) on the next successful lookup so a stale miss never masquerades as the reason
        /// for a later, unrelated null.
        /// </summary>
        public static string LastMiss { get; private set; }

        private static void Miss(string what)
        {
            LastMiss = what;
        }

        private static void Hit()
        {
            LastMiss = null;
        }

        /// <summary>Resolves a type by name: AccessTools.TypeByName first (every loaded assembly), then a short-name scan of this mod's own assembly.</summary>
        public static Type T(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                Miss("T: empty name");
                return null;
            }
            Type found = AccessTools.TypeByName(name);
            if (found != null)
            {
                Hit();
                return found;
            }
            found = typeof(DevReflect).Assembly.GetTypes().FirstOrDefault(t => t.Name == name);
            if (found != null)
            {
                Hit();
                return found;
            }
            Miss("T: no type named '" + name + "'");
            return null;
        }

        private static MemberInfo FindMember(Type type, string name, bool isStatic)
        {
            BindingFlags flags = isStatic ? AllStaticDeclared : AllInstanceDeclared;
            for (Type t = type; t != null; t = t.BaseType)
            {
                MemberInfo m = t.GetField(name, flags);
                if (m != null)
                {
                    return m;
                }
                m = t.GetProperty(name, flags);
                if (m != null)
                {
                    return m;
                }
            }
            return null;
        }

        /// <summary>Instance field-or-property read, walking up the type chain, every binding flag.</summary>
        public static object Get(object target, string member)
        {
            if (target == null)
            {
                Miss("Get: null target for '" + member + "'");
                return null;
            }
            MemberInfo m = FindMember(target.GetType(), member, isStatic: false);
            if (m == null)
            {
                Miss("Get: no member '" + member + "' on " + target.GetType().FullName);
                return null;
            }
            try
            {
                object value = m is FieldInfo f ? f.GetValue(target) : ((PropertyInfo)m).GetValue(target);
                Hit();
                return value;
            }
            catch (Exception ex)
            {
                Miss("Get: '" + member + "' threw " + UnwrapExceptionTypeName(ex));
                return null;
            }
        }

        public static object GetStatic(string typeName, string member)
        {
            Type type = T(typeName);
            if (type == null)
            {
                Miss("GetStatic: no type '" + typeName + "'");
                return null;
            }
            MemberInfo m = FindMember(type, member, isStatic: true);
            if (m == null)
            {
                Miss("GetStatic: no member '" + member + "' on " + typeName);
                return null;
            }
            try
            {
                object value = m is FieldInfo f ? f.GetValue(null) : ((PropertyInfo)m).GetValue(null);
                Hit();
                return value;
            }
            catch (Exception ex)
            {
                Miss("GetStatic: '" + member + "' threw " + UnwrapExceptionTypeName(ex));
                return null;
            }
        }

        // MUTATION-C: this is a generic DEBUG-only reflection primitive for the dev bridge, not a
        // player-facing mutation with a specific vanilla vehicle to ride — it is the direct
        // analogue of RoslynEvaluator's arbitrary compiled-script execution (which can already
        // write anything through the same reflection APIs, just dynamically, so the text scan
        // never sees it). No single Try*/CanAccept gate applies to an arbitrary member-by-name
        // write; never reachable from any player-facing code path.
        public static void Set(object target, string member, object value)
        {
            if (target == null)
            {
                Miss("Set: null target for '" + member + "'");
                return;
            }
            MemberInfo m = FindMember(target.GetType(), member, isStatic: false);
            if (m == null)
            {
                Miss("Set: no member '" + member + "' on " + target.GetType().FullName);
                return;
            }
            try
            {
                // MUTATION-C: generic DEBUG-only reflection write (see the doc comment above)
                if (m is FieldInfo f) f.SetValue(target, value); else ((PropertyInfo)m).SetValue(target, value);
                Hit();
            }
            catch (Exception ex)
            {
                Miss("Set: '" + member + "' threw " + UnwrapExceptionTypeName(ex));
            }
        }

        // MUTATION-C: same dev-bridge-only generic reflection primitive as Set above, for a
        // static member instead of an instance one.
        public static void SetStatic(string typeName, string member, object value)
        {
            Type type = T(typeName);
            if (type == null)
            {
                Miss("SetStatic: no type '" + typeName + "'");
                return;
            }
            MemberInfo m = FindMember(type, member, isStatic: true);
            if (m == null)
            {
                Miss("SetStatic: no member '" + member + "' on " + typeName);
                return;
            }
            try
            {
                // MUTATION-C: generic DEBUG-only reflection write (see Set's doc comment above)
                if (m is FieldInfo f) f.SetValue(null, value); else ((PropertyInfo)m).SetValue(null, value);
                Hit();
            }
            catch (Exception ex)
            {
                Miss("SetStatic: '" + member + "' threw " + UnwrapExceptionTypeName(ex));
            }
        }

        private static MethodInfo FindMethod(Type type, string name, int argCount, bool isStatic)
        {
            BindingFlags flags = (isStatic ? BindingFlags.Static : BindingFlags.Instance)
                | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            for (Type t = type; t != null; t = t.BaseType)
            {
                MethodInfo match = t.GetMethods(flags)
                    .FirstOrDefault(c => c.Name == name && c.GetParameters().Length == argCount);
                if (match != null)
                {
                    return match;
                }
            }
            return null;
        }

        /// <summary>First method whose name and parameter count match; Invoke coerces the arguments.</summary>
        public static object Call(object target, string member, params object[] args)
        {
            if (target == null)
            {
                Miss("Call: null target for '" + member + "'");
                return null;
            }
            args = args ?? Array.Empty<object>();
            MethodInfo method = FindMethod(target.GetType(), member, args.Length, isStatic: false);
            if (method == null)
            {
                Miss("Call: no method '" + member + "' with " + args.Length + " arg(s) on " + target.GetType().FullName);
                return null;
            }
            try
            {
                object result = method.Invoke(target, args);
                Hit();
                return result;
            }
            catch (Exception ex)
            {
                Miss("Call: '" + member + "' threw " + UnwrapExceptionTypeName(ex));
                return null;
            }
        }

        public static object CallStatic(string typeName, string member, params object[] args)
        {
            Type type = T(typeName);
            if (type == null)
            {
                Miss("CallStatic: no type '" + typeName + "'");
                return null;
            }
            args = args ?? Array.Empty<object>();
            MethodInfo method = FindMethod(type, member, args.Length, isStatic: true);
            if (method == null)
            {
                Miss("CallStatic: no method '" + member + "' with " + args.Length + " arg(s) on " + typeName);
                return null;
            }
            try
            {
                object result = method.Invoke(null, args);
                Hit();
                return result;
            }
            catch (Exception ex)
            {
                Miss("CallStatic: '" + member + "' threw " + UnwrapExceptionTypeName(ex));
                return null;
            }
        }

        private static string UnwrapExceptionTypeName(Exception ex)
        {
            Exception inner = ex;
            while (inner is TargetInvocationException && inner.InnerException != null)
            {
                inner = inner.InnerException;
            }
            return inner.GetType().Name;
        }

        /// <summary>"Model.CurrentRegion.Index" style chained instance Get; stops (and records the miss) at the first step that fails to resolve.</summary>
        public static object Walk(object root, string dottedPath)
        {
            if (string.IsNullOrEmpty(dottedPath))
            {
                Miss("Walk: empty path");
                return null;
            }
            object current = root;
            string[] steps = dottedPath.Split('.');
            for (int i = 0; i < steps.Length; i++)
            {
                if (current == null)
                {
                    Miss("Walk: '" + dottedPath + "' hit null before '" + steps[i] + "'");
                    return null;
                }
                string beforeStep = LastMiss;
                current = Get(current, steps[i]);
                if (current == null && LastMiss != beforeStep && LastMiss != null)
                {
                    Miss("Walk: '" + dottedPath + "' — " + LastMiss);
                    return null;
                }
            }
            return current;
        }

        /// <summary>Null-safe type name + ToString(), for describing a Get/Call/Walk result without a throw on null.</summary>
        public static string Describe(object o)
        {
            if (o == null)
            {
                return "(null)";
            }
            return o.GetType().FullName + ": " + o.ToString();
        }
    }
}
#endif
