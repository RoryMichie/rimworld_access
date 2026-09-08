using RimWorldAccess;

namespace RimWorldAccess.Tests.Inspection
{
    public class GizmoTextUtilityTests
    {
        [Theory]
        [InlineData(null, "")]
        [InlineData("", "")]
        [InlineData("plain text", "plain text")]
        public void FlattenNewlines_PassesThroughFlatText(string input, string expected)
        {
            Assert.Equal(expected, GizmoTextUtility.FlattenNewlines(input));
        }

        [Fact]
        public void FlattenNewlines_TurnsNewlineRunIntoSentenceBreak()
        {
            Assert.Equal("First line. Second line",
                GizmoTextUtility.FlattenNewlines("First line\n\nSecond line"));
        }

        [Fact]
        public void FlattenNewlines_DoesNotDoublePunctuate()
        {
            Assert.Equal("Ends with period. Next",
                GizmoTextUtility.FlattenNewlines("Ends with period.\nNext"));
            Assert.Equal("Ends with colon: Next",
                GizmoTextUtility.FlattenNewlines("Ends with colon:\nNext"));
            Assert.Equal("Question? Next",
                GizmoTextUtility.FlattenNewlines("Question?\nNext"));
        }

        [Fact]
        public void FlattenNewlines_TrimsWhitespaceAroundBreaks()
        {
            Assert.Equal("Trailing spaces. Indented continuation",
                GizmoTextUtility.FlattenNewlines("Trailing spaces   \n    Indented continuation"));
        }

        [Fact]
        public void FlattenNewlines_LeadingNewlinesProduceNoSeparator_TrailingNewlinesCloseTheSentence()
        {
            Assert.Equal("Body.", GizmoTextUtility.FlattenNewlines("\n\nBody\n\n"));
        }

        [Fact]
        public void FlattenNewlines_HandlesCarriageReturnPairs()
        {
            Assert.Equal("One. Two",
                GizmoTextUtility.FlattenNewlines("One\r\nTwo"));
        }
    }
}
