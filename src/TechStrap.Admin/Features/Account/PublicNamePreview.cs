using TechStrap.Contracts.Agents;

namespace TechStrap.Admin.Features.Account;

/// <summary>
/// The line under the public display name: what a customer will read as the agent's name. It uses the Contracts format constants, so the preview and the emails cannot drift apart.
/// The name is what was typed, or the first word of the agent's profile name (never one that is an email address), or, with neither, the product's plain support name.
/// Without an active product the product is shown as a placeholder.
/// </summary>
public static class PublicNamePreview
{
    public static string Build(string? typed, string? profileName, string? productDisplayName)
    {
        var product = string.IsNullOrWhiteSpace(productDisplayName) ? MySettingsCopy.GenericProduct : productDisplayName.Trim();
        var given = !string.IsNullOrWhiteSpace(typed) ? typed.Trim() : FirstWord(profileName);
        return given is null
            ? string.Format(System.Globalization.CultureInfo.InvariantCulture, AgentPublicName.FallbackFormat, product)
            : string.Format(System.Globalization.CultureInfo.InvariantCulture, AgentPublicName.Format, given, product);
    }

    /// <summary>The first word of the profile name; null when there is none or it contains an @ (an identity provider may send an email address as the name).</summary>
    public static string? FirstWord(string? profileName)
    {
        var word = profileName?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        return word is not null && word.Contains('@') ? null : word;
    }
}
