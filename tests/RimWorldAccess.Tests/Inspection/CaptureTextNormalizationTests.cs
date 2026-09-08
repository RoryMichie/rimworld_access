using System;
using RimWorldAccess;
using Xunit;

namespace RimWorldAccess.Tests.Inspection
{
    public class CaptureTextNormalizationTests
    {
        // ---- NormalizeForContainment ----

        [Fact]
        public void NormalizeForContainment_FoldsCaseAndDropsPunctuationAndWhitespace()
        {
            Assert.Equal(
                CaptureTextNormalization.NormalizeForContainment("Dev tool: heal!"),
                CaptureTextNormalization.NormalizeForContainment("devtoolheal"));
            Assert.Equal("devtoolheal", CaptureTextNormalization.NormalizeForContainment("Dev tool: heal!"));
        }

        [Fact]
        public void NormalizeForContainment_KeepsDigits()
        {
            Assert.Equal("mood42", CaptureTextNormalization.NormalizeForContainment("Mood: 42%"));
        }

        [Fact]
        public void NormalizeForContainment_NullOrEmpty_ReturnsEmpty()
        {
            Assert.Equal("", CaptureTextNormalization.NormalizeForContainment(null));
            Assert.Equal("", CaptureTextNormalization.NormalizeForContainment(""));
        }

        [Fact]
        public void NormalizeForContainment_PunctuationOnly_ReturnsEmpty()
        {
            Assert.Equal("", CaptureTextNormalization.NormalizeForContainment("  -.,:  "));
        }

        // ---- NormalizeFragmentLine ----

        [Fact]
        public void NormalizeFragmentLine_TrimsSurroundingWhitespace()
        {
            Assert.Equal("Health", CaptureTextNormalization.NormalizeFragmentLine("   Health   "));
        }

        [Fact]
        public void NormalizeFragmentLine_TrimsTrailingEllipsis()
        {
            // Text.Truncate clips to the drawn rect; the ellipsis is not content.
            Assert.Equal("Operate on left", CaptureTextNormalization.NormalizeFragmentLine("Operate on left..."));
        }

        [Fact]
        public void NormalizeFragmentLine_EllipsisTrimIsNotReTrimmed()
        {
            // Matches the historical extractor: strip the three dots only, no
            // second trim — a space before the ellipsis survives.
            Assert.Equal("ab ", CaptureTextNormalization.NormalizeFragmentLine("ab ..."));
        }

        [Fact]
        public void NormalizeFragmentLine_RejectsShortLines()
        {
            Assert.Null(CaptureTextNormalization.NormalizeFragmentLine("ok"));
            Assert.Null(CaptureTextNormalization.NormalizeFragmentLine("  a  "));
        }

        [Fact]
        public void NormalizeFragmentLine_ShortAfterEllipsisTrim_Rejected()
        {
            // "12..." trims to "12" (length 2), below the three-char floor.
            Assert.Null(CaptureTextNormalization.NormalizeFragmentLine("12..."));
        }

        [Fact]
        public void NormalizeFragmentLine_NullOrEmpty_ReturnsNull()
        {
            Assert.Null(CaptureTextNormalization.NormalizeFragmentLine(null));
            Assert.Null(CaptureTextNormalization.NormalizeFragmentLine(""));
            Assert.Null(CaptureTextNormalization.NormalizeFragmentLine("     "));
        }

        [Fact]
        public void NormalizeFragmentLine_KeepsExactlyThreeChars()
        {
            Assert.Equal("Age", CaptureTextNormalization.NormalizeFragmentLine("Age"));
        }

        // ---- IsUnmirrored ----

        private static string Haystack(params string[] rawLines)
        {
            return CaptureTextNormalization.NormalizeForContainment(string.Join("\n", rawLines));
        }

        [Fact]
        public void IsUnmirrored_FalseWhenTextFullyContainedInHaystack()
        {
            string haystack = Haystack("Dev tool: heal", "Some other line");
            Assert.False(CaptureTextNormalization.IsUnmirrored("Dev tool: heal", haystack, s => s));
        }

        [Fact]
        public void IsUnmirrored_TrueWhenLineAbsentFromHaystack()
        {
            string haystack = Haystack("Something else entirely");
            Assert.True(CaptureTextNormalization.IsUnmirrored("Dev tool: heal", haystack, s => s));
        }

        [Fact]
        public void IsUnmirrored_MultiLine_TrueWhenAnyLineAbsent()
        {
            string haystack = Haystack("First matching line");
            Assert.True(CaptureTextNormalization.IsUnmirrored("First matching line\nSecond absent line", haystack, s => s));
        }

        [Fact]
        public void IsUnmirrored_MultiLine_FalseWhenEveryLineMatches()
        {
            string haystack = Haystack("First matching line", "Second matching line");
            Assert.False(CaptureTextNormalization.IsUnmirrored("First matching line\nSecond matching line", haystack, s => s));
        }

        [Fact]
        public void IsUnmirrored_BlankText_False()
        {
            Assert.False(CaptureTextNormalization.IsUnmirrored("", "anything", s => s));
            Assert.False(CaptureTextNormalization.IsUnmirrored(null, "anything", s => s));
            Assert.False(CaptureTextNormalization.IsUnmirrored("   ", "anything", s => s));
        }

        [Fact]
        public void IsUnmirrored_ShortLinesBelowFloor_NeverCountAsUnmirrored()
        {
            // "ok" is below NormalizeFragmentLine's 3-char floor, so it never
            // gets a chance to be absent from the haystack.
            Assert.False(CaptureTextNormalization.IsUnmirrored("ok", "completely unrelated haystack", s => s));
        }

        [Fact]
        public void IsUnmirrored_UsesStripTagsCallbackPerLine()
        {
            string haystack = Haystack("colored text");
            bool called = false;
            Func<string, string> stripTags = s => { called = true; return s.Replace("<color=red>", "").Replace("</color>", ""); };
            Assert.False(CaptureTextNormalization.IsUnmirrored("<color=red>colored text</color>", haystack, stripTags));
            Assert.True(called);
        }
    }
}
