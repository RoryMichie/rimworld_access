using RimWorldAccess;

namespace RimWorldAccess.Tests;

/// <summary>
/// Tests for <see cref="TextCursor"/>, the caret arithmetic behind every text
/// field in the mod — the editable ones and the read-only browse of the dev
/// log's message details. Character, word and line stepping have to agree in
/// both, so the boundaries are pinned here rather than in either caller.
/// </summary>
public class TextCursorTests
{
    // ---------------------------------------------------------------
    // Word boundaries.
    // ---------------------------------------------------------------

    [Fact]
    public void NextWordBoundary_FromWordStart_SkipsWordAndFollowingSpace()
    {
        Assert.Equal(6, TextCursor.NextWordBoundary("alpha beta", 0));
    }

    [Fact]
    public void NextWordBoundary_MidWord_LandsOnNextWord()
    {
        Assert.Equal(6, TextCursor.NextWordBoundary("alpha beta", 2));
    }

    [Fact]
    public void NextWordBoundary_AtEnd_StaysAtEnd()
    {
        Assert.Equal(10, TextCursor.NextWordBoundary("alpha beta", 10));
    }

    [Fact]
    public void NextWordBoundary_TreatsPunctuationAsSeparator()
    {
        // From "Log" the next word starts after the colon, at "Error".
        Assert.Equal(10, TextCursor.NextWordBoundary("Verse.Log:Error", 6));
    }

    [Fact]
    public void PreviousWordBoundary_MidWord_LandsOnItsStart()
    {
        Assert.Equal(6, TextCursor.PreviousWordBoundary("alpha beta", 8));
    }

    [Fact]
    public void PreviousWordBoundary_AtWordStart_LandsOnThePreviousWord()
    {
        Assert.Equal(0, TextCursor.PreviousWordBoundary("alpha beta", 6));
    }

    [Fact]
    public void PreviousWordBoundary_AtStart_StaysAtStart()
    {
        Assert.Equal(0, TextCursor.PreviousWordBoundary("alpha beta", 0));
    }

    [Fact]
    public void WordBoundaries_EmptyText_StayAtZero()
    {
        Assert.Equal(0, TextCursor.NextWordBoundary("", 0));
        Assert.Equal(0, TextCursor.PreviousWordBoundary("", 0));
        Assert.Equal(0, TextCursor.NextWordBoundary(null, 3));
        Assert.Equal(0, TextCursor.PreviousWordBoundary(null, 3));
    }

    // ---------------------------------------------------------------
    // Lines.
    // ---------------------------------------------------------------

    [Fact]
    public void StartOfLine_MidSecondLine_LandsAfterTheNewline()
    {
        Assert.Equal(6, TextCursor.StartOfLine("alpha\nbeta", 8));
    }

    [Fact]
    public void EndOfLine_StopsBeforeTheNewline()
    {
        Assert.Equal(5, TextCursor.EndOfLine("alpha\nbeta", 2));
    }

    [Fact]
    public void EndOfLine_OnLastLine_IsTheEndOfTheText()
    {
        Assert.Equal(10, TextCursor.EndOfLine("alpha\nbeta", 8));
    }

    [Fact]
    public void LineAt_ReturnsTheLineWithoutItsNewline()
    {
        Assert.Equal("beta", TextCursor.LineAt("alpha\nbeta\ngamma", 7));
    }

    [Fact]
    public void LineAt_BlankLine_IsEmpty()
    {
        Assert.Equal("", TextCursor.LineAt("alpha\n\ngamma", 6));
    }

    [Fact]
    public void LineAt_EmptyText_IsEmpty()
    {
        Assert.Equal("", TextCursor.LineAt("", 0));
    }

    [Fact]
    public void LineDown_KeepsTheColumn()
    {
        // Column 3 of "alpha" -> column 3 of "bravo" (index 6 + 3).
        Assert.Equal(9, TextCursor.LineDown("alpha\nbravo", 3));
    }

    [Fact]
    public void LineDown_ClampsToAShorterLine()
    {
        // Column 4 of "alpha" -> the end of "hi" (index 6 + 2).
        Assert.Equal(8, TextCursor.LineDown("alpha\nhi\ngamma", 4));
    }

    [Fact]
    public void LineDown_OnLastLine_SnapsToTheEnd()
    {
        Assert.Equal(10, TextCursor.LineDown("alpha\nbeta", 7));
    }

    [Fact]
    public void LineUp_KeepsTheColumn()
    {
        Assert.Equal(3, TextCursor.LineUp("alpha\nbravo", 9));
    }

    [Fact]
    public void LineUp_ClampsToAShorterLine()
    {
        // Column 4 of "gamma" -> the end of "hi" (index 6 + 2).
        Assert.Equal(8, TextCursor.LineUp("alpha\nhi\ngamma", 13));
    }

    [Fact]
    public void LineUp_OnFirstLine_SnapsToTheStart()
    {
        Assert.Equal(0, TextCursor.LineUp("alpha\nbeta", 3));
    }

    [Fact]
    public void LineStepping_EmptyText_StaysAtZero()
    {
        Assert.Equal(0, TextCursor.LineUp("", 0));
        Assert.Equal(0, TextCursor.LineDown("", 0));
    }

    [Fact]
    public void LineStepping_WalksAStackTraceEndToEnd()
    {
        const string trace = "Exception thrown\n  at Verse.Log:Error\n  at RimWorld.Root:Update";
        int caret = 0;
        Assert.Equal("Exception thrown", TextCursor.LineAt(trace, caret));
        caret = TextCursor.LineDown(trace, caret);
        Assert.Equal("  at Verse.Log:Error", TextCursor.LineAt(trace, caret));
        caret = TextCursor.LineDown(trace, caret);
        Assert.Equal("  at RimWorld.Root:Update", TextCursor.LineAt(trace, caret));
        // Past the last line the caret rests at the end of the text, still on it.
        caret = TextCursor.LineDown(trace, caret);
        Assert.Equal(trace.Length, caret);
        Assert.Equal("  at RimWorld.Root:Update", TextCursor.LineAt(trace, caret));
    }

    // ---------------------------------------------------------------
    // Words under the caret.
    // ---------------------------------------------------------------

    [Fact]
    public void WordAt_OnAWordCharacter_ReturnsTheRestOfTheWord()
    {
        Assert.Equal("pha", TextCursor.WordAt("alpha beta", 2));
    }

    [Fact]
    public void WordAt_OnASeparator_IsEmpty()
    {
        Assert.Equal("", TextCursor.WordAt("alpha beta", 5));
    }

    [Fact]
    public void WordAt_PastTheEnd_IsEmpty()
    {
        Assert.Equal("", TextCursor.WordAt("alpha", 5));
    }

    [Fact]
    public void FirstWord_SkipsLeadingSeparators()
    {
        Assert.Equal("at", TextCursor.FirstWord("  at Verse.Log:Error"));
    }

    [Fact]
    public void LastWord_SkipsTrailingSeparators()
    {
        Assert.Equal("Error", TextCursor.LastWord("  at Verse.Log:Error."));
    }

    [Fact]
    public void FirstAndLastWord_WithoutAnyWords_AreEmpty()
    {
        Assert.Equal("", TextCursor.FirstWord("--- ..."));
        Assert.Equal("", TextCursor.LastWord("--- ..."));
        Assert.Equal("", TextCursor.FirstWord(""));
        Assert.Equal("", TextCursor.LastWord(""));
    }
}
