using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace RimWorldAccess
{
    /// <summary>
    /// THE canonical way to reach vanilla's private fields, methods, and properties
    /// via reflection. Caches each resolved MemberInfo by (declaring type, member
    /// name) so repeated lookups across call sites share one AccessTools resolution
    /// instead of re-resolving the same member file after file. Resolution runs on
    /// the game's main thread, so a plain Dictionary is safe without locking.
    ///
    /// Matches AccessTools' own behavior on a miss: an unresolved member caches and
    /// returns null (never throws) so existing null-guarded call sites keep working
    /// unchanged.
    ///
    /// Three "vanilla bridge" shapes sit on top of this cache; pick the one that
    /// matches the access pattern rather than defaulting to whichever one a
    /// nearby file happens to use. Stateless method-accessors (e.g.
    /// ModListVanillaBridge) fit page-less lookups: every call resolves against
    /// a known type with no live instance to track between calls. Stateful
    /// properties with Bind/Unbind (e.g. WorldParamsPageBridge) fit a single
    /// live page/dialog instance that has its own open/close lifecycle -- callers
    /// read and write through named properties instead of each holding the
    /// instance themselves. A raw shared FieldInfo/MethodInfo cache (e.g.
    /// XenotypeReflection) fits members that two or more call sites hit
    /// directly and often (including per GUI frame), where the only goal is one
    /// resolution instead of duplicate declarations -- no page binding or
    /// lifecycle involved.
    /// </summary>
    public static class VanillaAccess
    {
        private static readonly Dictionary<(Type, string), FieldInfo> fieldCache =
            new Dictionary<(Type, string), FieldInfo>();
        private static readonly Dictionary<(Type, string), MethodInfo> methodCache =
            new Dictionary<(Type, string), MethodInfo>();
        private static readonly Dictionary<(Type, string), PropertyInfo> propertyCache =
            new Dictionary<(Type, string), PropertyInfo>();

        /// <summary>Cached equivalent of AccessTools.Field(type, name).</summary>
        public static FieldInfo GetField(Type type, string name)
        {
            var key = (type, name);
            if (!fieldCache.TryGetValue(key, out FieldInfo field))
            {
                field = AccessTools.Field(type, name);
                fieldCache[key] = field;
            }
            return field;
        }

        /// <summary>Cached equivalent of AccessTools.Method(type, name).</summary>
        public static MethodInfo GetMethod(Type type, string name)
        {
            var key = (type, name);
            if (!methodCache.TryGetValue(key, out MethodInfo method))
            {
                method = AccessTools.Method(type, name);
                methodCache[key] = method;
            }
            return method;
        }

        /// <summary>Cached equivalent of AccessTools.Property(type, name).</summary>
        public static PropertyInfo GetProperty(Type type, string name)
        {
            var key = (type, name);
            if (!propertyCache.TryGetValue(key, out PropertyInfo property))
            {
                property = AccessTools.Property(type, name);
                propertyCache[key] = property;
            }
            return property;
        }
    }
}
