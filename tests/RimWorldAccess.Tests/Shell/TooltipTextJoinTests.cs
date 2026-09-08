using RimWorldAccess.Shell;
using Xunit;

namespace RimWorldAccess.Tests.Shell;

public class TooltipTextJoinTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void EmptyExisting_ReturnsTheJoinedTips(string existing)
    {
        Assert.Equal("Needs power. Keeps the door open.",
            TooltipTextJoin.Append(existing, new[] { "Needs power.", "Keeps the door open." }));
    }

    [Fact]
    public void NoTips_ReturnsExistingUnchanged()
    {
        Assert.Equal("Includes rotten ones", TooltipTextJoin.Append("Includes rotten ones", null));
        Assert.Equal("Includes rotten ones", TooltipTextJoin.Append("Includes rotten ones", new string[0]));
    }

    [Fact]
    public void TipAlreadyInsideExisting_IsSkipped()
    {
        Assert.Equal("Needs power. Keeps the door open.",
            TooltipTextJoin.Append("Needs power. Keeps the door open.", new[] { " Needs power. " }));
    }

    [Fact]
    public void TipRepeatedInTheSameCall_IsSkipped()
    {
        Assert.Equal("Needs power.",
            TooltipTextJoin.Append(null, new[] { "Needs power.", "Needs power." }));
    }

    [Fact]
    public void CaseDiffersFromExisting_StillSpeaks()
    {
        Assert.Equal("Needs power. NEEDS POWER.",
            TooltipTextJoin.Append("Needs power.", new[] { "NEEDS POWER." }));
    }

    [Fact]
    public void JoinDoesNotDoubleATerminatorAlreadyThere()
    {
        Assert.Equal("Ready! Fires on command?",
            TooltipTextJoin.Append("Ready!", new[] { "Fires on command?" }));
        Assert.Equal("Ready. Fires on command.",
            TooltipTextJoin.Append("Ready", new[] { "Fires on command." }));
    }

    [Fact]
    public void TipsKeepTheOrderGiven()
    {
        Assert.Equal("First. Second. Third.",
            TooltipTextJoin.Append(null, new[] { "First.", "Second.", "Third." }));
    }
}
