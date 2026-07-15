using Tomix.App.Vertipaq;
using Tomix.Core.Models;
using Tomix.Core.Vertipaq;

namespace Tomix.App.Tests;

public sealed class VertipaqAnnotationBuilderTests
{
    [Fact]
    public void Build_EmitsModelRootAnnotations_WithInvariantValues()
    {
        var targets = VertipaqAnnotationBuilder.Build(NewStats());

        var root = Assert.Single(targets, t => t.Path == ".");
        Assert.Null(root.Type);
        Assert.Contains(root.Assignments, a => a is { Property: "Annotation:Vertipaq_TotalSize", Value: "1430" });
        Assert.Contains(root.Assignments, a => a is { Property: "Annotation:Vertipaq_TableCount", Value: "2" });
        Assert.Contains(root.Assignments, a => a is { Property: "Annotation:Vertipaq_ColumnCount", Value: "3" });
        Assert.Contains(root.Assignments, a => a is
        {
            Property: "Annotation:Vertipaq_ExtractionDate",
            Value: "2026-07-14T12:00:00Z"
        });
    }

    [Fact]
    public void Build_EmitsTableAnnotations()
    {
        var targets = VertipaqAnnotationBuilder.Build(NewStats());

        var sales = Assert.Single(targets, t => t.Path == "Sales");
        Assert.Equal(ModelObjectKind.Table, sales.Type);
        Assert.Contains(sales.Assignments, a => a is { Property: "Annotation:Vertipaq_RowsCount", Value: "1000" });
        Assert.Contains(sales.Assignments, a => a is { Property: "Annotation:Vertipaq_TableSize", Value: "1300" });
    }

    [Fact]
    public void Build_EmitsColumnAnnotations_AndSkipsRowNumberColumns()
    {
        var targets = VertipaqAnnotationBuilder.Build(NewStats());

        var amount = Assert.Single(targets, t => t.Path == "Sales/Amount");
        Assert.Equal(ModelObjectKind.Column, amount.Type);
        Assert.Contains(amount.Assignments, a => a is { Property: "Annotation:Vertipaq_Cardinality", Value: "900" });
        Assert.Contains(amount.Assignments, a => a is { Property: "Annotation:Vertipaq_Encoding", Value: "VALUE" });

        Assert.DoesNotContain(targets, t => t.Path.Contains("RowNumber"));
    }

    [Fact]
    public void Build_QuotesSegmentsContainingSlashes()
    {
        var stats = NewStats() with
        {
            Tables =
            [
                new VertipaqTableStats("A/B", 1, 1, 1, 1, 0, 0, 0, 0, 100, 1, 1, 1, true)
            ],
            Columns =
            [
                NewColumn("A/B", "C/D", 1, "HASH")
            ]
        };

        var targets = VertipaqAnnotationBuilder.Build(stats);

        Assert.Contains(targets, t => t.Path == "'A/B'");
        Assert.Contains(targets, t => t.Path == "'A/B'/'C/D'");
    }

    private static VertipaqModelStats NewStats()
        => new(
            "SalesModel", "server", new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero),
            TotalSize: 1430, TableCount: 2, ColumnCount: 3, MaxRowCount: 1000,
            Tables:
            [
                new VertipaqTableStats("Sales", 1000, 1300, 1300, 800, 500, 0, 24, 0, 89.4, 2, 1, 2, true)
            ],
            Columns:
            [
                NewColumn("Sales", "Amount", 900, "VALUE"),
                NewColumn("Sales", "RowNumber-2662979B", 0, "HASH", isRowNumber: true)
            ],
            Relationships: [],
            Partitions: []);

    private static VertipaqColumnStats NewColumn(
        string table, string name, long cardinality, string encoding, bool isRowNumber = false)
        => new(
            table, name, cardinality, "Int64", encoding, 1000, 600, 400, 0,
            10, 20, 0.1, 1, 1, false, true, isRowNumber, "Ready");
}
