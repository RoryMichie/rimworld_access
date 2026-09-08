using System;
using System.Collections.Generic;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Verse.GenTypes-backed half of <see cref="SafeTypeSweep"/> — see that
    /// file's remarks for the poisoned-type failure this guards against. These
    /// are drop-in safe replacements for the corresponding GenTypes extension
    /// methods (<c>AllSubclasses</c>, <c>AllSubclassesNonAbstract</c>,
    /// <c>InstantiableDescendantsAndSelf</c>), whose own LINQ queries run
    /// IsSubclassOf/IsAssignableFrom over every loaded type with NO per-type
    /// guard (decompiled Verse/GenTypes.cs) — calling any of them on a base
    /// type that a poisoned mod assembly's types could match throws straight
    /// out, same as the unguarded call this file replaces.
    /// </summary>
    public static partial class SafeTypeSweep
    {
        /// <summary>
        /// Every non-null type from every loaded assembly (vanilla + every
        /// running mod) — the sweep source behind every method below.
        /// </summary>
        public static IEnumerable<Type> AllModTypes => WhereSafe(GenTypes.AllTypes, _ => true);

        /// <summary>Safe replacement for <c>baseType.AllAssignableOfNonAbstract()</c>-shaped queries built on Type.IsAssignableFrom.</summary>
        public static IEnumerable<Type> AssignableFromSafe(Type baseType, Action<Type, Exception> onError = null)
            => WhereSafe(GenTypes.AllTypes, type => baseType.IsAssignableFrom(type), onError);

        /// <summary>Safe replacement for <c>baseType.AllSubclasses()</c>.</summary>
        public static IEnumerable<Type> SubclassesSafe(Type baseType, Action<Type, Exception> onError = null)
            => WhereSafe(GenTypes.AllTypes, type => type.IsSubclassOf(baseType), onError);

        /// <summary>Safe replacement for <c>baseType.AllSubclassesNonAbstract()</c>.</summary>
        public static IEnumerable<Type> SubclassesNonAbstractSafe(Type baseType, Action<Type, Exception> onError = null)
            => WhereSafe(GenTypes.AllTypes, type => type.IsSubclassOf(baseType) && !type.IsAbstract, onError);

        /// <summary>
        /// Safe replacement for <c>baseType.InstantiableDescendantsAndSelf()</c>
        /// (decompiled Verse/GenTypes.cs): baseType itself when concrete, plus
        /// every concrete subclass, walked through <see cref="SubclassesSafe"/>
        /// rather than GenTypes' unguarded AllSubclasses.
        /// </summary>
        public static IEnumerable<Type> InstantiableDescendantsAndSelfSafe(Type baseType,
            Action<Type, Exception> onError = null)
        {
            if (!baseType.IsAbstract) yield return baseType;

            foreach (Type type in SubclassesSafe(baseType, onError))
            {
                if (!type.IsAbstract) yield return type;
            }
        }
    }
}
