using TechStrap.Contracts.Agents;

namespace TechStrap.Admin.Features.Account;

/// <summary>The public display name as typed. The same two rules as the API (60 characters at most, no @), checked before anything is sent; blank clears the name.</summary>
internal sealed class MyProfileViewModel
{
    public string PublicDisplayName { get; set; } = string.Empty;

    /// <summary>What would be saved: the trimmed text, or null to clear.</summary>
    public string? Normalized => string.IsNullOrWhiteSpace(PublicDisplayName) ? null : PublicDisplayName.Trim();

    public string? Check() => Normalized switch
    {
        null => null,
        { Length: > MySettingsCopy.PublicNameMaxLength } => MySettingsCopy.NameTooLong,
        var name when name.Contains('@') => MySettingsCopy.NameInvalid,
        _ => null,
    };

    public UpdateMyProfileRequest ToRequest() => new(Normalized);
}
