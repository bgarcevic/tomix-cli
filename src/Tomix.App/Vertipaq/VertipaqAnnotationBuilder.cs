using System.Globalization;
using Tomix.Core.Models;
using Tomix.Core.Vertipaq;

namespace Tomix.App.Vertipaq;

/// <summary>One mutation target: the object path plus its <c>Annotation:Vertipaq_*</c> assignments.</summary>
public sealed record VertipaqAnnotationTarget(
    string Path,
    ModelObjectKind? Type,
    IReadOnlyList<ModelPropertyAssignment> Assignments);

/// <summary>
/// Maps statistics to <c>Vertipaq_*</c> annotation assignments (the community naming convention,
/// so rule engines that consume these annotations keep working). Values are invariant-culture
/// raw numbers; the extraction date is ISO 8601 UTC. Row-number columns are skipped — they are
/// storage artifacts that cannot be addressed as model objects.
/// </summary>
public static class VertipaqAnnotationBuilder
{
    public static IReadOnlyList<VertipaqAnnotationTarget> Build(VertipaqModelStats stats)
    {
        var targets = new List<VertipaqAnnotationTarget>
        {
            new(".", null,
            [
                Annotation("Vertipaq_TotalSize", stats.TotalSize),
                Annotation("Vertipaq_TableCount", stats.TableCount),
                Annotation("Vertipaq_ColumnCount", stats.ColumnCount),
                new ModelPropertyAssignment(
                    "Annotation:Vertipaq_ExtractionDate",
                    (stats.ExtractionDate ?? DateTimeOffset.UtcNow).UtcDateTime
                        .ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture))
            ])
        };

        foreach (var table in stats.Tables)
        {
            targets.Add(new VertipaqAnnotationTarget(
                Segment(table.TableName),
                ModelObjectKind.Table,
                [
                    Annotation("Vertipaq_RowsCount", table.RowCount),
                    Annotation("Vertipaq_TableSize", table.TableSize),
                    Annotation("Vertipaq_ColumnsTotalSize", table.ColumnsTotalSize),
                    Annotation("Vertipaq_RelationshipsSize", table.RelationshipsSize),
                    Annotation("Vertipaq_UserHierarchiesSize", table.UserHierarchiesSize)
                ]));
        }

        foreach (var column in stats.Columns)
        {
            if (column.IsRowNumber)
                continue;

            targets.Add(new VertipaqAnnotationTarget(
                $"{Segment(column.TableName)}/{Segment(column.ColumnName)}",
                ModelObjectKind.Column,
                [
                    Annotation("Vertipaq_Cardinality", column.Cardinality),
                    Annotation("Vertipaq_TotalSize", column.TotalSize),
                    Annotation("Vertipaq_DataSize", column.DataSize),
                    Annotation("Vertipaq_DictionarySize", column.DictionarySize),
                    Annotation("Vertipaq_HierarchiesSize", column.HierarchiesSize),
                    new ModelPropertyAssignment("Annotation:Vertipaq_Encoding", column.Encoding)
                ]));
        }

        return targets;
    }

    private static ModelPropertyAssignment Annotation(string name, long value)
        => new($"Annotation:{name}", value.ToString(CultureInfo.InvariantCulture));

    // Same quoting rule as the TOM mutator's path parser: segments containing '/' are quoted.
    private static string Segment(string name)
        => name.Contains('/') ? $"'{name}'" : name;
}
