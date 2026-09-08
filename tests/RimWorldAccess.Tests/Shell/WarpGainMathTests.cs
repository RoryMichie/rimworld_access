using RimWorldAccess.Shell;
using Xunit;

namespace RimWorldAccess.Tests.Shell;

/// <summary>
/// Tests for <see cref="WarpGainMath"/>: the closed-loop calibration that absorbs a
/// misreported points-to-pixels quotient (2026-08-17 trace, warps moving ~2x their request).
/// </summary>
public class WarpGainMathTests
{
    [Fact]
    public void OvershootCalibratesInOneStep()
    {
        Assert.Equal(2f, WarpGainMath.Next(1f, 100f, 200f, 1f));
    }

    [Fact]
    public void UndershootAdaptsBackDown()
    {
        Assert.Equal(1f, WarpGainMath.Next(2f, 100f, 50f, 1f));
    }

    [Fact]
    public void RequestBelowMeasurableFloorIsIgnored()
    {
        Assert.Equal(1f, WarpGainMath.Next(1f, 4f, 100f, 1f));
    }

    [Fact]
    public void ObservedBelowMeasurableFloorIsIgnored()
    {
        Assert.Equal(1f, WarpGainMath.Next(1f, 100f, 0.5f, 1f));
    }

    [Fact]
    public void MovementAgainstTheRequestIsIgnored()
    {
        Assert.Equal(1f, WarpGainMath.Next(1f, 100f, 100f, -1f));
    }

    [Fact]
    public void TotalGainClampsAtMaximum()
    {
        Assert.Equal(WarpGainMath.MaxGain, WarpGainMath.Next(3f, 100f, 400f, 1f));
    }

    [Fact]
    public void StepAndTotalClampAtMinimum()
    {
        Assert.Equal(WarpGainMath.MinGain, WarpGainMath.Next(0.5f, 100f, 10f, 1f));
    }
}
