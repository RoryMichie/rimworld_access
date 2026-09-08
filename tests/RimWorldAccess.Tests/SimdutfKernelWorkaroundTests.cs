using RimWorldAccess;

namespace RimWorldAccess.Tests;

/// <summary>
/// Tests for <see cref="SimdutfKernelWorkaround.ShouldForce"/>, the gate that
/// decides whether to pin Prism's UTF-8 validation kernel. Getting this wrong
/// in either direction is costly: too narrow and AVX-512 players keep losing
/// every utterance containing multi-byte characters, too broad and a CPU
/// without AVX2 is asked to run a kernel it cannot execute.
/// </summary>
public class SimdutfKernelWorkaroundTests
{
    [Fact]
    public void Avx512Windows_Forces()
    {
        Assert.True(SimdutfKernelWorkaround.ShouldForce(true, hasAvx2: true, hasAvx512: true, existingValue: null));
    }

    [Fact]
    public void WithoutAvx512_DoesNotForce()
    {
        Assert.False(SimdutfKernelWorkaround.ShouldForce(true, hasAvx2: true, hasAvx512: false, existingValue: null));
    }

    [Fact]
    public void Avx512WithoutAvx2_DoesNotForce()
    {
        Assert.False(SimdutfKernelWorkaround.ShouldForce(true, hasAvx2: false, hasAvx512: true, existingValue: null));
    }

    [Fact]
    public void NonWindows_DoesNotForce()
    {
        Assert.False(SimdutfKernelWorkaround.ShouldForce(false, hasAvx2: true, hasAvx512: true, existingValue: null));
    }

    [Theory]
    [InlineData("haswell")]
    [InlineData("westmere")]
    [InlineData("icelake")]
    public void PlayerSetValue_IsLeftAlone(string existing)
    {
        Assert.False(SimdutfKernelWorkaround.ShouldForce(true, hasAvx2: true, hasAvx512: true, existingValue: existing));
    }

    [Fact]
    public void EmptyValue_CountsAsUnset()
    {
        Assert.True(SimdutfKernelWorkaround.ShouldForce(true, hasAvx2: true, hasAvx512: true, existingValue: ""));
    }

    [Fact]
    public void ForcedValue_NamesAKernelThatOnlyNeedsAvx2()
    {
        Assert.Equal("haswell", SimdutfKernelWorkaround.ForcedValue);
        Assert.Equal("SIMDUTF_FORCE_IMPLEMENTATION", SimdutfKernelWorkaround.VariableName);
    }
}
