using RimWorldAccess.Shell;
using Xunit;

namespace RimWorldAccess.Tests.Shell;

public class HoverTargetKeyTests
{
    [Fact]
    public void DistinctBandsWithIdenticalText_ProduceDifferentKeys()
    {
        // The no-dedupe law expressed as a test: hover announces on TARGET
        // change, never on text change, so two rows reading identically must
        // both be able to speak.
        var first = new HoverTargetKey(3, 0, CapturedExtraKind.Checkbox, "Allowed");
        var second = new HoverTargetKey(7, 0, CapturedExtraKind.Checkbox, "Allowed");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void SameBandDifferentMember_ProducesDifferentKeys()
    {
        var slider = new HoverTargetKey(2, 0, CapturedExtraKind.Slider, "Volume");
        var reset = new HoverTargetKey(2, 1, CapturedExtraKind.Button, "Reset");

        Assert.NotEqual(slider, reset);
    }

    [Fact]
    public void SameTargetTwice_IsEqualAndHashesAlike()
    {
        var first = new HoverTargetKey(5, 2, CapturedExtraKind.Button, "Accept");
        var second = new HoverTargetKey(5, 2, CapturedExtraKind.Button, "Accept");

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void None_MatchesNoRealTarget_AndEqualsItself()
    {
        Assert.Equal(HoverTargetKey.None, HoverTargetKey.None);
        Assert.NotEqual(HoverTargetKey.None, new HoverTargetKey(0, -1, CapturedExtraKind.Label, ""));
    }

    [Fact]
    public void SameBandDifferentCell_ProducesDifferentKeys()
    {
        // Eleven unlabeled main-bar buttons share one band, one member ordinal
        // and one (empty) label; only the cell tells them apart.
        var work = new HoverTargetKey(0, -1, 4, CapturedExtraKind.InvisibleButton, "");
        var schedule = new HoverTargetKey(0, -1, 6, CapturedExtraKind.InvisibleButton, "");

        Assert.NotEqual(work, schedule);
    }

    [Fact]
    public void NullAndEmptyRawLabel_AreTheSameKey()
    {
        Assert.Equal(new HoverTargetKey(1, -1, CapturedExtraKind.Label, null),
            new HoverTargetKey(1, -1, CapturedExtraKind.Label, ""));
    }
}
