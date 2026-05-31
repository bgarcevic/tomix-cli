using Mdl.Core.Models;
using Mdl.Core.Results;

namespace Mdl.App.Replace;

public sealed class ReplaceModelTextHandler
{
    private readonly IReadOnlyList<IModelProvider> _providers;

    public ReplaceModelTextHandler(IEnumerable<IModelProvider> providers)
        => _providers = providers.ToList();

    public async Task<MdlResult<ReplaceModelTextResult>> HandleAsync(
        ReplaceModelTextRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.Pattern))
            return MdlResult<ReplaceModelTextResult>.Fail(
                "MDL_REPLACE_PATTERN_REQUIRED",
                "A pattern is required.",
                exitCode: 2);

        var provider = _providers.FirstOrDefault(p => p.CanOpen(request.Model));
        if (provider is null)
            return MdlResult<ReplaceModelTextResult>.Fail(
                "MDL_NO_PROVIDER",
                $"No provider can open model: {request.Model.Value}",
                exitCode: 2);

        var shouldSave = (request.Save || !string.IsNullOrWhiteSpace(request.SaveTo)) && !request.DryRun;

        await using var session = await provider.OpenAsync(request.Model, cancellationToken);
        if (session is not IModelMutationSession mutator)
            return MdlResult<ReplaceModelTextResult>.Fail(
                "MDL_MUTATION_UNSUPPORTED_PROVIDER",
                $"Provider cannot mutate model: {request.Model.Value}");

        try
        {
            var replace = mutator.ReplaceText(new ModelReplaceRequest(
                request.Pattern,
                request.Replacement,
                request.Scope,
                request.Regex,
                request.CaseSensitive,
                Apply: shouldSave));

            if (!shouldSave)
            {
                return MdlResult<ReplaceModelTextResult>.Ok(new ReplaceModelTextResult(
                    request.Pattern,
                    request.Replacement,
                    DryRun: true,
                    replace.ChangeCount,
                    replace.Previews,
                    Saved: null));
            }

            object saved = false;
            if (replace.ChangeCount > 0)
            {
                var export = await mutator.SaveAsync(
                    request.SaveTo,
                    request.Serialization,
                    request.Force,
                    cancellationToken);
                saved = export.SavedPath;
            }

            return MdlResult<ReplaceModelTextResult>.Ok(new ReplaceModelTextResult(
                request.Pattern,
                request.Replacement,
                DryRun: null,
                replace.ChangeCount,
                Previews: null,
                saved));
        }
        catch (ArgumentException ex)
        {
            return MdlResult<ReplaceModelTextResult>.Fail("MDL_REPLACE_INVALID_ARGUMENT", ex.Message, exitCode: 2);
        }
        catch (NotSupportedException ex)
        {
            return MdlResult<ReplaceModelTextResult>.Fail("MDL_REPLACE_UNSUPPORTED", ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return MdlResult<ReplaceModelTextResult>.Fail("MDL_REPLACE_FAILED", ex.Message);
        }
        catch (IOException ex)
        {
            return MdlResult<ReplaceModelTextResult>.Fail("MDL_REPLACE_SAVE_FAILED", ex.Message, exitCode: 2);
        }
    }
}
