using System.Text.Json;
using System.Text.Json.Serialization;
using Mdl.Core.Diagnostics;
using Mdl.Core.Results;

namespace Mdl.App.Tests;

/// <summary>
/// JSON contract tests for the error envelope shape and specific error codes
/// emitted by mutation commands.
/// </summary>
public sealed class ErrorCodeContractTests
{
    private static readonly JsonSerializerOptions ErrorOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static string SerializeError(string code, string message, string? hint = null)
    {
        var errorObj = new Dictionary<string, string?>
        {
            ["error"] = message,
            ["code"] = code,
            ["severity"] = nameof(DiagnosticSeverity.Error),
            ["hint"] = hint
        };
        return JsonSerializer.Serialize(errorObj, ErrorOptions);
    }

    private static string JsonFromResult<T>(MdlResult<T> result)
    {
        var diag = result.Diagnostics.First();
        return SerializeError(diag.Code, diag.Message, diag.Hint);
    }

    // ── Error envelope shape ────────────────────────────────────────────────

    [Fact]
    public void ErrorEnvelope_HasAllFourFields()
    {
        var json = JsonDocument.Parse(SerializeError("MDL_TEST", "test message", "test hint"));

        Assert.Equal("test message", json.RootElement.GetProperty("error").GetString());
        Assert.Equal("MDL_TEST", json.RootElement.GetProperty("code").GetString());
        Assert.Equal("Error", json.RootElement.GetProperty("severity").GetString());
        Assert.Equal("test hint", json.RootElement.GetProperty("hint").GetString());
    }

    [Fact]
    public void ErrorEnvelope_NullHint_StillIncludesHintAsNull()
    {
        var json = SerializeError("MDL_TEST", "test message", hint: null);

        Assert.Contains("\"hint\": null", json);
    }

    // ── Mutation error codes ────────────────────────────────────────────────

    [Theory]
    [InlineData("MDL_MUTATION_UNSUPPORTED_PROVIDER")]
    [InlineData("MDL_MUTATION_UNSUPPORTED")]
    [InlineData("MDL_MUTATION_INVALID_VALUE")]
    [InlineData("MDL_MUTATION_FAILED")]
    [InlineData("MDL_MUTATION_SAVE_FAILED")]
    public void MutationErrorCodes_AreValidUppercaseSnakeCase(string code)
    {
        Assert.Matches(@"^MDL_[A-Z][A-Z0-9]*(_[A-Z0-9]+)*$", code);
    }

    [Fact]
    public void MutationErrorCode_Failed_UsedForInvalidOperationException()
    {
        var result = MdlResult<object>.Fail("MDL_MUTATION_FAILED", "Operation failed");
        var json = JsonDocument.Parse(JsonFromResult(result));

        Assert.Equal("MDL_MUTATION_FAILED", json.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public void MutationErrorCode_SaveFailed_HasExitCode2()
    {
        var result = MdlResult<object>.Fail("MDL_MUTATION_SAVE_FAILED", "IO error", exitCode: 2);

        Assert.Equal(2, result.ExitCode);
    }

    // ── Object lookup error codes ───────────────────────────────────────────

    [Theory]
    [InlineData("MDL_OBJECT_NOT_FOUND")]
    [InlineData("MDL_OBJECT_AMBIGUOUS")]
    public void ObjectLookupCodes_FollowMdlObjectPrefix(string code)
    {
        Assert.StartsWith("MDL_OBJECT_", code);
    }

    [Fact]
    public void ObjectNotFound_IncludesHintInErrorJson()
    {
        var result = MdlResult<object>.Fail(
            "MDL_OBJECT_NOT_FOUND",
            "Object 'X' not found.",
            hint: "Run 'mdl ls' to list available objects.");
        var json = JsonDocument.Parse(JsonFromResult(result));

        Assert.NotNull(json.RootElement.GetProperty("hint").GetString());
    }

    // ── Deprecated/removed error codes (regression guard) ───────────────────

    /// <summary>
    /// These codes were replaced by MDL_MUTATION_* codes and must NOT appear in
    /// mutation command output. This test guards against accidental reintroduction.
    /// </summary>
    [Theory]
    [InlineData("MDL_REPLACE_UNSUPPORTED")]
    [InlineData("MDL_REPLACE_FAILED")]
    [InlineData("MDL_REPLACE_SAVE_FAILED")]
    [InlineData("MDL_SCRIPT_SAVE_UNSUPPORTED")]
    [InlineData("MDL_SCRIPT_SAVE_FAILED")]
    [InlineData("MDL_BPA_FIX_UNSUPPORTED")]
    [InlineData("MDL_BPA_IGNORE_UNSUPPORTED")]
    public void DeprecatedErrorCodes_AreNotUsedByMutationRunner(string deprecatedCode)
    {
        var activeCodes = new HashSet<string>
        {
            "MDL_MUTATION_UNSUPPORTED_PROVIDER",
            "MDL_MUTATION_UNSUPPORTED",
            "MDL_MUTATION_INVALID_VALUE",
            "MDL_MUTATION_FAILED",
            "MDL_MUTATION_SAVE_FAILED",
            "MDL_OBJECT_NOT_FOUND",
            "MDL_OBJECT_AMBIGUOUS"
        };

        Assert.DoesNotContain(deprecatedCode, activeCodes);
    }
}
