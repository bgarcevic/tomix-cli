using Mdl.Core.Models;

namespace Mdl.App.Validate;

public sealed record ValidateModelRequest(
    ModelReference Model,
    bool ErrorsOnly,
    bool NoWarnings,
    bool NoAntipatterns,
    bool ServerOnly);
