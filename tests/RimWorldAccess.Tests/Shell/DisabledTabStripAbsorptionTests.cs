using System.Collections.Generic;
using RimWorldAccess.Shell;
using Xunit;

namespace RimWorldAccess.Tests.Shell;

/// <summary>
/// Tests for <see cref="DisabledTabStripAbsorption"/>, the geometry-only rule that recognises a
/// research-gated tab drawn as a plain textured Label (Colony Manager Redux's "Power" tab) so it
/// joins the strip's own tab cycle as a disabled stop instead of polluting every page's content.
/// </summary>
public class DisabledTabStripAbsorptionTests
{
    private static DisabledStripCandidate Cell(int clip, float x, float y, float width = 64f, float height = 64f)
    {
        return new DisabledStripCandidate { ClipId = clip, X = x, Y = y, Width = width, Height = height };
    }

    [Fact]
    public void MatchingClipBandAndSize_IsAbsorbed()
    {
        var stripMembers = new List<DisabledStripCandidate>
        {
            Cell(clip: 1, x: 18f, y: 18f),
            Cell(clip: 1, x: 118f, y: 18f),
        };
        var candidates = new List<DisabledStripCandidate> { Cell(clip: 1, x: 982f, y: 18f) };

        IReadOnlyList<bool> result = DisabledTabStripAbsorption.FindAbsorbedRows(stripMembers, candidates);

        Assert.True(result[0]);
    }

    [Fact]
    public void DifferentClip_NeverAbsorbed()
    {
        var stripMembers = new List<DisabledStripCandidate> { Cell(clip: 1, x: 18f, y: 18f) };
        var candidates = new List<DisabledStripCandidate> { Cell(clip: 2, x: 982f, y: 18f) };

        IReadOnlyList<bool> result = DisabledTabStripAbsorption.FindAbsorbedRows(stripMembers, candidates);

        Assert.False(result[0]);
    }

    [Fact]
    public void OutsideVerticalBand_NeverAbsorbed()
    {
        var stripMembers = new List<DisabledStripCandidate> { Cell(clip: 1, x: 18f, y: 18f) };
        // Sits well below the strip's own band (a settings-pane row of the same size and clip).
        var candidates = new List<DisabledStripCandidate> { Cell(clip: 1, x: 342f, y: 400f) };

        IReadOnlyList<bool> result = DisabledTabStripAbsorption.FindAbsorbedRows(stripMembers, candidates);

        Assert.False(result[0]);
    }

    [Fact]
    public void MismatchedSize_NeverAbsorbed()
    {
        var stripMembers = new List<DisabledStripCandidate> { Cell(clip: 1, x: 18f, y: 18f, width: 64f, height: 64f) };
        // A wide settings-row caption sitting in the same clip and band by coincidence.
        var candidates = new List<DisabledStripCandidate> { Cell(clip: 1, x: 342f, y: 30f, width: 358f, height: 60f) };

        IReadOnlyList<bool> result = DisabledTabStripAbsorption.FindAbsorbedRows(stripMembers, candidates);

        Assert.False(result[0]);
    }

    [Fact]
    public void SizeWithinEpsilon_StillAbsorbed()
    {
        var stripMembers = new List<DisabledStripCandidate> { Cell(clip: 1, x: 18f, y: 18f, width: 64f, height: 64f) };
        var candidates = new List<DisabledStripCandidate> { Cell(clip: 1, x: 982f, y: 18f, width: 65f, height: 63f) };

        IReadOnlyList<bool> result = DisabledTabStripAbsorption.FindAbsorbedRows(stripMembers, candidates);

        Assert.True(result[0]);
    }

    [Fact]
    public void EmptyOrNullInput_ReturnsNoMatches()
    {
        Assert.Empty(DisabledTabStripAbsorption.FindAbsorbedRows(new List<DisabledStripCandidate>(), new List<DisabledStripCandidate>()));
        Assert.Empty(DisabledTabStripAbsorption.FindAbsorbedRows(null, null));

        var stripMembers = new List<DisabledStripCandidate> { Cell(clip: 1, x: 18f, y: 18f) };
        IReadOnlyList<bool> result = DisabledTabStripAbsorption.FindAbsorbedRows(stripMembers, null);
        Assert.Empty(result);
    }
}
