using System.Text.Json.Serialization;
using Tomix.Core.Models;

namespace Tomix.App.IncrementalRefresh;

public sealed record SetRefreshPolicyResult(
    string Table,
    bool Created,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    RefreshPolicyInfo? Policy,
    object Saved,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<string>? CreatedExpressions = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<RefreshPolicyIssue>? Warnings = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    bool? Staged = null,
    bool Synced = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? SyncTarget = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? SyncWarning = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    bool Reverted = false);
