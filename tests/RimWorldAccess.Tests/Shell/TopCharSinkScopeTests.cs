using System.Collections.Generic;
using RimWorldAccess.Shell;

namespace RimWorldAccess.Tests.Shell;

/// <summary>
/// Covers <see cref="FocusStackCore.TopCharSinkScope"/> — a read-only mirror of
/// <see cref="FocusStackCore.OfferChar"/>'s walk. Reuses RecordingScope
/// (FocusStackTests.cs) and RecordingCharSink (FocusDispatchTests.cs).
/// </summary>
public class TopCharSinkScopeTests
{
    [Fact]
    public void EmptyStack_ReturnsNull()
    {
        var core = new FocusStackCore();

        Assert.Null(core.TopCharSinkScope);
    }

    [Fact]
    public void LiveNonModalScopeWithSink_ReturnsThatScope()
    {
        var core = new FocusStackCore();
        var journal = new List<string>();
        var scope = new RecordingScope("scope", journal, isModal: false);
        scope.CharSinkOverride = new RecordingCharSink(_ => true);
        core.Push(scope);

        Assert.Same(scope, core.TopCharSinkScope);
    }

    [Fact]
    public void SinkBearingScopeAboveModal_ReturnsUpperScope()
    {
        var core = new FocusStackCore();
        var journal = new List<string>();
        var lower = new RecordingScope("lower", journal);
        lower.CharSinkOverride = new RecordingCharSink(_ => true);
        var upperModal = new RecordingScope("upperModal", journal);
        upperModal.CharSinkOverride = new RecordingCharSink(_ => true);
        core.Push(lower);
        core.Push(upperModal);

        Assert.Same(upperModal, core.TopCharSinkScope);
    }

    [Fact]
    public void LiveModalWithoutSinkAboveSinkBearingScope_ReturnsNull()
    {
        var core = new FocusStackCore();
        var journal = new List<string>();
        var lower = new RecordingScope("lower", journal);
        lower.CharSinkOverride = new RecordingCharSink(_ => true);
        var topModal = new RecordingScope("topModal", journal); // modal, no sink
        core.Push(lower);
        core.Push(topModal);

        Assert.Null(core.TopCharSinkScope);
    }

    [Fact]
    public void ShadowSinkScope_Skipped()
    {
        var core = new FocusStackCore();
        var journal = new List<string>();
        var lower = new RecordingScope("lower", journal);
        lower.CharSinkOverride = new RecordingCharSink(_ => true);
        var shadow = new RecordingScope("shadow", journal, isLive: false);
        shadow.CharSinkOverride = new RecordingCharSink(_ => true);
        core.Push(lower);
        core.Push(shadow);

        Assert.Same(lower, core.TopCharSinkScope);
    }
}
