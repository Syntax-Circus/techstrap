namespace TechStrap.Tests.Shared.AdminHost;

/// <summary>The settings an Admin host needs to start (the options are validated on start). Test factories merge their own overrides on top.</summary>
public static class AdminTestSettings
{
    public const string Authority = "https://idp.test/application/o/techstrap-admin/";
    public const string ApiBaseUrl = "http://api.test/";

    public static IReadOnlyDictionary<string, string?> Required { get; } = new Dictionary<string, string?>
    {
        ["Auth:Authority"] = Authority,
        ["Auth:ClientId"] = "techstrap-admin-test",
        ["Auth:ClientSecret"] = "test-client-secret",
        ["Api:BaseUrl"] = ApiBaseUrl,
    };

    /// <summary>
    /// The required settings with <paramref name="overrides"/> applied. A null value blanks the key (rather than dropping it, so a variable set
    /// in the developer's shell cannot fill it in), which lets a test prove a missing setting stops the start.
    /// </summary>
    public static Dictionary<string, string?> With(IReadOnlyDictionary<string, string?>? overrides = null)
    {
        var settings = new Dictionary<string, string?>(Required, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in overrides ?? new Dictionary<string, string?>())
        {
            settings[key] = value ?? string.Empty;
        }

        return settings;
    }
}
