using Mdl.Core.Models;
using Mdl.Core.Results;

namespace Mdl.App.Save;

public sealed class SaveModelHandler
{
    private readonly IReadOnlyList<IModelProvider> _providers;

    public SaveModelHandler(IEnumerable<IModelProvider> providers)
        => _providers = providers.ToList();

    public async Task<MdlResult<SaveModelResult>> HandleAsync(
        SaveModelRequest request,
        CancellationToken cancellationToken)
    {
        var provider = _providers.FirstOrDefault(p => p.CanOpen(request.Model));
        if (provider is null)
            return MdlResult<SaveModelResult>.Fail(
                code: "MDL_NO_PROVIDER",
                message: $"No provider can open model: {request.Model.Value}",
                exitCode: 2);

        var outputPath = request.OutputPath;
        if (string.IsNullOrWhiteSpace(outputPath))
            outputPath = request.Model.Value;

        if (string.IsNullOrWhiteSpace(outputPath))
            return MdlResult<SaveModelResult>.Fail(
                code: "MDL_SAVE_OUTPUT_REQUIRED",
                message: "An output path is required when no model source is active.",
                exitCode: 2);

        var serialization = string.IsNullOrWhiteSpace(request.Serialization)
            ? InferSerialization(request.Model.Value)
            : request.Serialization;

        await using var session = await provider.OpenAsync(request.Model, cancellationToken);
        if (session is not IModelExportSession exporter)
            return MdlResult<SaveModelResult>.Fail(
                code: "MDL_SAVE_UNSUPPORTED_PROVIDER",
                message: $"Provider cannot save model: {request.Model.Value}",
                exitCode: 1);

        try
        {
            var export = await exporter.ExportAsync(
                new ModelExportRequest(outputPath, serialization, request.Force, request.SupportingFiles),
                cancellationToken);

            return MdlResult<SaveModelResult>.Ok(new SaveModelResult(export.SavedPath, export.Format));
        }
        catch (NotSupportedException ex)
        {
            return MdlResult<SaveModelResult>.Fail("MDL_SAVE_UNSUPPORTED_SERIALIZATION", ex.Message, exitCode: 2);
        }
        catch (IOException ex)
        {
            return MdlResult<SaveModelResult>.Fail("MDL_SAVE_OUTPUT_EXISTS", ex.Message, exitCode: 2);
        }
    }

    private static string InferSerialization(string modelPath)
    {
        var extension = Path.GetExtension(modelPath);
        if (extension.Equals(".bim", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".tmsl", StringComparison.OrdinalIgnoreCase))
            return "bim";

        return "tmdl";
    }
}
