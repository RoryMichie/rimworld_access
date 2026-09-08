using System;
using System.Collections.Generic;

namespace RimWorldAccess
{
    /// <summary>
    /// Resolves a value registered against the most-derived matching type in a
    /// runtime type's inheritance chain, reproducing C# `is`-chain polymorphism
    /// for a Dictionary-keyed registry (walking from the exact runtime type up
    /// through base types and returning the first registered match). Used by
    /// <see cref="GizmoHandlerRegistry"/> to resolve gizmo handlers without a
    /// hand-written `is`/`else if` chain. Pure: no game dependencies, so it is
    /// covered directly by tests.
    /// </summary>
    public sealed class TypeChainResolver<TValue>
    {
        private readonly Dictionary<Type, TValue> byType = new Dictionary<Type, TValue>();

        /// <summary>Registers <paramref name="value"/> against the exact type <paramref name="type"/>.</summary>
        public void Register(Type type, TValue value)
        {
            byType[type] = value;
        }

        /// <summary>
        /// Walks from <paramref name="runtimeType"/> up through base types (most
        /// derived first) and returns the first registered value found. Returns
        /// false if no type in the chain, including <paramref name="runtimeType"/>
        /// itself, is registered.
        /// </summary>
        public bool TryResolve(Type runtimeType, out TValue value)
        {
            return TryResolve(runtimeType, out value, out _);
        }

        /// <summary>
        /// Same walk as <see cref="TryResolve(Type,out TValue)"/>, additionally reporting which type in
        /// the chain actually carried the registration (<paramref name="matchedType"/> —
        /// <paramref name="runtimeType"/> itself for an exact match, an ancestor for an inherited one).
        /// Lets a caller tell an exact registration apart from one it merely inherited, e.g. to decide
        /// whether a base-type adapter still fits an override the concrete type introduced.
        /// </summary>
        public bool TryResolve(Type runtimeType, out TValue value, out Type matchedType)
        {
            for (Type t = runtimeType; t != null; t = t.BaseType)
            {
                if (byType.TryGetValue(t, out value))
                {
                    matchedType = t;
                    return true;
                }
            }

            value = default;
            matchedType = null;
            return false;
        }

        /// <summary>
        /// Walks from <paramref name="runtimeType"/> up through base types (most
        /// derived first) and yields every registered value along the chain. Lets
        /// a caller offer an operation to each candidate in precedence order until
        /// one claims it — a subtype handler that declines a facet defers to its
        /// base type's handler rather than ending resolution.
        /// </summary>
        public IEnumerable<TValue> ResolveChain(Type runtimeType)
        {
            for (Type t = runtimeType; t != null; t = t.BaseType)
            {
                if (byType.TryGetValue(t, out TValue value))
                    yield return value;
            }
        }
    }
}
