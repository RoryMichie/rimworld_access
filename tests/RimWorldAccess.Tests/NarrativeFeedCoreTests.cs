using System.Linq;
using RimWorldAccess;

namespace RimWorldAccess.Tests;

/// <summary>
/// Tests for <see cref="NarrativeFeedCore"/>, the pure ring buffer/dedupe/
/// announce-decision engine behind the Narrative Feed. Each test constructs a
/// fresh instance — the class is
/// static-free specifically so tests never share state.
/// </summary>
public class NarrativeFeedCoreTests
{
    private static NarrativeRecord MakeRecord(
        string dedupeKey = "key",
        string text = "hello",
        NarrativeSource source = NarrativeSource.Interaction,
        bool vocalizedByTts = false,
        bool announceEligible = true)
    {
        return new NarrativeRecord
        {
            SpeakerName = "Speaker",
            RecipientName = null,
            Text = text,
            Source = source,
            DedupeKey = dedupeKey,
            ConversationId = -1,
            Tick = 0,
            VocalizedByTts = vocalizedByTts,
            AnnounceEligible = announceEligible,
        };
    }

    [Fact]
    public void Add_FirstRecord_ReturnsAdded()
    {
        var core = new NarrativeFeedCore();
        Assert.Equal(NarrativeAddResult.Added, core.Add(MakeRecord()));
    }

    [Fact]
    public void Add_DuplicateDedupeKey_IsDroppedEntirely()
    {
        var core = new NarrativeFeedCore();
        core.Add(MakeRecord(dedupeKey: "same", text: "first"));

        var result = core.Add(MakeRecord(dedupeKey: "same", text: "second"));

        Assert.Equal(NarrativeAddResult.Duplicate, result);
        var snapshot = core.Snapshot();
        Assert.Single(snapshot);
        Assert.Equal("first", snapshot[0].Text);
    }

    [Fact]
    public void Add_DifferentDedupeKeys_BothRetained()
    {
        var core = new NarrativeFeedCore();
        core.Add(MakeRecord(dedupeKey: "a"));
        core.Add(MakeRecord(dedupeKey: "b"));

        Assert.Equal(2, core.Snapshot().Count);
    }

    [Fact]
    public void Snapshot_OrdersOldestFirstNewestLast()
    {
        var core = new NarrativeFeedCore();
        core.Add(MakeRecord(dedupeKey: "1", text: "one"));
        core.Add(MakeRecord(dedupeKey: "2", text: "two"));
        core.Add(MakeRecord(dedupeKey: "3", text: "three"));

        var snapshot = core.Snapshot();
        Assert.Equal(new[] { "one", "two", "three" }, snapshot.Select(r => r.Text));
    }

    [Fact]
    public void Add_AtCapacity_EvictsOldestAndRetainsCap()
    {
        var core = new NarrativeFeedCore();
        for (int i = 0; i < NarrativeFeedCore.Capacity; i++)
        {
            core.Add(MakeRecord(dedupeKey: $"key{i}", text: $"line{i}"));
        }

        core.Add(MakeRecord(dedupeKey: "overflow", text: "newest"));

        var snapshot = core.Snapshot();
        Assert.Equal(NarrativeFeedCore.Capacity, snapshot.Count);
        // The oldest entry (line0) was evicted; line1 is now the oldest retained.
        Assert.Equal("line1", snapshot[0].Text);
        Assert.Equal("newest", snapshot[snapshot.Count - 1].Text);
    }

    [Fact]
    public void Add_EvictedKey_BecomesReusable()
    {
        var core = new NarrativeFeedCore();
        for (int i = 0; i < NarrativeFeedCore.Capacity; i++)
        {
            core.Add(MakeRecord(dedupeKey: $"key{i}", text: $"line{i}"));
        }

        // key0 was just evicted by the overflow add below; re-adding it must
        // succeed as a fresh entry, not be rejected as a stale duplicate.
        core.Add(MakeRecord(dedupeKey: "keyOverflow", text: "pushes key0 out"));
        var result = core.Add(MakeRecord(dedupeKey: "key0", text: "key0 again"));

        Assert.Equal(NarrativeAddResult.Added, result);
    }

    [Fact]
    public void Reset_ClearsBufferAndDedupeKeys()
    {
        var core = new NarrativeFeedCore();
        core.Add(MakeRecord(dedupeKey: "a"));

        core.Reset();

        Assert.Empty(core.Snapshot());
        // The same key must be accepted again post-reset (not treated as a duplicate).
        Assert.Equal(NarrativeAddResult.Added, core.Add(MakeRecord(dedupeKey: "a")));
    }

    private static NarrativeAnnounceOptions AllEnabled()
    {
        return new NarrativeAnnounceOptions(masterEnabled: true, vanillaInteractionsEnabled: true, ttsBackoffEnabled: false);
    }

    [Fact]
    public void ShouldAnnounce_NotEligible_ReturnsFalse()
    {
        var core = new NarrativeFeedCore();
        var record = MakeRecord(announceEligible: false);

        Assert.False(core.ShouldAnnounce(record, AllEnabled()));
    }

    [Fact]
    public void ShouldAnnounce_MasterDisabled_ReturnsFalse()
    {
        var core = new NarrativeFeedCore();
        var record = MakeRecord();
        var options = new NarrativeAnnounceOptions(masterEnabled: false, vanillaInteractionsEnabled: true, ttsBackoffEnabled: false);

        Assert.False(core.ShouldAnnounce(record, options));
    }

    [Fact]
    public void ShouldAnnounce_InteractionSourceWithVanillaDisabled_ReturnsFalse()
    {
        var core = new NarrativeFeedCore();
        var record = MakeRecord(source: NarrativeSource.Interaction);
        var options = new NarrativeAnnounceOptions(masterEnabled: true, vanillaInteractionsEnabled: false, ttsBackoffEnabled: false);

        Assert.False(core.ShouldAnnounce(record, options));
    }

    [Theory]
    [InlineData(NarrativeSource.RimTalkLine)]
    [InlineData(NarrativeSource.PlayerLine)]
    [InlineData(NarrativeSource.Extension)]
    public void ShouldAnnounce_NonInteractionSourceIgnoresVanillaToggle(NarrativeSource source)
    {
        var core = new NarrativeFeedCore();
        var record = MakeRecord(source: source);
        var options = new NarrativeAnnounceOptions(masterEnabled: true, vanillaInteractionsEnabled: false, ttsBackoffEnabled: false);

        Assert.True(core.ShouldAnnounce(record, options));
    }

    [Fact]
    public void ShouldAnnounce_VocalizedByTtsWithBackoffEnabled_ReturnsFalse()
    {
        var core = new NarrativeFeedCore();
        var record = MakeRecord(vocalizedByTts: true);
        var options = new NarrativeAnnounceOptions(masterEnabled: true, vanillaInteractionsEnabled: true, ttsBackoffEnabled: true);

        Assert.False(core.ShouldAnnounce(record, options));
    }

    [Fact]
    public void ShouldAnnounce_VocalizedByTtsWithBackoffDisabled_ReturnsTrue()
    {
        var core = new NarrativeFeedCore();
        var record = MakeRecord(vocalizedByTts: true);
        var options = new NarrativeAnnounceOptions(masterEnabled: true, vanillaInteractionsEnabled: true, ttsBackoffEnabled: false);

        Assert.True(core.ShouldAnnounce(record, options));
    }

    [Fact]
    public void ShouldAnnounce_AllGatesPass_ReturnsTrue()
    {
        var core = new NarrativeFeedCore();
        var record = MakeRecord(source: NarrativeSource.Interaction, vocalizedByTts: false, announceEligible: true);

        Assert.True(core.ShouldAnnounce(record, AllEnabled()));
    }
}
