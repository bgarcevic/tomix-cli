using Mdl.App.Mutations;
using Mdl.App.State;
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

        // --dry-run is a preview: never persist or stage.
        var options = new MutationOptions(
            request.Save && !request.DryRun,
            request.DryRun ? null : request.SaveTo,
            request.Stage && !request.DryRun,
            request.Revert,
            request.Serialization,
            request.Force);
        var stagingStore = new StagingStore();
        var connection = new CliStateStore().LoadCurrentSession();

        var begin = await MutationLifecycle.BeginAsync(
            _providers, request.Model, options, stagingStore, connection, cancellationToken);
        if (begin.Error is { } error)
            return MdlResult<ReplaceModelTextResult>.Fail(error.Code, error.Message, error.ExitCode);

        if (begin.Mode == MutationMode.Revert)
        {
            stagingStore.Discard(request.Model);
            return MdlResult<ReplaceModelTextResult>.Ok(new ReplaceModelTextResult(
                request.Pattern, request.Replacement, DryRun: null, ChangeCount: 0, Previews: null, Saved: false));
        }

        var context = begin.Context!;
        var persist = context.Mode is MutationMode.Save or MutationMode.Stage;

        var provider = _providers.FirstOrDefault(p => p.CanOpen(context.EffectiveModel));
        if (provider is null)
            return MdlResult<ReplaceModelTextResult>.Fail(
                "MDL_NO_PROVIDER",
                $"No provider can open model: {context.EffectiveModel.Value}",
                exitCode: 2,
                hint: "Supported formats: TMDL folder, .bim file. For remote models, use --server and --database.");

        await using var session = await provider.OpenAsync(context.EffectiveModel, cancellationToken);
        if (session is not IModelMutationSession mutator)
            return MdlResult<ReplaceModelTextResult>.Fail(
                "MDL_MUTATION_UNSUPPORTED_PROVIDER",
                $"Provider cannot mutate model: {context.EffectiveModel.Value}");

        try
        {
            var replace = mutator.ReplaceText(new ModelReplaceRequest(
                request.Pattern,
                request.Replacement,
                request.Scope,
                request.Regex,
                request.CaseSensitive,
                Apply: persist));

            if (!persist)
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
            bool? staged = null;
            if (replace.ChangeCount > 0)
            {
                var outcome = await MutationLifecycle.CompleteAsync(
                    mutator, context, "replace", $"replace {request.Pattern}", cancellationToken);
                saved = outcome.Saved;
                staged = outcome.Staged;
            }

            return MdlResult<ReplaceModelTextResult>.Ok(new ReplaceModelTextResult(
                request.Pattern,
                request.Replacement,
                DryRun: null,
                replace.ChangeCount,
                Previews: null,
                saved,
                staged));
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
