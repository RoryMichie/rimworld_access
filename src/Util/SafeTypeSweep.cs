using System;
using System.Collections.Generic;

namespace RimWorldAccess
{
    /// <summary>
    /// Guards a sweep over reflection <see cref="Type"/>s against a poisoned
    /// type: a Type object that exists (it survived Assembly.GetTypes()'s own
    /// ReflectionTypeLoadException recovery) but still throws when the CLR is
    /// forced to resolve it further.
    ///
    /// Live-observed failure (2026-08-02): the installed mod "RimTalk TTS"
    /// (nitoritech.rimtalk.tts) ships RimTalk.TTS.Service.SiliconFlowClient,
    /// which references NAudio.Wave.Mp3FileReader — unresolvable on macOS.
    /// Verse.GenTypes.AllTypes already recovers from the
    /// ReflectionTypeLoadException that Assembly.GetTypes() throws (mirroring
    /// vanilla's own catch, falling back to ex.Types), so the poisoned Type
    /// object still ends up in the returned list. Calling Type.IsAssignableFrom
    /// (or IsSubclassOf, or anything else that forces the CLR to resolve that
    /// type's fields) on it then throws TypeLoadException from INSIDE the
    /// predicate itself (observed at RuntimeTypeHandle.type_is_assignable_from)
    /// — a sweep must guard each per-type predicate call, not just the initial
    /// enumeration, or one poisoned type takes the whole sweep down with it.
    ///
    /// This half is pure (no Verse/UnityEngine dependency) so it can run under
    /// the plain unit test host; <c>SafeTypeSweep.Game.cs</c> adds the
    /// Verse.GenTypes-backed convenience wrappers.
    /// </summary>
    public static partial class SafeTypeSweep
    {
        /// <summary>
        /// Filters <paramref name="types"/> to those for which
        /// <paramref name="predicate"/> returns true. A predicate that throws
        /// (a poisoned type failing to resolve mid-check) is treated as "does
        /// not match" instead of aborting the whole sweep; <paramref name="onError"/>,
        /// if given, is called with the offending type and exception first.
        /// Null entries in <paramref name="types"/> are skipped without
        /// invoking the predicate.
        /// </summary>
        public static IEnumerable<Type> WhereSafe(IEnumerable<Type> types, Func<Type, bool> predicate,
            Action<Type, Exception> onError = null)
        {
            if (types == null) yield break;

            foreach (Type type in types)
            {
                if (type == null) continue;

                bool matches;
                try
                {
                    matches = predicate(type);
                }
                catch (Exception ex)
                {
                    onError?.Invoke(type, ex);
                    continue;
                }

                if (matches) yield return type;
            }
        }

        /// <summary>
        /// Evaluates <paramref name="func"/> against <paramref name="type"/>,
        /// returning false (and reporting through <paramref name="onError"/>)
        /// instead of throwing when the type cannot be fully resolved.
        /// </summary>
        public static bool TryEvaluate<TResult>(Type type, Func<Type, TResult> func, out TResult result,
            Action<Type, Exception> onError = null)
        {
            try
            {
                result = func(type);
                return true;
            }
            catch (Exception ex)
            {
                onError?.Invoke(type, ex);
                result = default;
                return false;
            }
        }
    }
}
