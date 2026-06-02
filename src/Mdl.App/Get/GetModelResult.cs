using System.Text.Json.Serialization;
using Mdl.Core.Models;

namespace Mdl.App.Get;

public sealed record GetModelResult(
    string Type,
    string Path,
    IReadOnlyDictionary<string, object?> Properties,
    [property: JsonIgnore] ModelObject Object);
