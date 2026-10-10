using System.Reflection;
using System.Text.RegularExpressions;

namespace TechStrap.Admin.Features.Shell;

/// <summary>
/// The semantic version of the running build, for the foot of the rail. The images are built with <c>/p:InformationalVersion</c> from GitVersion, so the assembly's informational version looks like
/// <c>0.4.1</c>, <c>0.5.0-rc.1+Branch.main.Sha.abc123</c> or (the SDK's own suffix) <c>0.4.1+&lt;commit&gt;</c>. Only the SemVer 2.0 core and the prerelease label are kept; the build metadata
/// (everything from the first <c>+</c>) is dropped, and a value that is not SemVer (a local build, <c>1.0.0.0</c>) shows nothing. Registered as a singleton so a host test can replace it.
/// </summary>
public sealed partial class BuildVersion(string? value)
{
    // MAJOR.MINOR.PATCH[-prerelease][+build], numeric parts without leading zeros (SemVer 2.0), an optional leading "v".
    [GeneratedRegex(@"^v?(?<core>(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*))(?<pre>-(?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*)(?:\.(?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*))*)?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$", RegexOptions.CultureInvariant)]
    private static partial Regex SemVer();

    /// <summary>The semver to show (no <c>v</c> prefix, no build metadata), or null when there is none to show.</summary>
    public string? Semver { get; } = Parse(value);

    /// <summary>Reads the version once from the Admin assembly's <see cref="AssemblyInformationalVersionAttribute"/>.</summary>
    public static BuildVersion FromAssembly() =>
        new(typeof(BuildVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    /// <summary>The core and prerelease label of a SemVer 2.0 string, without a leading <c>v</c> or the build metadata; null when <paramref name="value"/> is not SemVer.</summary>
    public static string? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var match = SemVer().Match(value.Trim());
        return match.Success ? match.Groups["core"].Value + match.Groups["pre"].Value : null;
    }
}
