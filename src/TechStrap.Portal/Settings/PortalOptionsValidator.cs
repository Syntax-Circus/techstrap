using Microsoft.Extensions.Options;
using TechStrap.Portal.Routing;

namespace TechStrap.Portal.Settings;

/// <summary>Fails the start when the Portal's settings are missing or malformed. Each message names the setting as the operator sets it, so the container's log says what to fix.</summary>
internal sealed class PortalOptionsValidator(IHostEnvironment environment) : IValidateOptions<PortalOptions>
{
    public ValidateOptionsResult Validate(string? name, PortalOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ApiBaseUrl) || !IsHttpBase(options.ApiBaseUrl))
        {
            failures.Add($"{PortalOptions.ApiBaseUrlKey} (API__BASEURL) must be the absolute http or https URL of the TechStrap API, with no query, fragment or user info, for example http://api/.");
        }

        if (string.IsNullOrWhiteSpace(options.PublicUrl))
        {
            if (!environment.IsDevelopment())
            {
                failures.Add($"{PortalOptions.PublicUrlKey} is required: the public address of the Portal, for example https://support.example.com.");
            }
        }
        else if (!IsHttpBase(options.PublicUrl))
        {
            failures.Add($"{PortalOptions.PublicUrlKey} must be an absolute http or https URL with no query, fragment or user info, for example https://support.example.com.");
        }

        if (!string.IsNullOrWhiteSpace(options.DefaultProduct) && !ProductKeyShape.IsWellFormed(options.DefaultProduct.Trim()))
        {
            failures.Add($"{PortalOptions.DefaultProductKey} must be a product key (lowercase letters, digits and single hyphens, at most {ProductKeyShape.MaxLength} characters), or blank.");
        }

        if (!PortalLandingModes.IsKnown(options.Landing))
        {
            failures.Add($"{PortalOptions.LandingKey} must be {PortalLandingModes.Neutral} or {PortalLandingModes.Products}.");
        }
        else if (options.ListsProducts && options.DefaultProductKeyOrNull is not null)
        {
            failures.Add($"{PortalOptions.LandingKey}={PortalLandingModes.Products} and {PortalOptions.DefaultProductKey} cannot both be set: the root either lists the products or redirects to one. Blank one of them.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    /// <summary>An absolute http(s) address that can be a base: no user info, no query, no fragment. A lone "?" or "#" counts (<see cref="Uri.Query"/> and <see cref="Uri.Fragment"/> keep it).</summary>
    private static bool IsHttpBase(string value) =>
        Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
        && string.IsNullOrEmpty(uri.UserInfo)
        && uri.Query.Length == 0
        && uri.Fragment.Length == 0;
}
