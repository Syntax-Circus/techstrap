using System.Reflection;
using System.Text.RegularExpressions;
using TechStrap.Domain.Rules;
using TechStrap.Portal.Routing;

namespace TechStrap.Architecture.Tests;

/// <summary>
/// The Portal cannot reference the Domain, so <see cref="ProductKeyShape"/> carries a copy of the Domain's slug rule. This is the one place both are visible: the copy must say what the Domain says,
/// or a key the Domain accepts would be refused (or a malformed one let through) at the Portal's door. The Domain's pattern is a private constant on an internal class, read by reflection.
/// </summary>
public sealed class ProductKeyParityTests
{
    private static string DomainPattern()
    {
        var guard = typeof(DomainLimits).Assembly.GetType("TechStrap.Domain.Rules.Guard", throwOnError: true)!;
        var field = guard.GetField("SlugPattern", BindingFlags.NonPublic | BindingFlags.Static)!;
        field.ShouldNotBeNull("Guard.SlugPattern moved: update this parity test");
        return (string)field.GetRawConstantValue()!;
    }

    private static string PortalPattern()
    {
        var method = typeof(ProductKeyShape).GetMethod("Slug", BindingFlags.NonPublic | BindingFlags.Static)!;
        method.ShouldNotBeNull("ProductKeyShape.Slug moved: update this parity test");
        return method.GetCustomAttribute<GeneratedRegexAttribute>()!.Pattern;
    }

    /// <summary>The pattern without its anchors: the Domain writes <c>^...$</c>, the Portal <c>\A...\z</c> (stricter about a trailing newline), the rest must be identical.</summary>
    private static string Core(string pattern) => pattern.Replace(@"\A", "").Replace(@"\z", "").TrimStart('^').TrimEnd('$');

    [Fact]
    public void The_Portal_key_pattern_is_the_Domain_slug_pattern()
    {
        Core(PortalPattern()).ShouldBe(Core(DomainPattern()));
        Core(PortalPattern()).ShouldNotBeEmpty();
    }

    [Fact]
    public void The_Portal_key_length_is_the_Domain_slug_length()
    {
        ProductKeyShape.MaxLength.ShouldBe(DomainLimits.SlugMaxLength);
    }
}
