using RimWorldAccess;

namespace RimWorldAccess.Tests;

/// <summary>
/// Tests for <see cref="AsyncTextStabilityCore"/>, the pure wait-for-settled state machine behind
/// the stable-read rule. Each test constructs a fresh instance
/// so tests never share state.
/// </summary>
public class AsyncTextStabilityCoreTests
{
    [Fact]
    public void Tick_NothingPending_IsNoOp()
    {
        var core = new AsyncTextStabilityCore();
        core.Tick();
        Assert.Equal(0, core.PendingCount);
    }

    [Fact]
    public void Defer_WhileGenerating_DoesNotFireUntilSettled()
    {
        var core = new AsyncTextStabilityCore();
        bool generating = true;
        bool fired = false;
        string firedText = null;

        core.Defer("k1", () => generating, () => true, () => "final", text => { fired = true; firedText = text; });
        core.Tick();

        Assert.False(fired);
        Assert.True(core.IsPending("k1"));

        generating = false;
        core.Tick();

        Assert.True(fired);
        Assert.Equal("final", firedText);
        Assert.False(core.IsPending("k1"));
    }

    [Fact]
    public void Settle_WhenNoLongerRelevant_DropsSilently()
    {
        var core = new AsyncTextStabilityCore();
        bool generating = true;
        bool relevant = true;
        bool fired = false;

        core.Defer("k1", () => generating, () => relevant, () => "final", _ => fired = true);

        // The player navigates away before the text settles.
        relevant = false;
        generating = false;
        core.Tick();

        Assert.False(fired);
        Assert.False(core.IsPending("k1"));
    }

    [Fact]
    public void Settle_FiresExactlyOnce()
    {
        var core = new AsyncTextStabilityCore();
        bool generating = true;
        int fireCount = 0;

        core.Defer("k1", () => generating, () => true, () => "final", _ => fireCount++);

        generating = false;
        core.Tick();
        core.Tick();
        core.Tick();

        Assert.Equal(1, fireCount);
    }

    [Fact]
    public void StillGenerating_NeverFires_AndStaysPending()
    {
        var core = new AsyncTextStabilityCore();
        int fireCount = 0;

        core.Defer("k1", () => true, () => true, () => "final", _ => fireCount++);

        core.Tick();
        core.Tick();

        Assert.Equal(0, fireCount);
        Assert.True(core.IsPending("k1"));
    }

    [Fact]
    public void Defer_SameKeyAgain_ReplacesEarlierInterest()
    {
        var core = new AsyncTextStabilityCore();
        bool generating = true;
        string firedText = null;

        core.Defer("k1", () => generating, () => true, () => "stale-text", text => firedText = text);
        // Revisited on a later frame with a freshened closure over the same logical row.
        core.Defer("k1", () => generating, () => true, () => "fresh-text", text => firedText = text);

        generating = false;
        core.Tick();

        Assert.Equal("fresh-text", firedText);
        Assert.Equal(0, core.PendingCount);
    }

    [Fact]
    public void Cancel_DropsPendingInterestUnfired()
    {
        var core = new AsyncTextStabilityCore();
        bool fired = false;
        core.Defer("k1", () => true, () => true, () => "final", _ => fired = true);

        core.Cancel("k1");

        Assert.False(core.IsPending("k1"));
        core.Tick();
        Assert.False(fired);
    }

    [Fact]
    public void Clear_DropsEveryPendingInterestUnfired()
    {
        var core = new AsyncTextStabilityCore();
        bool fired1 = false;
        bool fired2 = false;
        core.Defer("k1", () => false, () => true, () => "a", _ => fired1 = true);
        core.Defer("k2", () => false, () => true, () => "b", _ => fired2 = true);

        core.Clear();
        core.Tick();

        Assert.False(fired1);
        Assert.False(fired2);
        Assert.Equal(0, core.PendingCount);
    }

    [Fact]
    public void MultipleKeys_SettleIndependently()
    {
        var core = new AsyncTextStabilityCore();
        bool generatingA = true;
        bool generatingB = true;
        string firedA = null;
        string firedB = null;

        core.Defer("a", () => generatingA, () => true, () => "A-final", t => firedA = t);
        core.Defer("b", () => generatingB, () => true, () => "B-final", t => firedB = t);

        generatingA = false;
        core.Tick();

        Assert.Equal("A-final", firedA);
        Assert.Null(firedB);
        Assert.True(core.IsPending("b"));

        generatingB = false;
        core.Tick();

        Assert.Equal("B-final", firedB);
        Assert.False(core.IsPending("b"));
    }

    [Fact]
    public void ReDeferFromWithinCallback_IsSafe()
    {
        // A settle callback that immediately re-Defers the same key (the surface began generating
        // again right away) must not corrupt the pending dictionary being enumerated by Tick().
        var core = new AsyncTextStabilityCore();
        bool generating = true;
        int settleCount = 0;

        void OnSettled(string text)
        {
            settleCount++;
            if (settleCount == 1)
            {
                generating = true;
                core.Defer("k1", () => generating, () => true, () => "second-final", OnSettled);
            }
        }

        core.Defer("k1", () => generating, () => true, () => "first-final", OnSettled);

        generating = false;
        core.Tick(); // first settle fires, re-defers with generating = true

        Assert.Equal(1, settleCount);
        Assert.True(core.IsPending("k1"));

        generating = false;
        core.Tick(); // second settle fires

        Assert.Equal(2, settleCount);
        Assert.False(core.IsPending("k1"));
    }
}
