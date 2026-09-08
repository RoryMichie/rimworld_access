using System.Collections.Generic;
using RimWorldAccess;

namespace RimWorldAccess.Tests.Inspection;

/// <summary>
/// The residue rule behind the inspect tree's captured-row presentation
/// (InspectionTreeBuilder.EmitCapturedRow): a band that is nothing but its own
/// controls must not read a glued summary line before reading each control.
/// </summary>
public class CapturedRowResidueTests
{
    private static List<string> Fragments(params string[] fragments) => new(fragments);

    [Fact]
    public void PureButtonBand_HasNoContext()
    {
        // The colonist inspect tab's own row, verbatim from a live capture.
        string rowText = "Rename colonist. button, Banish. button, Open expertise panel. button";
        Assert.False(CapturedRowResidue.HasContext(rowText,
            Fragments("Rename colonist. button", "Banish. button", "Open expertise panel. button")));
    }

    [Fact]
    public void LabelBesideControls_HasContext()
    {
        string rowText = "Hunger, 40%. slider, Reset. button";
        Assert.True(CapturedRowResidue.HasContext(rowText, Fragments("40%. slider", "Reset. button")));
    }

    [Fact]
    public void ResidualDigits_CountAsContext()
    {
        // A read-out that is only a number is still something a sighted player
        // reads off the line.
        Assert.True(CapturedRowResidue.HasContext("3, Increase. button", Fragments("Increase. button")));
    }

    [Fact]
    public void SeparatorsAndWhitespaceOnly_AreNotContext()
    {
        Assert.False(CapturedRowResidue.HasContext(" ,  . ; ", Fragments()));
        Assert.False(CapturedRowResidue.HasContext("", Fragments()));
        Assert.False(CapturedRowResidue.HasContext(null, Fragments()));
    }

    [Fact]
    public void Strip_RemovesEachFragmentOnce()
    {
        Assert.Equal("A, ", CapturedRowResidue.Strip("A, B", Fragments("B")));
    }

    [Fact]
    public void Strip_IgnoresNullAndBlankFragments()
    {
        Assert.Equal("A, B", CapturedRowResidue.Strip("A, B", Fragments(null, "")));
        Assert.Equal("A, B", CapturedRowResidue.Strip("A, B", null));
    }

    [Fact]
    public void NonLatinResidue_CountsAsContext()
    {
        // Letters, not the Latin alphabet, decide — the same row on a Russian
        // or Chinese install must keep its context line.
        Assert.True(CapturedRowResidue.HasContext("Голод, 40%. ползунок", Fragments("40%. ползунок")));
    }
}
