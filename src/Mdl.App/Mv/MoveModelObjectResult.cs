using System.Text.Json.Serialization;

namespace Mdl.App.Mv;

public sealed record MoveModelObjectResult(
    string Moved,
    string To,
    object Saved,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    bool? Staged);
