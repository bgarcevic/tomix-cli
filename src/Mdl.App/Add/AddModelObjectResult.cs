using System.Text.Json.Serialization;

namespace Mdl.App.Add;

public sealed record AddModelObjectResult(
    object Added,
    object Saved,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    bool? Staged);
