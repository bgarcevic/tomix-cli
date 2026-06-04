using Mdl.App.ModelObjects;
using Mdl.Core.Models;
using Mdl.Core.Results;

namespace Mdl.App.Format;

public sealed class FormatModelHandler
{
    private readonly IReadOnlyList<IModelProvider> _providers;
    private readonly IExpressionFormatterClient _formatter;

    public FormatModelHandler(
        IEnumerable<IModelProvider> providers,
        IExpressionFormatterClient formatter)
    {
        _providers = providers.ToList();
        _formatter = formatter;
    }

    public async Task<MdlResult<FormatModelResult>> HandleAsync(
        FormatModelRequest request,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.Expression))
            return await FormatInlineAsync(request, cancellationToken);

        var provider = _providers.FirstOrDefault(p => p.CanOpen(request.Model));
        if (provider is null)
            return MdlResult<FormatModelResult>.Fail(
                "MDL_NO_PROVIDER",
                $"No provider can open model: {request.Model.Value}",
                exitCode: 2,
                hint: "Supported formats: TMDL folder, .bim file. For remote models, use --server and --database.");

        await using var session = await provider.OpenAsync(request.Model, cancellationToken);
        var snapshot = await session.GetSnapshotAsync(cancellationToken);

        if (request.Save || !string.IsNullOrWhiteSpace(request.SaveTo))
        {
            if (session is not IModelMutationSession)
            {
                return MdlResult<FormatModelResult>.Fail(
                    "MDL_MUTATION_UNSUPPORTED_PROVIDER",
                    $"Provider cannot save formatted expressions: {request.Model.Value}");
            }
        }

        return !string.IsNullOrWhiteSpace(request.Path)
            ? await FormatObjectAsync(request, snapshot, session, cancellationToken)
            : await FormatModelAsync(request, snapshot, session, cancellationToken);
    }

    private async Task<MdlResult<FormatModelResult>> FormatInlineAsync(
        FormatModelRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryResolveLanguage(request.Language, null, out var language, out var error))
            return MdlResult<FormatModelResult>.Fail("MDL_FORMAT_UNSUPPORTED_LANGUAGE", error, exitCode: 2);

        var formatted = await _formatter.FormatAsync(
            new ExpressionFormatRequest(
                request.Expression!,
                language,
                request.Long,
                request.Semicolons,
                request.NoSpaceAfterFunction),
            cancellationToken);

        return MdlResult<FormatModelResult>.Ok(
            new InlineFormatResult(formatted.Success, formatted.Formatted, formatted.Errors),
            exitCode: 0);
    }

    private async Task<MdlResult<FormatModelResult>> FormatObjectAsync(
        FormatModelRequest request,
        ModelSnapshot snapshot,
        IModelSession session,
        CancellationToken cancellationToken)
    {
        var matches = ModelObjectLookup.Find(snapshot, request.Path!, request.Type).ToList();
        if (matches.Count == 0)
            return MdlResult<FormatModelResult>.Fail(
                "MDL_OBJECT_NOT_FOUND",
                $"Object not found: {request.Path}",
                exitCode: 1,
                hint: "Run 'mdl ls' to list available objects, or 'mdl ls Sa*' to filter.");

        if (matches.Count > 1)
            return MdlResult<FormatModelResult>.Fail(
                "MDL_OBJECT_AMBIGUOUS",
                $"Object path matched more than one object: {request.Path}",
                exitCode: 1);

        var obj = matches[0];
        if (string.IsNullOrWhiteSpace(obj.Expression))
            return MdlResult<FormatModelResult>.Fail(
                "MDL_FORMAT_NO_EXPRESSION",
                $"Object has no expression to format: {obj.Path}",
                exitCode: 1);

        if (!TryResolveLanguage(request.Language, obj.Kind, out var language, out var error))
            return MdlResult<FormatModelResult>.Fail("MDL_FORMAT_UNSUPPORTED_LANGUAGE", error, exitCode: 2);

        var formatted = await FormatExpressionAsync(request, obj.Expression!, language, cancellationToken);
        if (formatted.Success)
            await ApplySaveAsync(request, session, [(obj, formatted.Formatted)], cancellationToken);

        return MdlResult<FormatModelResult>.Ok(new ObjectFormatResult(
            formatted.Success,
            obj.Path,
            FormatterLanguages.DisplayName(language),
            formatted.Success ? Status(obj.Expression!, formatted.Formatted) : "failed",
            formatted.Formatted,
            Saved: null));
    }

    private async Task<MdlResult<FormatModelResult>> FormatModelAsync(
        FormatModelRequest request,
        ModelSnapshot snapshot,
        IModelSession session,
        CancellationToken cancellationToken)
    {
        if (!TryResolveLanguage(request.Language, request.Type, out var language, out var error))
            return MdlResult<FormatModelResult>.Fail("MDL_FORMAT_UNSUPPORTED_LANGUAGE", error, exitCode: 2);

        var objects = FormatTargets(snapshot, language, request.Type).ToList();
        var results = new List<ModelFormatObjectResult>();
        var successful = new List<(ModelObject Object, string Formatted)>();
        var formattedCount = 0;
        var unchangedCount = 0;
        var failedCount = 0;

        foreach (var obj in objects)
        {
            var response = await FormatExpressionAsync(request, obj.Expression!, language, cancellationToken);
            if (!response.Success)
            {
                failedCount++;
                results.Add(ToModelResult(obj, "failed"));
                continue;
            }

            var status = Status(obj.Expression!, response.Formatted);
            if (status == "formatted")
                formattedCount++;
            else
                unchangedCount++;

            successful.Add((obj, response.Formatted));
            results.Add(ToModelResult(obj, status));
        }

        if (failedCount == 0)
            await ApplySaveAsync(request, session, successful, cancellationToken);

        return MdlResult<FormatModelResult>.Ok(new ModelFormatResult(
            objects.Count,
            formattedCount,
            unchangedCount,
            failedCount,
            results,
            Saved: null));
    }

    private Task<ExpressionFormatResponse> FormatExpressionAsync(
        FormatModelRequest request,
        string expression,
        string language,
        CancellationToken cancellationToken)
        => _formatter.FormatAsync(
            new ExpressionFormatRequest(
                expression,
                language,
                request.Long,
                request.Semicolons,
                request.NoSpaceAfterFunction),
            cancellationToken);

    private static async Task ApplySaveAsync(
        FormatModelRequest request,
        IModelSession session,
        IReadOnlyList<(ModelObject Object, string Formatted)> formatted,
        CancellationToken cancellationToken)
    {
        if (!request.Save && string.IsNullOrWhiteSpace(request.SaveTo))
            return;

        var mutator = (IModelMutationSession)session;
        foreach (var (obj, value) in formatted)
        {
            mutator.SetProperty(new ModelObjectSetRequest(
                obj.Path,
                [new ModelPropertyAssignment("expression", value)],
                obj.Kind));
        }

        await mutator.SaveAsync(request.SaveTo, "", force: true, cancellationToken);
    }

    private static IEnumerable<ModelObject> FormatTargets(
        ModelSnapshot snapshot,
        string language,
        ModelObjectKind? type)
    {
        var objects = ModelObjectProjection
            .Flatten(snapshot)
            .Where(o => !string.IsNullOrWhiteSpace(o.Expression));

        if (type is not null)
            objects = objects.Where(o => o.Kind == type.Value);
        else
            objects = language == FormatterLanguages.PowerQuery
                ? objects.Where(o => o.Kind == ModelObjectKind.Partition)
                : objects.Where(o => o.Kind == ModelObjectKind.Measure);

        return objects;
    }

    private static bool TryResolveLanguage(
        string? requested,
        ModelObjectKind? type,
        out string language,
        out string error)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            var valid = FormatterLanguages.TryNormalize(requested, out language);
            error = valid ? "" : $"Unsupported formatter language: {requested}";
            return valid;
        }

        language = type == ModelObjectKind.Partition
            ? FormatterLanguages.PowerQuery
            : FormatterLanguages.Dax;
        error = "";
        return true;
    }

    private static string Status(string before, string after)
        => string.Equals(before, after, StringComparison.Ordinal) ? "unchanged" : "formatted";

    private static ModelFormatObjectResult ToModelResult(ModelObject obj, string status)
    {
        var table = obj.Path.Split('/')[0];
        return obj.Kind == ModelObjectKind.Partition
            ? new ModelFormatObjectResult(
                Measure: null,
                Table: table,
                Status: status,
                Partition: obj.Name)
            : new ModelFormatObjectResult(
                Measure: obj.Name,
                Table: table,
                Status: status,
                Partition: null);
    }
}
