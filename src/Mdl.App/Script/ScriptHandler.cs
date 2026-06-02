using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Mdl.App.Diagnostics;
using Mdl.Core.Authentication;
using Mdl.Core.Models;
using Mdl.Core.Results;

namespace Mdl.App.Script;

public sealed class ScriptHandler
{
    private readonly IReadOnlyList<IModelProvider> _providers;

    public ScriptHandler(IEnumerable<IModelProvider> providers)
        => _providers = providers.ToList();

    public async Task<MdlResult<ScriptRunResult>> HandleAsync(
        ScriptRunRequest request,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ScriptInput> inputs;
        try
        {
            inputs = ResolveInputs(request);
        }
        catch (FileNotFoundException ex)
        {
            return MdlResult<ScriptRunResult>.Fail("MDL_SCRIPT_FILE_NOT_FOUND", ex.Message, exitCode: 1);
        }

        if (inputs.Count == 0)
            return MdlResult<ScriptRunResult>.Fail(
                "MDL_SCRIPT_REQUIRED",
                "No scripts specified.\nHint: Pass --script <file>, -e <inline-expression>, or pipe code with -e -. Run 'mdl script --help' for examples.",
                exitCode: 1);

        var provider = _providers.FirstOrDefault(p => p.CanOpen(request.Model));
        if (provider is null)
            return MdlResult<ScriptRunResult>.Fail(
                "MDL_NO_PROVIDER",
                $"No provider can open model: {request.Model.Value}",
                exitCode: 1);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            await using var session = await provider.OpenAsync(request.Model, cancellationToken);
            var summary = await session.GetSummaryAsync(cancellationToken);
            var snapshot = await session.GetSnapshotAsync(cancellationToken);

            if (request.DryRun)
                return MdlResult<ScriptRunResult>.Ok(DryRun(summary.Name, inputs));

            var messages = new List<ScriptMessage>();
            for (var i = 0; i < inputs.Count; i++)
            {
                var input = inputs[i];
                if (!ScriptExpressionEvaluator.TryEvaluate(snapshot, summary, input.Code, out var result, out var compileError, out var runtimeError))
                {
                    stopwatch.Stop();
                    return MdlResult<ScriptRunResult>.Ok(
                        ScriptRunResult.Failed(
                            summary.Name,
                            (int)stopwatch.ElapsedMilliseconds,
                            input.Source,
                            i + 1,
                            compileError is null ? [] : [compileError],
                            runtimeError,
                            messages),
                        exitCode: 1);
                }

                if (result is not null)
                    messages.Add(result);
            }

            var saved = await SaveIfRequestedAsync(request, session, cancellationToken);
            stopwatch.Stop();

            return MdlResult<ScriptRunResult>.Ok(
                ScriptRunResult.Executed(
                    summary.Name,
                    (int)stopwatch.ElapsedMilliseconds,
                    inputs,
                    messages,
                    saved));
        }
        catch (AuthenticationRequiredException ex)
        {
            return MdlResult<ScriptRunResult>.Fail("MDL_AUTH_REQUIRED", ex.Message, exitCode: 1);
        }
        catch (Exception ex) when (request.Model.IsRemote && ex is not OperationCanceledException)
        {
            return MdlResult<ScriptRunResult>.Fail(
                "MDL_CONNECT_FAILED",
                RemoteConnectError.Describe(request.Model.Value, ex),
                exitCode: 1);
        }
        catch (NotSupportedException ex)
        {
            return MdlResult<ScriptRunResult>.Fail("MDL_SCRIPT_SAVE_UNSUPPORTED", ex.Message, exitCode: 1);
        }
        catch (IOException ex)
        {
            return MdlResult<ScriptRunResult>.Fail("MDL_SCRIPT_SAVE_FAILED", ex.Message, exitCode: 2);
        }
    }

    private static ScriptRunResult DryRun(string modelName, IReadOnlyList<ScriptInput> inputs)
    {
        var scripts = inputs.Select(input =>
        {
            var ok = ScriptExpressionEvaluator.CanCompile(input.Code, out var error);
            return new ScriptDryRunResult(input.Source, ok, ok ? [] : [error!]);
        }).ToList();

        return ScriptRunResult.CreateDryRun(modelName, scripts);
    }

    private static IReadOnlyList<ScriptInput> ResolveInputs(ScriptRunRequest request)
    {
        var inputs = new List<ScriptInput>();

        foreach (var file in request.ScriptFiles.Where(file => !string.IsNullOrWhiteSpace(file)))
        {
            if (!File.Exists(file))
                throw new FileNotFoundException($"Script file not found: {file}", file);

            inputs.Add(new ScriptInput(file, File.ReadAllText(file)));
        }

        inputs.AddRange(request.Expressions
            .Where(expression => !string.IsNullOrWhiteSpace(expression))
            .Select(expression => new ScriptInput("<inline>", expression)));

        return inputs;
    }

    private static async Task<object> SaveIfRequestedAsync(
        ScriptRunRequest request,
        IModelSession session,
        CancellationToken cancellationToken)
    {
        if (!request.Save && string.IsNullOrWhiteSpace(request.SaveTo))
            return false;

        var serialization = string.IsNullOrWhiteSpace(request.Serialization)
            ? InferSerialization(request.Model.Value)
            : request.Serialization;

        if (session is IModelMutationSession mutator)
        {
            var export = await mutator.SaveAsync(
                request.SaveTo,
                serialization,
                request.Force,
                cancellationToken);
            return export.SavedPath;
        }

        if (session is IModelExportSession exporter)
        {
            var outputPath = string.IsNullOrWhiteSpace(request.SaveTo)
                ? request.Model.Value
                : request.SaveTo;
            var export = await exporter.ExportAsync(
                new ModelExportRequest(outputPath, serialization, request.Force, SupportingFiles: false),
                cancellationToken);
            return export.SavedPath;
        }

        throw new NotSupportedException($"Provider cannot save model: {request.Model.Value}");
    }

    private static string InferSerialization(string modelPath)
    {
        var extension = Path.GetExtension(modelPath);
        return extension.Equals(".bim", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".tmsl", StringComparison.OrdinalIgnoreCase)
            ? "bim"
            : "tmdl";
    }
}

public sealed record ScriptRunRequest(
    ModelReference Model,
    IReadOnlyList<string> ScriptFiles,
    IReadOnlyList<string> Expressions,
    bool DryRun,
    bool Force,
    bool Save,
    string? SaveTo,
    string? Serialization);

public sealed record ScriptRunResult(
    string ModelName,
    bool DryRun,
    bool Success,
    int DurationMs,
    int ScriptsExecuted,
    IReadOnlyList<ScriptInput> Inputs,
    IReadOnlyList<ScriptMessage> Messages,
    object Saved,
    bool? Staged,
    IReadOnlyList<ScriptDryRunResult> Scripts,
    string? FailedScript,
    int? ScriptIndex,
    IReadOnlyList<string> CompileErrors,
    string? RuntimeError)
{
    public static ScriptRunResult CreateDryRun(
        string modelName,
        IReadOnlyList<ScriptDryRunResult> scripts)
        => new(
            modelName,
            DryRun: true,
            Success: scripts.All(script => script.Success),
            DurationMs: 0,
            ScriptsExecuted: 0,
            Inputs: [],
            Messages: [],
            Saved: false,
            Staged: false,
            Scripts: scripts,
            FailedScript: null,
            ScriptIndex: null,
            CompileErrors: [],
            RuntimeError: null);

    public static ScriptRunResult Executed(
        string modelName,
        int durationMs,
        IReadOnlyList<ScriptInput> inputs,
        IReadOnlyList<ScriptMessage> messages,
        object saved)
        => new(
            modelName,
            DryRun: false,
            Success: true,
            DurationMs: durationMs,
            ScriptsExecuted: inputs.Count,
            Inputs: inputs,
            Messages: messages,
            Saved: saved,
            Staged: saved is bool b && b == false ? false : null,
            Scripts: [],
            FailedScript: null,
            ScriptIndex: null,
            CompileErrors: [],
            RuntimeError: null);

    public static ScriptRunResult Failed(
        string modelName,
        int durationMs,
        string failedScript,
        int scriptIndex,
        IReadOnlyList<string> compileErrors,
        string? runtimeError,
        IReadOnlyList<ScriptMessage> messages)
        => new(
            modelName,
            DryRun: false,
            Success: false,
            DurationMs: durationMs,
            ScriptsExecuted: scriptIndex - 1,
            Inputs: [],
            Messages: messages,
            Saved: false,
            Staged: false,
            Scripts: [],
            FailedScript: failedScript,
            ScriptIndex: scriptIndex,
            CompileErrors: compileErrors,
            RuntimeError: runtimeError);
}

public sealed record ScriptInput(string Source, string Code);

public sealed record ScriptMessage(string Level, string Text);

public sealed record ScriptDryRunResult(
    string Source,
    bool Success,
    IReadOnlyList<string> Errors);

internal static partial class ScriptExpressionEvaluator
{
    public static bool CanCompile(string code, out string? error)
    {
        if (TryNormalizeExpression(code, out _, out error))
            return true;

        return false;
    }

    public static bool TryEvaluate(
        ModelSnapshot snapshot,
        ModelSummary summary,
        string code,
        out ScriptMessage? result,
        out string? compileError,
        out string? runtimeError)
    {
        result = null;
        runtimeError = null;

        if (!TryNormalizeExpression(code, out var expression, out compileError))
            return false;

        try
        {
            var value = EvaluateExpression(snapshot, summary, expression);
            if (value is not null)
                result = new ScriptMessage("output", FormatValue(value));
            return true;
        }
        catch (ScriptRuntimeException ex)
        {
            runtimeError = ex.Message;
            compileError = null;
            return false;
        }
    }

    private static bool TryNormalizeExpression(string code, out string expression, out string? error)
    {
        expression = code.Trim();
        error = null;

        if (expression.StartsWith("return ", StringComparison.Ordinal))
            expression = expression["return ".Length..].Trim();

        if (expression.EndsWith(";", StringComparison.Ordinal))
            expression = expression[..^1].Trim();

        if (InfoCall().Match(expression) is { Success: true } info)
            expression = info.Groups["expression"].Value.Trim();

        if (expression.EndsWith(".ToString()", StringComparison.Ordinal))
            expression = expression[..^".ToString()".Length].Trim();

        if (expression.Length == 0)
        {
            error = "error: Script expression is empty.";
            return false;
        }

        if (ExpressionLooksSupported(expression))
            return true;

        error = $"error: Unsupported script expression: {code.Trim()}";
        return false;
    }

    private static bool ExpressionLooksSupported(string expression)
        => expression is "Model.Name"
           || expression is "Model.Tables.Count"
           || expression is "Model.Relationships.Count"
           || expression is "Model.Roles.Count"
           || IndexedObjectProperty().IsMatch(expression)
           || SimpleArithmetic().IsMatch(expression);

    private static object EvaluateExpression(
        ModelSnapshot snapshot,
        ModelSummary summary,
        string expression)
    {
        if (expression == "Model.Name")
            return summary.Name == "(unnamed)" ? "Model" : summary.Name;

        if (expression == "Model.Tables.Count")
            return summary.Tables;

        if (expression == "Model.Relationships.Count")
            return summary.Relationships;

        if (expression == "Model.Roles.Count")
            return summary.Roles;

        var indexed = IndexedObjectProperty().Match(expression);
        if (indexed.Success)
            return EvaluateIndexedProperty(snapshot, indexed);

        var arithmetic = SimpleArithmetic().Match(expression);
        if (arithmetic.Success)
            return EvaluateArithmetic(arithmetic);

        throw new ScriptRuntimeException($"Unsupported script expression: {expression}");
    }

    private static object EvaluateIndexedProperty(ModelSnapshot snapshot, Match match)
    {
        var table = GetByIndex(Tables(snapshot), int.Parse(match.Groups["table"].Value, CultureInfo.InvariantCulture));
        var childKind = match.Groups["childKind"].Value;
        var childIndex = match.Groups["childIndex"].Value;
        var property = match.Groups["property"].Value;

        if (string.IsNullOrWhiteSpace(childKind))
            return property switch
            {
                "Name" => table.Name,
                "Columns.Count" => CountChildren(table, ModelObjectKind.Column),
                "Measures.Count" => CountChildren(table, ModelObjectKind.Measure),
                "Partitions.Count" => CountChildren(table, ModelObjectKind.Partition),
                _ => throw new ScriptRuntimeException($"Unsupported table property: {property}")
            };

        var kind = childKind switch
        {
            "Columns" => ModelObjectKind.Column,
            "Measures" => ModelObjectKind.Measure,
            "Partitions" => ModelObjectKind.Partition,
            _ => throw new ScriptRuntimeException($"Unsupported table collection: {childKind}")
        };

        var child = GetByIndex(
            table.Children.Where(child => child.Kind == kind).ToList(),
            int.Parse(childIndex, CultureInfo.InvariantCulture));

        return property switch
        {
            "Name" => child.Name,
            _ => throw new ScriptRuntimeException($"Unsupported child property: {property}")
        };
    }

    private static object EvaluateArithmetic(Match match)
    {
        var left = decimal.Parse(match.Groups["left"].Value, CultureInfo.InvariantCulture);
        var right = decimal.Parse(match.Groups["right"].Value, CultureInfo.InvariantCulture);
        var value = match.Groups["operator"].Value switch
        {
            "+" => left + right,
            "-" => left - right,
            "*" => left * right,
            "/" when right != 0 => left / right,
            "/" => throw new ScriptRuntimeException("Attempted to divide by zero."),
            _ => throw new ScriptRuntimeException("Unsupported arithmetic operator.")
        };

        return decimal.Truncate(value) == value ? decimal.ToInt64(value) : value;
    }

    private static IReadOnlyList<ModelObject> Tables(ModelSnapshot snapshot)
        => snapshot.Objects.Where(obj => obj.Kind == ModelObjectKind.Table).ToList();

    private static int CountChildren(ModelObject table, ModelObjectKind kind)
        => table.Children.Count(child => child.Kind == kind);

    private static ModelObject GetByIndex(IReadOnlyList<ModelObject> objects, int index)
    {
        if (index < 0 || index >= objects.Count)
            throw new ScriptRuntimeException($" (Parameter 'index')\r\nActual value was {index}.");

        return objects[index];
    }

    private static string FormatValue(object value)
        => value switch
        {
            bool b => b ? "True" : "False",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? "",
            _ => value.ToString() ?? ""
        };

    [GeneratedRegex(@"^Info\((?<expression>.*)\)$")]
    private static partial Regex InfoCall();

    [GeneratedRegex(@"^Model\.Tables\[(?<table>\d+)\](?:\.(?<childKind>Columns|Measures|Partitions)\[(?<childIndex>\d+)\])?\.(?<property>Name|Columns\.Count|Measures\.Count|Partitions\.Count)$")]
    private static partial Regex IndexedObjectProperty();

    [GeneratedRegex(@"^(?<left>-?\d+(?:\.\d+)?)\s*(?<operator>[+\-*/])\s*(?<right>-?\d+(?:\.\d+)?)$")]
    private static partial Regex SimpleArithmetic();
}

internal sealed class ScriptRuntimeException(string message) : Exception(message);
