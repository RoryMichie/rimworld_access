using RimWorldAccess.Shell;

namespace RimWorldAccess.Tests.Shell;

public class SectionPrefixTrackerTests
{
    [Fact]
    public void FirstLandingSpeaksThenRepeatsAreSilent()
    {
        var tracker = new SectionPrefixTracker(trackSilentRows: false, speakFirstLanding: true);

        Assert.Equal("Weapons", tracker.Cross("Weapons"));
        Assert.Null(tracker.Cross("Weapons"));
        Assert.Equal("Apparel", tracker.Cross("Apparel"));
    }

    [Fact]
    public void SectionlessRowIsSilentAndNeverSpeaksItsOwnName()
    {
        var tracker = new SectionPrefixTracker(trackSilentRows: false, speakFirstLanding: true);

        Assert.Null(tracker.Cross(""));
        Assert.Null(tracker.Cross(null));
        Assert.Equal("Weapons", tracker.Cross("Weapons"));
    }

    [Fact]
    public void WithoutSilentRowTracking_ReturningToTheSameSectionStaysSilent()
    {
        var tracker = new SectionPrefixTracker(trackSilentRows: false, speakFirstLanding: true);

        Assert.Equal("Weapons", tracker.Cross("Weapons"));
        Assert.Null(tracker.Cross(null));
        Assert.Null(tracker.Cross("Weapons"));
    }

    [Fact]
    public void WithSilentRowTracking_ReturningToTheSameSectionReAnnounces()
    {
        var tracker = new SectionPrefixTracker(trackSilentRows: true, speakFirstLanding: true);

        Assert.Equal("Weapons", tracker.Cross("Weapons"));
        Assert.Null(tracker.Cross(null));
        Assert.Equal("Weapons", tracker.Cross("Weapons"));
    }

    [Fact]
    public void WithoutFirstLanding_TheFirstSectionedLandingIsSilent()
    {
        var tracker = new SectionPrefixTracker(trackSilentRows: true, speakFirstLanding: false);

        Assert.Null(tracker.Cross("Memes"));
        Assert.Equal("Precepts", tracker.Cross("Precepts"));
        Assert.Null(tracker.Cross("Precepts"));
    }

    [Fact]
    public void ResetRestoresFirstLandingBehavior()
    {
        var speaking = new SectionPrefixTracker(trackSilentRows: false, speakFirstLanding: true);
        Assert.Equal("Weapons", speaking.Cross("Weapons"));
        speaking.Reset();
        Assert.Equal("Weapons", speaking.Cross("Weapons"));

        var silent = new SectionPrefixTracker(trackSilentRows: true, speakFirstLanding: false);
        Assert.Null(silent.Cross("Memes"));
        Assert.Equal("Precepts", silent.Cross("Precepts"));
        silent.Reset();
        Assert.Null(silent.Cross("Precepts"));
    }

    [Fact]
    public void PrimeSilencesTheNextLandingOnThePrimedSection()
    {
        var tracker = new SectionPrefixTracker(trackSilentRows: false, speakFirstLanding: true);

        tracker.Prime("Weapons");
        Assert.Null(tracker.Cross("Weapons"));
        Assert.Equal("Apparel", tracker.Cross("Apparel"));
    }

    [Fact]
    public void SectionNamesCompareOrdinally()
    {
        var tracker = new SectionPrefixTracker(trackSilentRows: false, speakFirstLanding: true);

        Assert.Equal("Weapons", tracker.Cross("Weapons"));
        Assert.Equal("weapons", tracker.Cross("weapons"));
    }
}
