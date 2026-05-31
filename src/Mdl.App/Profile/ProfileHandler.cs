using Mdl.App.State;
using Mdl.Core.Results;

namespace Mdl.App.Profile;

public sealed class ProfileHandler
{
    private readonly CliStateStore _store;

    public ProfileHandler()
        : this(new CliStateStore())
    {
    }

    public ProfileHandler(CliStateStore store) => _store = store;

    public MdlResult<ProfileListResult> List()
    {
        var profiles = _store.LoadProfiles()
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);

        return MdlResult<ProfileListResult>.Ok(new ProfileListResult(profiles));
    }

    public MdlResult<ProfileShowResult> Show(string name)
    {
        var profiles = _store.LoadProfiles();
        return profiles.TryGetValue(name, out var profile)
            ? MdlResult<ProfileShowResult>.Ok(new ProfileShowResult(profile))
            : MdlResult<ProfileShowResult>.Fail("MDL_PROFILE_NOT_FOUND", $"Profile not found: {name}", exitCode: 1);
    }

    public MdlResult<ProfileSetResult> Set(ProfileSetRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return MdlResult<ProfileSetResult>.Fail("MDL_PROFILE_NAME_REQUIRED", "Profile name is required.", exitCode: 2);

        var profiles = _store.LoadProfiles();
        var profile = new CliProfile(
            request.Name,
            request.Server,
            request.Database,
            request.Model,
            request.Auth,
            request.Description,
            request.AutoFormat,
            request.ValidateOnMutation,
            request.BpaOnMutation,
            request.BpaOnDeploy,
            request.VertipaqOnRefresh,
            request.Spinner);

        profiles[request.Name] = profile;
        _store.SaveProfiles(profiles);
        return MdlResult<ProfileSetResult>.Ok(new ProfileSetResult(profile));
    }

    public MdlResult<ProfileRemoveResult> Remove(string name)
    {
        var profiles = _store.LoadProfiles();
        var removed = profiles.Remove(name);
        if (removed)
            _store.SaveProfiles(profiles);

        return MdlResult<ProfileRemoveResult>.Ok(new ProfileRemoveResult(name, removed));
    }
}

public sealed record ProfileSetRequest(
    string Name,
    string? Server,
    string? Database,
    string? Model,
    string? Auth,
    string? Description,
    bool? AutoFormat,
    bool? ValidateOnMutation,
    bool? BpaOnMutation,
    bool? BpaOnDeploy,
    bool? VertipaqOnRefresh,
    bool? Spinner);
