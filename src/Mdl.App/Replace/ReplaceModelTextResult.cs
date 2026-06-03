using System.Text.Json.Serialization;
using Mdl.Core.Models;

namespace Mdl.App.Replace;

public sealed record ReplaceModelTextResult(
    string Pattern,
    string Replacement,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    bool? DryRun,
    int ChangeCount,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<ModelReplacePreview>? Previews,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    object? Saved,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    bool? Staged = null);
