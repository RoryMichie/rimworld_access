using Xunit;

namespace RimWorldAccess.Tests.Building;

public class PlacementFootprintNamesTests
{
    [Fact]
    public void NamedPart_HasItsOwnKeyPerVerdictAndPosition()
    {
        Assert.Equal("RimWorldAccess.Building.Place.WatermillWheelOkAt",
            PlacementFootprintNames.OutlineKey(FootprintPart.WatermillWheel, OutlineVerdict.Suitable, atCursor: false));
        Assert.Equal("RimWorldAccess.Building.Place.WatermillWheelOkHere",
            PlacementFootprintNames.OutlineKey(FootprintPart.WatermillWheel, OutlineVerdict.Suitable, atCursor: true));
        Assert.Equal("RimWorldAccess.Building.Place.WatermillWheelBadAt",
            PlacementFootprintNames.OutlineKey(FootprintPart.WatermillWheel, OutlineVerdict.Unsuitable, atCursor: false));
        Assert.Equal("RimWorldAccess.Building.Place.WatermillWheelBadHere",
            PlacementFootprintNames.OutlineKey(FootprintPart.WatermillWheel, OutlineVerdict.Unsuitable, atCursor: true));
    }

    [Fact]
    public void SharedFlowArea_IsItsOwnVerdict()
    {
        Assert.Equal("RimWorldAccess.Building.Place.WatermillFlowSharedAt",
            PlacementFootprintNames.OutlineKey(FootprintPart.WatermillWaterFlow,
                OutlineVerdict.SharedWithOtherWatermill, atCursor: false));
        Assert.Equal("RimWorldAccess.Building.Place.WatermillFlowSharedHere",
            PlacementFootprintNames.OutlineKey(FootprintPart.WatermillWaterFlow,
                OutlineVerdict.SharedWithOtherWatermill, atCursor: true));
    }

    [Fact]
    public void UnnamedPart_KeepsTheGenericRequiredAreaKeys()
    {
        // Every place worker not in the vocabulary, modded ones included, reads generically.
        Assert.Equal("RimWorldAccess.Building.Place.OutlineOkAt",
            PlacementFootprintNames.OutlineKey(FootprintPart.Unnamed, OutlineVerdict.Suitable, atCursor: false));
        Assert.Equal("RimWorldAccess.Building.Place.OutlineBadHere",
            PlacementFootprintNames.OutlineKey(FootprintPart.Unnamed, OutlineVerdict.Unsuitable, atCursor: true));
    }

    [Fact]
    public void VerdictAPartIsNeverPaintedIn_HasNoKey()
    {
        // The caller falls back to the generic form rather than inventing a phrase: vanilla paints
        // the flow area green or orange only, and the shared verdict belongs to it alone.
        Assert.Null(PlacementFootprintNames.OutlineKey(FootprintPart.WatermillWaterFlow,
            OutlineVerdict.Unsuitable, atCursor: false));
        Assert.Null(PlacementFootprintNames.OutlineKey(FootprintPart.WatermillWheel,
            OutlineVerdict.SharedWithOtherWatermill, atCursor: false));
        Assert.Null(PlacementFootprintNames.OutlineKey(FootprintPart.Unnamed,
            OutlineVerdict.SharedWithOtherWatermill, atCursor: true));
    }
}
