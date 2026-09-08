using RimWorldAccess;

namespace RimWorldAccess.Tests.Inspection
{
    public class InspectTextUtilityTests
    {
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   \n  \r ")]
        public void SplitLines_EmptyInputYieldsNoLines(string input)
        {
            Assert.Empty(InspectTextUtility.SplitLines(input));
        }

        [Fact]
        public void SplitLines_SplitsTrimsAndDropsBlanks()
        {
            Assert.Equal(new[] { "First", "Second", "Third" },
                InspectTextUtility.SplitLines("First\n  Second  \r\n\n   \nThird"));
        }

        [Fact]
        public void SplitLines_DropsLinesRedundantWithLabel()
        {
            Assert.Equal(new[] { "Duster", "Button-down shirt" },
                InspectTextUtility.SplitLines(
                    "Required apparel:\nDuster\nButton-down shirt",
                    "Required apparel"));
        }

        [Fact]
        public void SplitLines_RedundancyIgnoresCaseAndTrailingPunctuation()
        {
            Assert.Equal(new[] { "Detail" },
                InspectTextUtility.SplitLines("REQUIRED APPAREL.\nDetail", "Required apparel:"));
        }

        [Fact]
        public void SplitLines_NoLabelKeepsHeaderLines()
        {
            Assert.Equal(new[] { "Required apparel:", "Duster" },
                InspectTextUtility.SplitLines("Required apparel:\nDuster"));
        }

        [Theory]
        [InlineData("Required apparel:", "Required apparel", true)]
        [InlineData("required APPAREL. ", "Required apparel:", true)]
        [InlineData("Duster", "Required apparel", false)]
        [InlineData(null, "Label", false)]
        [InlineData("Line", null, false)]
        [InlineData("", "", false)]
        [InlineData(":.", "Label", false)]
        public void IsRedundantWith_ComparesNormalizedForms(string line, string label, bool expected)
        {
            Assert.Equal(expected, InspectTextUtility.IsRedundantWith(line, label));
        }
    }
}
