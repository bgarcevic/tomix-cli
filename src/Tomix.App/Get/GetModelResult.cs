using System.Text.Json.Serialization;
using Tomix.Core.Models;

namespace Tomix.App.Get;

public sealed record GetModelResult(
    string Type,
    string Path,
    IReadOnlyDictionary<string, object?> Properties,
    [property: JsonIgnore] ModelObject Object,
    [property: JsonIgnore] IReadOnlySet<string>? MeasureNames = null)
{
    /// <summary>Object counts, reported for the model root ("."); null for any other object.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ModelCounts? Counts { get; init; }
}

public sealed record ModelCounts(int Tables, int Columns, int Measures, int Relationships, int Roles);
