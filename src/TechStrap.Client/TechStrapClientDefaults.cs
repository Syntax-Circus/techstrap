namespace TechStrap.Client;

/// <summary>Names the client registers under: the named <see cref="System.Net.Http.HttpClient"/> and the default configuration section.</summary>
public static class TechStrapClientDefaults
{
    /// <summary>The name of the <see cref="System.Net.Http.HttpClient"/> that <c>AddTechStrapClient</c> registers.</summary>
    public const string HttpClientName = "TechStrap";

    /// <summary>The configuration section a host binds to the client options.</summary>
    public const string ConfigurationSection = "TechStrap";

    /// <summary>The most response body the named client buffers (the intake response is a few hundred bytes).</summary>
    public const long MaxResponseBytes = 1024 * 1024;
}
