using Xunit;

namespace RimWorldAccess.Tests.Building;

public class PlacementHelpMergeTests
{
    [Fact]
    public void NewHelpWithHeldTile_SpeaksHelpFirst()
    {
        var decision = PlacementHelpMerge.Decide("Will be linked to: Field Smithy", "Sandstone floor, 12, 34", null);

        Assert.Equal("Will be linked to: Field Smithy. Sandstone floor, 12, 34", decision.Utterance);
        Assert.Equal("Will be linked to: Field Smithy", decision.LastSpoken);
        Assert.True(decision.CarriesTile);
    }

    [Fact]
    public void UnchangedHelp_SpeaksTileAlone()
    {
        // The required behaviour: the link text fires on entering and leaving range,
        // not on every step, while every step still says where the cursor is.
        var decision = PlacementHelpMerge.Decide("Will be linked to: Field Smithy", "Sandstone floor, 12, 35",
            "Will be linked to: Field Smithy");

        Assert.Equal("Sandstone floor, 12, 35", decision.Utterance);
        Assert.Equal("Will be linked to: Field Smithy", decision.LastSpoken);
        Assert.True(decision.CarriesTile);
    }

    [Fact]
    public void UnchangedHelpWithNothingHeld_SaysNothing()
    {
        var decision = PlacementHelpMerge.Decide("Links", null, "Links");

        Assert.Null(decision.Utterance);
        Assert.Equal("Links", decision.LastSpoken);
        Assert.False(decision.CarriesTile);
    }

    [Fact]
    public void NewHelpWithNothingHeld_SpeaksHelpAlone()
    {
        var decision = PlacementHelpMerge.Decide("Links", null, "Something else");

        Assert.Equal("Links", decision.Utterance);
        Assert.Equal("Links", decision.LastSpoken);
        Assert.False(decision.CarriesTile);
    }

    [Fact]
    public void NoHelp_StillSpeaksHeldTile_AndForgetsTheLastHelp()
    {
        var decision = PlacementHelpMerge.Decide(null, "Sandstone floor, 12, 36", "Links");

        Assert.Equal("Sandstone floor, 12, 36", decision.Utterance);
        Assert.Null(decision.LastSpoken);
        Assert.True(decision.CarriesTile);
    }

    [Fact]
    public void NoHelpAndNothingHeld_SaysNothing()
    {
        var decision = PlacementHelpMerge.Decide(null, null, "Links");

        Assert.Null(decision.Utterance);
        Assert.Null(decision.LastSpoken);
        Assert.False(decision.CarriesTile);
    }

    [Fact]
    public void HeldTile_WaitsAFullExtraFrameBeforeItIsFlushed()
    {
        // The draw pass can land either side of the key event within a frame, so a same-frame or
        // next-frame flush would pre-empt the merge the hold exists for.
        Assert.False(PlacementHelpMerge.ShouldFlushHeld(100, 100));
        Assert.False(PlacementHelpMerge.ShouldFlushHeld(100, 101));
        Assert.True(PlacementHelpMerge.ShouldFlushHeld(100, 102));
        Assert.True(PlacementHelpMerge.ShouldFlushHeld(100, 500));
    }
}
