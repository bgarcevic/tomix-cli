using Mdl.Core.Models;

namespace Mdl.App.ModelObjects;

internal static class ModelObjectProjection
{
    public static IReadOnlyList<ModelObject> Flatten(ModelSnapshot snapshot)
    {
        var objects = new List<ModelObject>();

        void Walk(IEnumerable<ModelObject> nodes)
        {
            foreach (var node in nodes)
            {
                objects.Add(node);
                Walk(node.Children);
            }
        }

        Walk(snapshot.Objects);
        return objects;
    }

    public static IReadOnlyDictionary<string, object?> ToProperties(ModelObject obj)
    {
        var counts = obj.Children.GroupBy(c => c.Kind).ToDictionary(g => g.Key, g => g.Count());

        if (obj.Kind == ModelObjectKind.Table)
        {
            return new Dictionary<string, object?>
            {
                ["name"] = obj.Name,
                ["description"] = obj.Description ?? "",
                ["isHidden"] = obj.Hidden,
                ["dataCategory"] = "",
                ["lineageTag"] = "",
                ["columns"] = counts.GetValueOrDefault(ModelObjectKind.Column),
                ["measures"] = counts.GetValueOrDefault(ModelObjectKind.Measure),
                ["hierarchies"] = counts.GetValueOrDefault(ModelObjectKind.Hierarchy),
                ["partitions"] = counts.GetValueOrDefault(ModelObjectKind.Partition),
                ["refreshPolicy"] = null,
                ["defaultDetailRowsExpression"] = null
            };
        }

        if (obj.Kind == ModelObjectKind.Measure)
        {
            return new Dictionary<string, object?>
            {
                ["name"] = obj.Name,
                ["description"] = obj.Description ?? "",
                ["isHidden"] = obj.Hidden,
                ["expression"] = obj.Expression ?? "",
                ["formatString"] = null,
                ["displayFolder"] = "",
                ["lineageTag"] = ""
            };
        }

        return new Dictionary<string, object?>
        {
            ["name"] = obj.Name,
            ["description"] = obj.Description ?? "",
            ["isHidden"] = obj.Hidden,
            ["detail"] = obj.Detail,
            ["expression"] = obj.Expression
        };
    }

    public static string KindLabel(ModelObjectKind kind) => kind switch
    {
        ModelObjectKind.RoleMember => "RoleMember",
        _ => kind.ToString()
    };
}
