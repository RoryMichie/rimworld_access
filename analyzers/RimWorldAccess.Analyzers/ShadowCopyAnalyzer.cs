using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace RimWorldAccess.Analyzers
{
    /// <summary>
    /// RWA0001: a State or Scope must not hold an editable copy of a vanilla value across
    /// keypresses and write it back at the end. Write through to the vanilla object on every
    /// keypress instead. analyzers/README.md states the rule and the deliberately silent shapes.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class ShadowCopyAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "RWA0001";

        private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
            DiagnosticId,
            "State holds an editable copy of vanilla state",
            "Field '{0}' is an editable copy of vanilla state: seeded in '{1}', edited in '{2}', written back in '{3}'. Write through to the vanilla object on every change instead of buffering it.",
            "RimWorldAccess.Doctrine",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: "A value that lives in our object across keypresses while vanilla's equivalent lives one IMGUI frame on the stack is a shadow copy. Seed/edit/write-back is the shape; write through on each keypress instead.");

        private static readonly string[] SeedVerbs = { "Open", "Begin", "Start" };

        private static readonly string[] EditVerbs =
        {
            "Adjust", "Select", "Step", "Jump", "Handle", "Increase", "Decrease", "Toggle", "Cycle",
        };

        private static readonly string[] CommitVerbs = { "Confirm", "Accept", "Apply", "Commit" };

        private static readonly string[] ExemptNameFragments = { "typeahead", "buffer", "pending", "armed" };

        private static readonly string[] ExemptNameSuffixes = { "Index", "Cursor", "Scroll" };

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSymbolStartAction(Guard<SymbolStartAnalysisContext>(OnSymbolStart), SymbolKind.NamedType);
        }

        /// <summary>
        /// Roslyn silently disables an analyzer that throws, which would turn this guardrail into
        /// a placebo without anyone noticing. Every callback swallows instead; the canary test
        /// covers the resulting blind spot.
        /// </summary>
        private static Action<T> Guard<T>(Action<T> body) => ctx =>
        {
            try
            {
                body(ctx);
            }
            catch (Exception)
            {
                // Intentionally swallowed: a missed diagnostic beats a broken build.
            }
        };

        private static void OnSymbolStart(SymbolStartAnalysisContext context)
        {
            var type = (INamedTypeSymbol)context.Symbol;
            if (type.TypeKind != TypeKind.Class && type.TypeKind != TypeKind.Struct) return;
            if (!type.Name.EndsWith("State", StringComparison.Ordinal) &&
                !type.Name.EndsWith("Scope", StringComparison.Ordinal))
            {
                return;
            }

            var facts = new ConcurrentDictionary<ISymbol, FieldFacts>(SymbolEqualityComparer.Default);

            FieldFacts FactsFor(IFieldSymbol field) =>
                facts.GetOrAdd(field, _ => new FieldFacts());

            void RecordWrite(IOperation target, IOperation value, ISymbol containingMethod)
            {
                var field = RootFieldOf(target, type, out bool writesFieldItself);
                if (field == null) return;

                // Writing through a member of a vanilla object we hold a reference to is the live
                // posture the doctrine asks for, not a copy. Only replacing the reference itself,
                // or editing a value-type field (where the member write lands on our own copy),
                // counts.
                if (!writesFieldItself && !field.Type.IsValueType) return;

                string method = containingMethod?.Name;
                if (method == null) return;

                if (value != null && StartsWithVerb(method, SeedVerbs) && IsVanillaOrigin(value))
                {
                    FactsFor(field).Seed = method;
                }

                if (StartsWithVerb(method, EditVerbs))
                {
                    FactsFor(field).Edit = method;
                }
            }

            context.RegisterOperationAction(Guard<OperationAnalysisContext>(ctx =>
            {
                var assignment = (IAssignmentOperation)ctx.Operation;
                RecordWrite(assignment.Target, assignment.Value, ctx.ContainingSymbol);
            }), OperationKind.SimpleAssignment, OperationKind.CompoundAssignment, OperationKind.CoalesceAssignment);

            context.RegisterOperationAction(Guard<OperationAnalysisContext>(ctx =>
            {
                var op = (IIncrementOrDecrementOperation)ctx.Operation;
                RecordWrite(op.Target, null, ctx.ContainingSymbol);
            }), OperationKind.Increment, OperationKind.Decrement);

            context.RegisterOperationAction(Guard<OperationAnalysisContext>(ctx =>
            {
                var read = (IFieldReferenceOperation)ctx.Operation;
                if (!IsMemberOf(read.Field, type) || IsWriteTarget(read)) return;

                string method = ctx.ContainingSymbol?.Name;
                if (method != null && StartsWithVerb(method, CommitVerbs))
                {
                    FactsFor(read.Field).Commit = method;
                }
            }), OperationKind.FieldReference);

            context.RegisterSymbolEndAction(Guard<SymbolAnalysisContext>(ctx =>
            {
                foreach (var pair in facts)
                {
                    var f = pair.Value;
                    if (f.Seed == null || f.Edit == null || f.Commit == null) continue;
                    if (IsExempt((IFieldSymbol)pair.Key)) continue;

                    var location = pair.Key.Locations.FirstOrDefault(l => l.IsInSource);
                    if (location == null) continue;

                    ctx.ReportDiagnostic(Diagnostic.Create(
                        Rule, location, pair.Key.Name, f.Seed, f.Edit, f.Commit));
                }
            }));
        }

        /// <summary>
        /// The field a write ultimately lands on: <c>hitPointsRange.min = x</c> writes
        /// <c>hitPointsRange</c>, so the target's instance chain is walked inward.
        /// </summary>
        private static IFieldSymbol RootFieldOf(IOperation target, INamedTypeSymbol owner, out bool writesFieldItself)
        {
            writesFieldItself = true;
            while (target != null)
            {
                switch (target)
                {
                    case IFieldReferenceOperation fieldRef:
                        if (IsMemberOf(fieldRef.Field, owner)) return fieldRef.Field;
                        target = fieldRef.Instance;
                        writesFieldItself = false;
                        break;
                    case IPropertyReferenceOperation propertyRef:
                        target = propertyRef.Instance;
                        writesFieldItself = false;
                        break;
                    case IArrayElementReferenceOperation arrayRef:
                        target = arrayRef.ArrayReference;
                        writesFieldItself = false;
                        break;
                    case IParenthesizedOperation parenthesized:
                        target = parenthesized.Operand;
                        break;
                    default:
                        return null;
                }
            }

            return null;
        }

        private static bool IsWriteTarget(IOperation operation)
        {
            // Climb out of any member/element access the reference is the receiver of, then ask
            // whether that outermost access is what an assignment or increment writes to.
            IOperation node = operation;
            while (node.Parent is IFieldReferenceOperation parentField && parentField.Instance == node ||
                   node.Parent is IPropertyReferenceOperation parentProperty && parentProperty.Instance == node ||
                   node.Parent is IArrayElementReferenceOperation parentArray && parentArray.ArrayReference == node)
            {
                node = node.Parent;
            }

            switch (node.Parent)
            {
                case IAssignmentOperation assignment:
                    return assignment.Target == node;
                case IIncrementOrDecrementOperation increment:
                    return increment.Target == node;
                case IArgumentOperation argument:
                    return argument.Parameter?.RefKind == RefKind.Out;
                default:
                    return false;
            }
        }

        /// <summary>
        /// A seeded value came from outside: a vanilla member, a reflection read, or a parameter
        /// (the caller handing us the value it is about to have written back).
        /// </summary>
        private static bool IsVanillaOrigin(IOperation value)
        {
            foreach (var op in value.DescendantsAndSelf())
            {
                switch (op)
                {
                    case IParameterReferenceOperation _:
                        return true;
                    case IFieldReferenceOperation fieldRef when IsVanillaType(fieldRef.Field.ContainingType):
                        return true;
                    case IPropertyReferenceOperation propertyRef when IsVanillaType(propertyRef.Property.ContainingType):
                        return true;
                    case IInvocationOperation invocation when IsReflectionGetValue(invocation.TargetMethod):
                        return true;
                }
            }

            return false;
        }

        private static bool IsReflectionGetValue(IMethodSymbol method)
        {
            if (method == null || method.Name != "GetValue") return false;
            string owner = method.ContainingType?.Name;
            return (owner == "FieldInfo" || owner == "PropertyInfo") &&
                   method.ContainingType.ContainingNamespace?.ToDisplayString() == "System.Reflection";
        }

        /// <summary>
        /// Compares the root namespace segment, never a prefix: our own root namespace is
        /// <c>RimWorldAccess</c>, which a StartsWith("RimWorld") test would match.
        /// </summary>
        private static bool IsVanillaType(INamedTypeSymbol type)
        {
            INamespaceSymbol ns = type?.ContainingNamespace;
            if (ns == null || ns.IsGlobalNamespace) return false;
            while (ns.ContainingNamespace != null && !ns.ContainingNamespace.IsGlobalNamespace)
            {
                ns = ns.ContainingNamespace;
            }

            return ns.Name == "Verse" || ns.Name == "RimWorld";
        }

        private static bool IsMemberOf(IFieldSymbol field, INamedTypeSymbol type) =>
            field != null &&
            SymbolEqualityComparer.Default.Equals(field.ContainingType?.OriginalDefinition, type.OriginalDefinition);

        private static bool StartsWithVerb(string methodName, string[] verbs) =>
            verbs.Any(v => methodName.StartsWith(v, StringComparison.Ordinal));

        /// <summary>
        /// The deliberately silent shapes: typeahead and numeric-entry buffers, one-shot armed
        /// channels, and display cursors. Missing a real site beats flagging one of these.
        /// </summary>
        private static bool IsExempt(IFieldSymbol field)
        {
            string name = field.Name;
            if (ExemptNameFragments.Any(f => name.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0)) return true;
            if (ExemptNameSuffixes.Any(s => name.EndsWith(s, StringComparison.OrdinalIgnoreCase))) return true;
            if (field.Type.SpecialType == SpecialType.System_String) return true;
            if (field.Type.SpecialType == SpecialType.System_Int32 &&
                name.IndexOf("index", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            return false;
        }

        private sealed class FieldFacts
        {
            public string Seed;
            public string Edit;
            public string Commit;
        }
    }
}
