using Microsoft.AnalysisServices.Tabular;
using Tomix.Core.Models;

namespace Tomix.Provider.Tom;

/// <summary>
/// Entry point for all TOM model mutations, shared by the file, server, and TMDL sessions.
/// A thin facade over the mutation collaborators: <see cref="TomObjectAdder"/> (add),
/// <see cref="TomMutationTargetResolver"/> (path → object), <see cref="TomPropertyApplier"/>
/// (set/rewrite), <see cref="TomTextReplacer"/> (replace), and <see cref="TomRemoveCascade"/>
/// (remove cascades).
/// </summary>
public sealed class TomModelMutator
{
    private readonly Database _database;
    private readonly TomObjectAdder _adder;
    private readonly TomMutationTargetResolver _resolver;
    private readonly TomTextReplacer _replacer;

    public TomModelMutator(Database database)
    {
        _database = database;
        _adder = new TomObjectAdder(database);
        _resolver = new TomMutationTargetResolver(database);
        _replacer = new TomTextReplacer(database);
    }

    public ModelObjectMutationResult AddObject(ModelObjectAddRequest request)
        => _adder.AddObject(request);

    public ModelObjectMutationResult SetProperty(ModelObjectSetRequest request)
    {
        if (request.Properties.Count == 0)
            throw new ArgumentException("At least one property assignment is required.", nameof(request));

        var target = _resolver.TryResolveForMutation(request.Path, request.Type)
                     ?? throw TomMutationTargetResolver.NotFound(request.Path);

        ModelPropertyAssignment last = request.Properties[^1];
        foreach (var assignment in request.Properties)
        {
            TomPropertyApplier.ApplyProperty(target.Target, assignment);
            last = assignment;
        }

        return new ModelObjectMutationResult(
            target.Display,
            Changed: true,
            Property: last.Property,
            Value: last.Value);
    }

    public ModelExpressionRewriteResult RewriteExpressions(IReadOnlyList<ModelExpressionEdit> edits)
    {
        foreach (var edit in edits)
        {
            var resolved = _resolver.TryResolveForMutation(edit.Path, edit.Kind)
                           ?? throw TomMutationTargetResolver.NotFound(edit.Path);
            TomPropertyApplier.ApplyExpressionEdit(resolved.Target, edit);
        }

        return new ModelExpressionRewriteResult(edits.Count);
    }

    public ModelObjectMutationResult RemoveObject(ModelObjectRemoveRequest request)
    {
        var target = _resolver.TryResolveForMutation(request.Path, request.Type);
        if (target is null)
        {
            if (request.IfExists)
                return new ModelObjectMutationResult(TomMutationPaths.NormalizePath(request.Path), Changed: false, Reason: "not_found");

            throw TomMutationTargetResolver.NotFound(request.Path);
        }

        var cascade = RemoveResolvedObject(target);
        return new ModelObjectMutationResult(
            target.Display, Changed: true,
            CascadeRemoved: cascade.Count > 0 ? cascade : null);
    }

    public ModelReplaceResult ReplaceText(ModelReplaceRequest request)
        => _replacer.Replace(request);

    private IReadOnlyList<string> RemoveResolvedObject(TomResolvedObject resolved)
    {
        switch (resolved.Target)
        {
            case Table table:
                {
                    var cascade = TomRemoveCascade.ForTable(table);
                    _database.Model.Tables.Remove(table);
                    return cascade;
                }

            case Measure measure when resolved.Parent is Table table:
                {
                    var cascade = TomRemoveCascade.ForMeasure(measure);
                    table.Measures.Remove(measure);
                    return cascade;
                }

            case Column column when resolved.Parent is Table table:
                {
                    var cascade = TomRemoveCascade.ForColumn(column);
                    table.Columns.Remove(column);
                    return cascade;
                }

            case Hierarchy hierarchy when resolved.Parent is Table table:
                {
                    var cascade = TomRemoveCascade.ForHierarchy(hierarchy);
                    table.Hierarchies.Remove(hierarchy);
                    return cascade;
                }

            case Partition partition when resolved.Parent is Table table:
                if (table.Partitions.Count == 1)
                    throw new InvalidOperationException(
                        $"Cannot remove the last partition of table '{table.Name}'; a table must have at least one partition.");

                table.Partitions.Remove(partition);
                return [];

            case ModelRole role:
                _database.Model.Roles.Remove(role);
                return [];

            case SingleColumnRelationship relationship:
                {
                    var cascade = TomRemoveCascade.ForRelationship(relationship);
                    _database.Model.Relationships.Remove(relationship);
                    return cascade;
                }

            case Level level when resolved.Parent is Hierarchy hierarchy:
                {
                    var cascade = new List<string>();
                    cascade.AddRange(TomRemoveCascade.ForLevel(level));
                    hierarchy.Levels.Remove(level);
                    if (hierarchy.Levels.Count == 0)
                    {
                        var table = hierarchy.Table;
                        cascade.AddRange(TomRemoveCascade.ForHierarchy(hierarchy));
                        table.Hierarchies.Remove(hierarchy);
                        cascade.Add($"hierarchy '{table.Name}'[{hierarchy.Name}] (no levels left)");
                    }

                    return cascade;
                }

            case CalculationItem item when resolved.Parent is Table calcGroupTable:
                {
                    var cascade = TomRemoveCascade.ForCalculationItem(item);
                    calcGroupTable.CalculationGroup.CalculationItems.Remove(item);
                    return cascade;
                }

            case ModelRoleMember member when resolved.Parent is ModelRole memberRole:
                memberRole.Members.Remove(member);
                return [];

            case Perspective perspective:
                _database.Model.Perspectives.Remove(perspective);
                return [];

            case Culture culture:
                _database.Model.Cultures.Remove(culture);
                return [];

            case NamedExpression expression:
                _database.Model.Expressions.Remove(expression);
                return [];

            case Function function:
                _database.Model.Functions.Remove(function);
                return [];

            case DataSource dataSource:
                {
                    // M partitions can reference a data source by name inside their query text,
                    // which no structural sweep can see — but a QueryPartitionSource binding is
                    // explicit and would fail validation the moment the source disappears.
                    var referencing = _database.Model.Tables
                        .SelectMany(t => t.Partitions
                            .Where(p => p.Source is QueryPartitionSource query && query.DataSource == dataSource)
                            .Select(p => $"{t.Name}/{p.Name}"))
                        .ToList();
                    if (referencing.Count > 0)
                        throw new InvalidOperationException(
                            $"Cannot remove data source '{dataSource.Name}'; it is used by partition(s): "
                            + $"{string.Join(", ", referencing)}. Repoint or remove those partitions first.");

                    _database.Model.DataSources.Remove(dataSource);
                    return [];
                }

            default:
                throw new NotSupportedException("Removing this object type is not supported.");
        }
    }
}
