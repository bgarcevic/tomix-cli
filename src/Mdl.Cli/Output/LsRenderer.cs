using System.Text;
using System.Text.RegularExpressions;
using Mdl.App.Ls;
using Mdl.Core.Models;

namespace Mdl.Cli.Output;

/// <summary>
/// Renders an <see cref="LsModelResult"/> for human (text) output. A homogeneous list of tables
/// gets dedicated count columns; anything else is grouped by kind with per-kind column sets.
/// </summary>
internal sealed partial class LsRenderer
{
    private const int MaxCollapsedDetail = 80;

    public static void Render(LsModelResult data, bool pathsOnly, bool noMultiline)
    {
        if (pathsOnly)
        {
            foreach (var obj in data.Objects)
                Console.WriteLine(obj.Path);

            return;
        }

        var allTables = data.Objects.Count > 0 && data.Objects.All(o => o.Kind == ModelObjectKind.Table);

        if (allTables)
        {
            Console.WriteLine($"Tables ({data.Objects.Count})");
            RenderTables(data.Objects);
        }
        else
        {
            RenderGrouped(data.Objects, noMultiline);
        }
    }

    private static void RenderTables(IReadOnlyList<LsObject> objects)
    {
        RenderBoxTable(
            ["Name", "Columns", "Measures", "Partitions", "Hidden", "Description"],
            objects.Select(o => (IReadOnlyList<string>)
            [
                o.Name,
                Count(o, ModelObjectKind.Column),
                Count(o, ModelObjectKind.Measure),
                Count(o, ModelObjectKind.Partition),
                BoolText(o.Hidden),
                o.Description ?? ""
            ]).ToList());
    }

    private static void RenderGrouped(IReadOnlyList<LsObject> objects, bool noMultiline)
    {
        var groups = objects
            .GroupBy(o => o.Kind)
            .OrderBy(g => KindOrder(g.Key))
            .Select(g => (Kind: g.Key, Items: (IReadOnlyList<LsObject>)g.ToList()))
            .ToList();

        var first = true;
        foreach (var (kind, items) in groups)
        {
            if (!first)
                Console.WriteLine();
            first = false;

            Console.WriteLine($"{KindPlural(kind)} ({items.Count})");

            switch (kind)
            {
                case ModelObjectKind.Column:
                    RenderColumns(items);
                    break;
                case ModelObjectKind.Measure:
                    RenderMeasures(items, noMultiline);
                    break;
                case ModelObjectKind.Hierarchy:
                    Console.WriteLine();
                    RenderHierarchies(items);
                    break;
                case ModelObjectKind.Partition:
                    Console.WriteLine();
                    RenderPartitions(items, noMultiline);
                    break;
                case ModelObjectKind.Level:
                    Console.WriteLine();
                    RenderLevels(items);
                    break;
                default:
                    Console.WriteLine();
                    RenderGeneric(items, noMultiline);
                    break;
            }
        }
    }

    private static void RenderColumns(IReadOnlyList<LsObject> objects)
    {
        RenderBoxTable(
            ["Name", "SourceColumn", "DataType", "Description", "Hidden"],
            objects.Select(o => (IReadOnlyList<string>)
            [
                o.Name,
                o.SourceColumn ?? "",
                ColumnDataTypeDisplay(o),
                o.Description ?? "",
                BoolText(o.Hidden)
            ]).ToList());
    }

    private static void RenderMeasures(IReadOnlyList<LsObject> objects, bool noMultiline)
    {
        RenderBoxTable(
            ["Name", "Description", "Hidden", "Expression", "FormatString"],
            objects.Select(o => (IReadOnlyList<string>)
            [
                o.Name,
                o.Description ?? "",
                BoolText(o.Hidden),
                ExpressionLines(o, noMultiline)[0],
                Property(o, "FormatString", "")
            ]).ToList());
    }

    private static void RenderHierarchies(IReadOnlyList<LsObject> objects)
    {
        var showDescription = objects.Any(o => !string.IsNullOrEmpty(o.Description));

        var nameWidth = Math.Max("NAME".Length, objects.Max(o => o.Name.Length));
        var levelsWidth = NumberColumnWidth("LEVELS", objects, ModelObjectKind.Level);
        const int hiddenWidth = 6;

        var header = new StringBuilder()
            .Append("NAME".PadRight(nameWidth)).Append("  ")
            .Append("LEVELS".PadLeft(levelsWidth)).Append("  ")
            .Append("HIDDEN".PadRight(hiddenWidth));
        if (showDescription)
            header.Append("  ").Append("DESCRIPTION");
        Console.WriteLine(header.ToString().TrimEnd());

        foreach (var obj in objects)
        {
            var row = new StringBuilder()
                .Append(obj.Name.PadRight(nameWidth)).Append("  ")
                .Append(Count(obj, ModelObjectKind.Level).PadLeft(levelsWidth)).Append("  ")
                .Append((obj.Hidden ? "yes" : "no").PadRight(hiddenWidth));
            if (showDescription)
                row.Append("  ").Append(obj.Description ?? "");
            Console.WriteLine(row.ToString().TrimEnd());
        }
    }

    private static void RenderPartitions(IReadOnlyList<LsObject> objects, bool noMultiline)
    {
        var showDescription = objects.Any(o => !string.IsNullOrEmpty(o.Description));

        var nameWidth = Math.Max("NAME".Length, objects.Max(o => o.Name.Length));
        var modeWidth = Math.Max("MODE".Length, objects.Max(o => (o.Detail ?? "").Length));
        var continuation = new string(' ', nameWidth + 2 + modeWidth + 2);
        var exprLines = objects.ToDictionary(o => o, o => ExpressionLines(o, noMultiline));
        var exprWidth = showDescription
            ? Math.Max("EXPRESSION".Length, exprLines.Values.Max(ls => ls[0].Length))
            : 0;

        var header = new StringBuilder()
            .Append("NAME".PadRight(nameWidth)).Append("  ")
            .Append("MODE".PadRight(modeWidth)).Append("  ");
        header.Append(showDescription ? $"{"EXPRESSION".PadRight(exprWidth)}  DESCRIPTION" : "EXPRESSION");
        Console.WriteLine(header.ToString().TrimEnd());

        foreach (var obj in objects)
        {
            var lines = exprLines[obj];
            var firstExpr = showDescription ? lines[0].PadRight(exprWidth) : lines[0];
            var head = new StringBuilder()
                .Append(obj.Name.PadRight(nameWidth)).Append("  ")
                .Append((obj.Detail ?? "").PadRight(modeWidth)).Append("  ")
                .Append(firstExpr);
            if (showDescription)
                head.Append("  ").Append(obj.Description ?? "");
            Console.WriteLine(head.ToString().TrimEnd());

            for (var i = 1; i < lines.Count; i++)
                Console.WriteLine($"{continuation}{lines[i]}");
        }
    }

    private static void RenderLevels(IReadOnlyList<LsObject> objects)
    {
        var showDescription = objects.Any(o => !string.IsNullOrEmpty(o.Description));

        var nameWidth = Math.Max("NAME".Length, objects.Max(o => o.Name.Length));
        var colWidth = Math.Max("COLUMN".Length, objects.Max(o => (o.Detail ?? "").Length));

        var header = new StringBuilder()
            .Append("NAME".PadRight(nameWidth)).Append("  ")
            .Append("COLUMN".PadRight(colWidth));
        if (showDescription)
            header.Append("  ").Append("DESCRIPTION");
        Console.WriteLine(header.ToString().TrimEnd());

        foreach (var obj in objects)
        {
            var row = new StringBuilder()
                .Append(obj.Name.PadRight(nameWidth)).Append("  ")
                .Append((obj.Detail ?? "").PadRight(colWidth));
            if (showDescription)
                row.Append("  ").Append(obj.Description ?? "");
            Console.WriteLine(row.ToString().TrimEnd());
        }
    }

    private static void RenderGeneric(IReadOnlyList<LsObject> objects, bool noMultiline)
    {
        var showDescription = objects.Any(o => !string.IsNullOrEmpty(o.Description));

        var nameWidth = Math.Max("NAME".Length, objects.Max(o => o.Name.Length));
        var continuation = new string(' ', nameWidth + 2);
        var detailLines = objects.ToDictionary(o => o, o => DetailLines(o, noMultiline));
        var detailWidth = showDescription
            ? Math.Max("DETAIL".Length, detailLines.Values.Max(ls => ls[0].Length))
            : 0;

        var header = new StringBuilder().Append("NAME".PadRight(nameWidth)).Append("  ");
        header.Append(showDescription ? $"{"DETAIL".PadRight(detailWidth)}  DESCRIPTION" : "DETAIL");
        Console.WriteLine(header.ToString().TrimEnd());

        foreach (var obj in objects)
        {
            var lines = detailLines[obj];
            var firstDetail = showDescription ? lines[0].PadRight(detailWidth) : lines[0];
            var head = new StringBuilder()
                .Append(obj.Name.PadRight(nameWidth)).Append("  ")
                .Append(firstDetail);
            if (showDescription)
                head.Append("  ").Append(obj.Description ?? "");
            Console.WriteLine(head.ToString().TrimEnd());

            for (var i = 1; i < lines.Count; i++)
                Console.WriteLine($"{continuation}{lines[i]}");
        }
    }

    private static string Count(LsObject obj, ModelObjectKind kind)
        => obj.ChildCounts.GetValueOrDefault(kind).ToString();

    private static void RenderBoxTable(
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyList<string>> rows)
    {
        if (headers.Count == 0)
            return;

        var widths = headers
            .Select((header, index) => Math.Max(header.Length, rows.Count == 0 ? 0 : rows.Max(row => row[index].Length)))
            .ToArray();
        widths[0]++;

        var blank = new string(' ', BoxRow(headers, widths).Length);
        Console.WriteLine(blank);
        Console.WriteLine(BoxRow(headers, widths));
        Console.WriteLine(BoxSeparator(widths));

        foreach (var row in rows)
            Console.WriteLine(BoxRow(row, widths));

        Console.WriteLine(blank);
    }

    private static string BoxRow(IReadOnlyList<string> cells, IReadOnlyList<int> widths)
        => string.Join("│", cells.Select((cell, index) => BoxCell(cell, widths[index], index, cells.Count)));

    private static string BoxCell(string value, int width, int index, int count)
    {
        if (index == 0)
            return "  " + value.PadRight(width);

        return index == count - 1
            ? " " + value.PadRight(width) + "  "
            : " " + value.PadRight(width) + " ";
    }

    private static string BoxSeparator(IReadOnlyList<int> widths)
        => string.Join("┼", widths.Select((width, index) => BoxSeparatorCell(width, index, widths.Count)));

    private static string BoxSeparatorCell(int width, int index, int count)
    {
        if (index == 0)
            return " " + new string('─', width + 1);

        return index == count - 1
            ? new string('─', width + 2) + " "
            : new string('─', width + 2);
    }

    private static string BoolText(bool value)
        => value ? "True" : "False";

    private static string ColumnDataTypeDisplay(LsObject obj)
        => DataTypeDisplay(Property(obj, "DataType", obj.Detail ?? ""));

    private static string Property(LsObject obj, string key, string fallback)
        => obj.Properties is not null && obj.Properties.TryGetValue(key, out var value)
            ? value
            : fallback;

    private static string DataTypeDisplay(string value)
        => value.Trim().ToLowerInvariant() switch
        {
            "int64" => "Integer / Whole Number (int64)",
            "decimal" => "Currency / Fixed Decimal Number (decimal)",
            "string" => "String / Text",
            "double" => "Decimal Number (double)",
            "boolean" or "bool" => "Boolean / True/False",
            "datetime" => "DateTime / Date/Time",
            _ => value
        };

    private static int NumberColumnWidth(string header, IReadOnlyList<LsObject> objects, ModelObjectKind kind)
        => Math.Max(header.Length, objects.Max(o => Count(o, kind).Length));

    private static IReadOnlyList<string> ExpressionLines(LsObject obj, bool noMultiline)
        => DetailLines(obj.Expression ?? obj.Detail ?? "", noMultiline);

    private static IReadOnlyList<string> DetailLines(LsObject obj, bool noMultiline)
        => DetailLines(obj.Expression ?? obj.Detail ?? "", noMultiline);

    private static IReadOnlyList<string> DetailLines(string detail, bool noMultiline)
    {
        if (noMultiline)
            return [Collapse(detail)];

        var lines = detail
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Replace("\t", "    ")
            .Split('\n')
            .Select(l => l.TrimEnd())
            .ToList();

        while (lines.Count > 0 && lines[0].Length == 0)
            lines.RemoveAt(0);
        while (lines.Count > 0 && lines[^1].Length == 0)
            lines.RemoveAt(lines.Count - 1);

        if (lines.Count == 0)
            return [""];

        var indent = lines.Where(l => l.Length > 0).Min(l => l.Length - l.TrimStart().Length);
        return indent == 0 ? lines : lines.Select(l => l.Length >= indent ? l[indent..] : l).ToList();
    }

    private static string Collapse(string text)
    {
        var single = Whitespace().Replace(text, " ").Trim();
        return single.Length > MaxCollapsedDetail
            ? single[..(MaxCollapsedDetail - 3)] + "..."
            : single;
    }

    private static int KindOrder(ModelObjectKind kind) => kind switch
    {
        ModelObjectKind.Column => 0,
        ModelObjectKind.Measure => 1,
        ModelObjectKind.Hierarchy => 2,
        ModelObjectKind.Level => 3,
        ModelObjectKind.Partition => 4,
        ModelObjectKind.Relationship => 5,
        ModelObjectKind.Role => 6,
        ModelObjectKind.RoleMember => 7,
        ModelObjectKind.Perspective => 8,
        ModelObjectKind.Culture => 9,
        _ => 10
    };

    private static string KindPlural(ModelObjectKind kind) => kind switch
    {
        ModelObjectKind.Column => "Columns",
        ModelObjectKind.Measure => "Measures",
        ModelObjectKind.Hierarchy => "Hierarchies",
        ModelObjectKind.Level => "Levels",
        ModelObjectKind.Partition => "Partitions",
        ModelObjectKind.Relationship => "Relationships",
        ModelObjectKind.Role => "Roles",
        ModelObjectKind.RoleMember => "Members",
        ModelObjectKind.Perspective => "Perspectives",
        ModelObjectKind.Culture => "Cultures",
        _ => kind.ToString() + "s"
    };

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
