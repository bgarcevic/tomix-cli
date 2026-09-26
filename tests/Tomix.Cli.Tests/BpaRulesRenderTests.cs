using Tomix.Cli.Output;

namespace Tomix.Cli.Tests;

public class BpaRulesRenderTests
{
    [Fact]
    public void ExpressionLines_JoinsLoneOperatorsAndDropsBlankLines()
    {
        var expression = "IsAvailableInMDX\r\nand\r\n\n(IsHidden or Table.IsHidden)\r\nand\r\n  not UsedInSortBy.Any() \r\n";

        Assert.Equal(
            ["IsAvailableInMDX", "and (IsHidden or Table.IsHidden)", "and not UsedInSortBy.Any()"],
            BpaRulesRenderer.ExpressionLines(expression));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  \r\n ")]
    public void ExpressionLines_Empty_ReturnsNothing(string? expression)
        => Assert.Empty(BpaRulesRenderer.ExpressionLines(expression));

    [Fact]
    public void SplitReference_SeparatesTrailingLink()
    {
        var (text, reference) = BpaRulesRenderer.SplitReference("Hide it.\r\nReference: https://example.com/x");

        Assert.Equal("Hide it.", text);
        Assert.Equal("https://example.com/x", reference);
    }

    [Theory]
    [InlineData("Just guidance.", "Just guidance.")]
    [InlineData(null, "")]
    public void SplitReference_NoLink_KeepsText(string? description, string expected)
    {
        var (text, reference) = BpaRulesRenderer.SplitReference(description);

        Assert.Equal(expected, text);
        Assert.Null(reference);
    }
}
