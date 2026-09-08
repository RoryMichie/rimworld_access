using RimWorldAccess.Shell;
using Xunit;

namespace RimWorldAccess.Tests.Shell;

public class DefaultAcceptInertnessTests
{
    [Fact]
    public void Null_IsNotInert()
    {
        Assert.False(DefaultAcceptInertness.IsInert(null));
    }

    [Fact]
    public void RoleNone_IsInert()
    {
        var d = new ElementDescription { Role = ElementRole.None };
        Assert.True(DefaultAcceptInertness.IsInert(d));
    }

    [Fact]
    public void ReadOnly_IsInert_RegardlessOfRole()
    {
        var d = new ElementDescription { Role = ElementRole.Button, ReadOnly = true };
        Assert.True(DefaultAcceptInertness.IsInert(d));
    }

    [Fact]
    public void RadioButton_SelectedViaSelectedChannel_IsInert()
    {
        var d = new ElementDescription { Role = ElementRole.RadioButton, Selected = true };
        Assert.True(DefaultAcceptInertness.IsInert(d));
    }

    [Fact]
    public void RadioButton_SelectedViaCheckChannel_IsInert()
    {
        var d = new ElementDescription { Role = ElementRole.RadioButton, Check = CheckState.Checked };
        Assert.True(DefaultAcceptInertness.IsInert(d));
    }

    [Fact]
    public void RadioButton_NotSelected_IsNotInert()
    {
        var d = new ElementDescription { Role = ElementRole.RadioButton, Selected = false };
        Assert.False(DefaultAcceptInertness.IsInert(d));
    }

    [Theory]
    [InlineData(ElementRole.Slider)]
    [InlineData(ElementRole.Stepper)]
    public void SliderOrStepper_WithoutEditSession_IsInert(ElementRole role)
    {
        var d = new ElementDescription { Role = role };
        Assert.True(DefaultAcceptInertness.IsInert(d));
    }

    [Theory]
    [InlineData(ElementRole.Slider)]
    [InlineData(ElementRole.Stepper)]
    public void SliderOrStepper_EnteringEditOnAccept_IsNotInert(ElementRole role)
    {
        var d = new ElementDescription { Role = role, EntersEditOnAccept = true };
        Assert.False(DefaultAcceptInertness.IsInert(d));
    }

    [Theory]
    [InlineData(ElementRole.Slider)]
    [InlineData(ElementRole.Stepper)]
    public void SliderOrStepper_ReadOnly_IsInert_EvenWithEditSession(ElementRole role)
    {
        var d = new ElementDescription { Role = role, EntersEditOnAccept = true, ReadOnly = true };
        Assert.True(DefaultAcceptInertness.IsInert(d));
    }

    [Theory]
    [InlineData(ElementRole.Button)]
    [InlineData(ElementRole.Checkbox)]
    [InlineData(ElementRole.ComboBox)]
    [InlineData(ElementRole.TextField)]
    [InlineData(ElementRole.MenuItem)]
    [InlineData(ElementRole.Tab)]
    [InlineData(ElementRole.TreeItem)]
    [InlineData(ElementRole.TableCell)]
    public void EveryOtherInteractiveRole_IsNotInert(ElementRole role)
    {
        var d = new ElementDescription { Role = role };
        Assert.False(DefaultAcceptInertness.IsInert(d));
    }

    [Fact]
    public void KeepsAccept_WithRoleNone_IsNotInert()
    {
        var d = new ElementDescription { Role = ElementRole.None, KeepsAccept = true };
        Assert.False(DefaultAcceptInertness.IsInert(d));
    }

    [Fact]
    public void KeepsAccept_WithReadOnly_IsNotInert()
    {
        var d = new ElementDescription { Role = ElementRole.Button, ReadOnly = true, KeepsAccept = true };
        Assert.False(DefaultAcceptInertness.IsInert(d));
    }

    [Fact]
    public void KeepsAccept_WithSelectedRadioButton_IsNotInert()
    {
        var d = new ElementDescription { Role = ElementRole.RadioButton, Selected = true, KeepsAccept = true };
        Assert.False(DefaultAcceptInertness.IsInert(d));
    }

    [Fact]
    public void KeepsAccept_WithSliderWithoutEditSession_IsNotInert()
    {
        var d = new ElementDescription { Role = ElementRole.Slider, KeepsAccept = true };
        Assert.False(DefaultAcceptInertness.IsInert(d));
    }

    [Fact]
    public void KeepsAcceptFalse_WithRoleNone_IsInert()
    {
        var d = new ElementDescription { Role = ElementRole.None, KeepsAccept = false };
        Assert.True(DefaultAcceptInertness.IsInert(d));
    }
}
