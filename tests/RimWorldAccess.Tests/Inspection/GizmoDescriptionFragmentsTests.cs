using RimWorldAccess;
using RimWorldAccess.Shell;

namespace RimWorldAccess.Tests.Inspection;

public class GizmoDescriptionFragmentsTests
{
    [Fact]
    public void Constructor_SetsModeAndLeavesAllFragmentsUnset()
    {
        var fragments = new GizmoDescriptionFragments(GizmoDescriptionMode.Speech);

        Assert.Equal(GizmoDescriptionMode.Speech, fragments.Mode);
        Assert.Null(fragments.ToggleStateSuffix);
        Assert.Null(fragments.ToggleState);
        Assert.Null(fragments.TargetRangeSuffix);
        Assert.False(fragments.AbilityInfoAvailable);
        Assert.Null(fragments.AbilityCostInfo);
        Assert.Null(fragments.AbilityRangeInfo);
        Assert.Null(fragments.AbilityCooldownInfo);
    }

    [Theory]
    [InlineData(GizmoDescriptionMode.Speech)]
    [InlineData(GizmoDescriptionMode.MenuLabel)]
    public void Constructor_PreservesRequestedMode(GizmoDescriptionMode mode)
    {
        var fragments = new GizmoDescriptionFragments(mode);

        Assert.Equal(mode, fragments.Mode);
    }

    [Theory]
    [InlineData(CheckState.Checked)]
    [InlineData(CheckState.Unchecked)]
    public void ToggleState_CarriesTheCheckStateTheSpeechPathReads(CheckState state)
    {
        var fragments = new GizmoDescriptionFragments(GizmoDescriptionMode.Speech)
        {
            ToggleState = state,
        };

        Assert.Equal(state, fragments.ToggleState);
    }

    /// <summary>
    /// A toggle reports BOTH shapes: the check state for the speech path's
    /// element description, and the pre-composed suffix the float-menu rows
    /// still splice into their single-string label.
    /// </summary>
    [Fact]
    public void ToggleState_IsIndependentOfTheLegacySuffix()
    {
        var fragments = new GizmoDescriptionFragments(GizmoDescriptionMode.MenuLabel)
        {
            ToggleState = CheckState.Checked,
            ToggleStateSuffix = ": On",
        };

        Assert.Equal(CheckState.Checked, fragments.ToggleState);
        Assert.Equal(": On", fragments.ToggleStateSuffix);
    }
}
