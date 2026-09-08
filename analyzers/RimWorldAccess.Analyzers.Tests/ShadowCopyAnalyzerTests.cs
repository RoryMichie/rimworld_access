using Microsoft.CodeAnalysis.Testing;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    RimWorldAccess.Analyzers.ShadowCopyAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace RimWorldAccess.Analyzers.Tests;

public class ShadowCopyAnalyzerTests
{
    /// <summary>
    /// RWA0001 keys on the namespace name, so stubs in namespaces literally called Verse and
    /// RimWorld stand in for the game assemblies; no RimWorld reference is needed.
    /// </summary>
    private const string Stubs = """

        namespace Verse
        {
            public class ThingFilter { public float AllowedHitPoints; }
        }

        namespace RimWorld
        {
            public class Transferable { public int CountToTransfer { get; set; } }
        }
        """;

    private static string Source(string body) => body + Stubs;

    private const string CanonicalShadowCopy = """
        using Verse;

        public class RangeState
        {
            private float {|#0:hitPoints|};

            public void OpenRange(ThingFilter filter) { hitPoints = filter.AllowedHitPoints; }
            public void AdjustValue(int direction) { hitPoints += direction; }
            public void ApplyAndClose(out float applied) { applied = hitPoints; }
        }
        """;

    private static DiagnosticResult CanonicalDiagnostic => VerifyCS.Diagnostic()
        .WithLocation(0)
        .WithArguments("hitPoints", "OpenRange", "AdjustValue", "ApplyAndClose");

    [Fact]
    public async Task SeedEditCommit_OnAState_Fires()
    {
        await VerifyCS.VerifyAnalyzerAsync(Source(CanonicalShadowCopy), CanonicalDiagnostic);
    }

    /// <summary>
    /// Roslyn disables a throwing analyzer without a word, which would leave every other test in
    /// this file passing vacuously. This one asserts the analyzer is alive at all.
    /// </summary>
    [Fact]
    public async Task SilentDisablementCanary_AnalyzerStillProducesRwa0001()
    {
        await VerifyCS.VerifyAnalyzerAsync(Source(CanonicalShadowCopy), CanonicalDiagnostic);
    }

    [Fact]
    public async Task ShadowCopyThroughAStructField_Fires()
    {
        await VerifyCS.VerifyAnalyzerAsync(Source("""
            using Verse;

            public struct Range { public float Min; }

            public class FilterScope
            {
                private Range {|#0:range|};

                public void OpenFilter(ThingFilter filter) { range.Min = filter.AllowedHitPoints; }
                public void StepValue(float delta) { range.Min += delta; }
                public float ConfirmEdit() { return range.Min; }
            }
            """),
            VerifyCS.Diagnostic().WithLocation(0)
                .WithArguments("range", "OpenFilter", "StepValue", "ConfirmEdit"));
    }

    /// <summary>
    /// Census 12's first near miss: a reference to a vanilla object we edit in place. That is the
    /// live posture the doctrine wants, so it must stay silent.
    /// </summary>
    [Fact]
    public async Task ReferenceToVanillaObjectEditedInPlace_IsSilent()
    {
        await VerifyCS.VerifyAnalyzerAsync(Source("""
            using Verse;

            public class BillState
            {
                private ThingFilter filter;

                public void Open(ThingFilter target) { filter = target; }
                public void AdjustRadius(float delta) { filter.AllowedHitPoints += delta; }
                public float ApplyChanges() { return filter.AllowedHitPoints; }
            }
            """));
    }

    [Fact]
    public async Task SeedFromReflection_Fires()
    {
        await VerifyCS.VerifyAnalyzerAsync(Source("""
            using System.Reflection;

            public class ReflectedState
            {
                private float {|#0:severity|};

                public void Begin() { severity = (float)Field().GetValue(null); }
                public void ToggleSeverity() { severity = 1f - severity; }
                public float AcceptEdit() { return severity; }

                private static FieldInfo Field() { return null; }
            }
            """),
            VerifyCS.Diagnostic().WithLocation(0)
                .WithArguments("severity", "Begin", "ToggleSeverity", "AcceptEdit"));
    }

    [Theory]
    [InlineData("typeaheadValue")]
    [InlineData("numericBuffer")]
    [InlineData("pendingValue")]
    [InlineData("armedValue")]
    [InlineData("rowIndex")]
    [InlineData("rowCursor")]
    [InlineData("rowScroll")]
    public async Task ExemptFieldNames_AreSilent(string fieldName)
    {
        await VerifyCS.VerifyAnalyzerAsync(Source($$"""
            using Verse;

            public class ExemptState
            {
                private float {{fieldName}};

                public void OpenRange(ThingFilter filter) { {{fieldName}} = filter.AllowedHitPoints; }
                public void AdjustValue(int direction) { {{fieldName}} += direction; }
                public void ApplyAndClose(out float applied) { applied = {{fieldName}}; }
            }
            """));
    }

    [Fact]
    public async Task StringField_IsSilent()
    {
        await VerifyCS.VerifyAnalyzerAsync(Source("""
            using RimWorld;

            public class NameState
            {
                private string entry;

                public void OpenEntry(Transferable transferable) { entry = transferable.CountToTransfer.ToString(); }
                public void HandleCharacter(char c) { entry += c; }
                public string AcceptEntry() { return entry; }
            }
            """));
    }

    [Fact]
    public async Task IntFieldNamedIndex_IsSilent()
    {
        await VerifyCS.VerifyAnalyzerAsync(Source("""
            using RimWorld;

            public class PickerState
            {
                private int indexOfChoice;

                public void OpenPicker(Transferable transferable) { indexOfChoice = transferable.CountToTransfer; }
                public void SelectNext() { indexOfChoice++; }
                public int ConfirmChoice() { return indexOfChoice; }
            }
            """));
    }

    [Fact]
    public async Task SeedFromLiteral_IsSilent()
    {
        await VerifyCS.VerifyAnalyzerAsync(Source("""
            public class CounterState
            {
                private int count;

                public void OpenCounter() { count = 0; }
                public void AdjustCount(int delta) { count += delta; }
                public int ConfirmCount() { return count; }
            }
            """));
    }

    [Fact]
    public async Task MutatedButNeverCommitted_IsSilent()
    {
        await VerifyCS.VerifyAnalyzerAsync(Source("""
            using Verse;

            public class DraftState
            {
                private float hitPoints;

                public void OpenRange(ThingFilter filter) { hitPoints = filter.AllowedHitPoints; }
                public void AdjustValue(int direction) { hitPoints += direction; }
            }
            """));
    }

    [Fact]
    public async Task CommittedButNeverMutated_IsSilent()
    {
        await VerifyCS.VerifyAnalyzerAsync(Source("""
            using Verse;

            public class SnapshotState
            {
                private float hitPoints;

                public void OpenRange(ThingFilter filter) { hitPoints = filter.AllowedHitPoints; }
                public void ApplyAndClose(out float applied) { applied = hitPoints; }
            }
            """));
    }

    [Fact]
    public async Task TypeNotNamedStateOrScope_IsSilent()
    {
        await VerifyCS.VerifyAnalyzerAsync(Source("""
            using Verse;

            public class RangeEditor
            {
                private float hitPoints;

                public void OpenRange(ThingFilter filter) { hitPoints = filter.AllowedHitPoints; }
                public void AdjustValue(int direction) { hitPoints += direction; }
                public void ApplyAndClose(out float applied) { applied = hitPoints; }
            }
            """));
    }

    [Fact]
    public async Task OurOwnRimWorldAccessNamespace_IsNotMistakenForVanilla()
    {
        await VerifyCS.VerifyAnalyzerAsync(Source("""
            namespace RimWorldAccess.Shell
            {
                public static class Settings { public static float Volume; }

                public class VolumeState
                {
                    private float volume;

                    public void OpenVolume() { volume = Settings.Volume; }
                    public void AdjustVolume(float delta) { volume += delta; }
                    public float ApplyVolume() { return volume; }
                }
            }
            """));
    }
}
