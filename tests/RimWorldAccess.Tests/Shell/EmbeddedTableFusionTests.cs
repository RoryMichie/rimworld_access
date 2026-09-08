using System.Collections.Generic;
using RimWorldAccess.Shell;
using Xunit;

namespace RimWorldAccess.Tests.Shell;

/// <summary>
/// Tests for <see cref="EmbeddedTableFusion"/>, the geometry-only rule that fuses the
/// per-cell fragments a vanilla PawnTable body draws (column-major, so one pawn's cells
/// arrive interleaved with every other pawn's) into one presentation row per visual row.
/// Pinned against a live trace of the Colony Manager Redux manager-overview geometry:
/// "Sense, Scientist" / "Sense, button" / "going for a walk" read as three unrelated rows.
/// </summary>
public class EmbeddedTableFusionTests
{
    private static TableCellCandidate Cell(float x, float y, string label, bool interactive = false, float width = 100f, float height = 30f)
    {
        return new TableCellCandidate { X = x, Y = y, Width = width, Height = height, Label = label, Interactive = interactive };
    }

    [Fact]
    public void ColumnMajorPawnTable_FusesOneRowPerPawn()
    {
        // Draw order mirrors PawnTableOnGUI: all name cells first (column 1),
        // then all activity cells (column 2). Three pawns, two columns.
        var cells = new List<TableCellCandidate>
        {
            Cell(0f, 0f, "Sense, Scientist", interactive: true),
            Cell(0f, 30f, "Eve, Entrepreneur", interactive: true),
            Cell(0f, 60f, "Kevin, Con artist", interactive: true),
            Cell(100f, 0f, "going for a walk"),
            Cell(100f, 30f, "wandering"),
            Cell(100f, 60f, "cowering"),
        };

        IReadOnlyList<TableRowGroup> groups = EmbeddedTableFusion.Compute(cells);

        Assert.Equal(3, groups.Count);
        Assert.Equal("Sense, Scientist. going for a walk", groups[0].FusedLabel);
        Assert.Equal("Eve, Entrepreneur. wandering", groups[1].FusedLabel);
        Assert.Equal("Kevin, Con artist. cowering", groups[2].FusedLabel);
    }

    [Fact]
    public void NameClickTarget_ContainedInFullLabel_IsDroppedFromSpeech_ButCarriesActivation()
    {
        // The vanilla label column draws the pawn's full "Name, Title" label AND a short-name
        // click target on the same row; the short name must not be spoken twice, but the
        // interactive member is still the one activation rides on.
        var cells = new List<TableCellCandidate>
        {
            Cell(0f, 0f, "Sense, Scientist"),
            Cell(10f, 0f, "Sense", interactive: true, width: 40f),
            Cell(100f, 0f, "going for a walk"),
        };

        IReadOnlyList<TableRowGroup> groups = EmbeddedTableFusion.Compute(cells);

        Assert.Single(groups);
        Assert.Equal("Sense, Scientist. going for a walk", groups[0].FusedLabel);
        Assert.Equal(1, groups[0].PrimaryIndex);
    }

    [Fact]
    public void RowsInDistinctBands_NeverFuse()
    {
        var cells = new List<TableCellCandidate>
        {
            Cell(0f, 0f, "Sense, Scientist", interactive: true),
            Cell(0f, 31f, "Eve, Entrepreneur", interactive: true),
        };

        IReadOnlyList<TableRowGroup> groups = EmbeddedTableFusion.Compute(cells);

        Assert.Equal(2, groups.Count);
        Assert.Single(groups[0].Members);
        Assert.Single(groups[1].Members);
    }

    [Fact]
    public void TallCellSharingHalfItsShorterNeighbor_SharesTheBand()
    {
        // CMR's activity column asks for two line-heights per cell; the name cell is one.
        // Overlap greater than half the SHORTER height still bands them together.
        var cells = new List<TableCellCandidate>
        {
            Cell(0f, 0f, "Sense, Scientist", interactive: true, height: 30f),
            Cell(100f, -10f, "going for a walk", height: 60f),
        };

        IReadOnlyList<TableRowGroup> groups = EmbeddedTableFusion.Compute(cells);

        Assert.Single(groups);
    }

    [Fact]
    public void NoInteractiveMember_PrimaryIsLeftmost()
    {
        var cells = new List<TableCellCandidate>
        {
            Cell(100f, 0f, "wandering"),
            Cell(0f, 0f, "Eve, Entrepreneur"),
        };

        IReadOnlyList<TableRowGroup> groups = EmbeddedTableFusion.Compute(cells);

        Assert.Single(groups);
        Assert.Equal(1, groups[0].PrimaryIndex);
        Assert.Equal("Eve, Entrepreneur. wandering", groups[0].FusedLabel);
    }

    [Fact]
    public void IdenticalTwinLabels_SpeakOnce()
    {
        var cells = new List<TableCellCandidate>
        {
            Cell(0f, 0f, "wandering"),
            Cell(100f, 0f, "wandering"),
        };

        IReadOnlyList<TableRowGroup> groups = EmbeddedTableFusion.Compute(cells);

        Assert.Single(groups);
        Assert.Equal("wandering", groups[0].FusedLabel);
    }

    [Fact]
    public void EmptyAndWhitespaceLabels_AreSkipped()
    {
        var cells = new List<TableCellCandidate>
        {
            Cell(0f, 0f, "Kevin, Con artist", interactive: true),
            Cell(60f, 0f, "  "),
            Cell(100f, 0f, "cowering"),
        };

        IReadOnlyList<TableRowGroup> groups = EmbeddedTableFusion.Compute(cells);

        Assert.Single(groups);
        Assert.Equal("Kevin, Con artist. cowering", groups[0].FusedLabel);
    }
}
