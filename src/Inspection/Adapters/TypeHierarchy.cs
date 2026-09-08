using System;

namespace RimWorldAccess
{
    /// <summary>
    /// Pure Type-hierarchy queries shared by the inspection registries. No game
    /// dependencies, so it is covered directly by tests.
    /// </summary>
    public static class TypeHierarchy
    {
        /// <summary>
        /// True when <paramref name="candidate"/> is <paramref name="type"/> itself
        /// or one of its base types. Used to tell an override introduced BELOW a
        /// resolved registration (a strict descendant of it) apart from one that
        /// merely inherits the registration's own behavior unchanged.
        /// </summary>
        public static bool IsSameOrAncestor(Type candidate, Type type)
        {
            if (candidate == null || type == null)
                return false;

            for (Type t = type; t != null; t = t.BaseType)
            {
                if (t == candidate)
                    return true;
            }
            return false;
        }
    }
}
