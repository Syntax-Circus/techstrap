using System.Xml.Linq;

namespace TechStrap.Architecture.Tests;

public sealed class WireLiteralTests
{
    [Fact]
    public void No_source_file_outside_the_allowed_paths_types_a_wire_literal()
    {
        var sources = WireLiteralRules.SourceFiles(ProjectGraph.FindRepositoryRoot()).ToList();

        sources.ShouldContain(s => s.Path.EndsWith("IntakeController.cs", StringComparison.Ordinal), "the scan must see the Api sources");
        WireLiteralRules.Evaluate(sources).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("X-Api-Key")]
    [InlineData("X-Ticket-Token")]
    [InlineData("Idempotency-Key")]
    [InlineData("api/intake/tickets")]
    [InlineData("/api/intake/tickets")]
    public void Each_wire_literal_is_flagged(string literal)
    {
        WireLiteralRules.Evaluate([("src/TechStrap.Api/Bad.cs", $"var x = \"{literal}\";")]).ShouldHaveSingleItem();
    }

    [Theory]
    [InlineData("src/TechStrap.Contracts/Http/HeaderNames.cs")]
    [InlineData("src/TechStrap.Contracts/Intake/IntakeRoutes.cs")]
    [InlineData("src/TechStrap.Hosting/Sentry/SensitiveHeaderSentryProcessor.cs")]
    [InlineData("src\\TechStrap.Hosting\\Sentry\\SensitiveHeaderSentryProcessor.cs")]
    public void The_named_paths_may_hold_the_literals(string path)
    {
        WireLiteralRules.Evaluate([(path, "var x = \"X-Api-Key\"; var y = \"api/intake/tickets\";")]).ShouldBeEmpty();
    }

    [Fact]
    public void The_exemption_is_not_a_blanket_for_other_files_in_the_same_project()
    {
        WireLiteralRules.Evaluate([("src/TechStrap.Hosting/Other.cs", "var x = \"X-Api-Key\";")]).ShouldHaveSingleItem();
    }

    [Fact]
    public void Comments_and_partial_text_are_ignored()
    {
        WireLiteralRules.Evaluate(
        [
            ("src/TechStrap.Api/Ok.cs", "    // \"X-Api-Key\" is sent here\n    /// <c>\"Idempotency-Key\"</c>\nvar m = $\"The Idempotency-Key must be set\";"),
        ]).ShouldBeEmpty();
    }

    [Fact]
    public void Packaging_props_package_references_are_all_private_assets()
    {
        var path = Path.Combine(ProjectGraph.FindRepositoryRoot(), "eng", "Packaging.props");

        File.Exists(path).ShouldBeTrue("eng/Packaging.props must exist");
        WireLiteralRules.PackagingReferenceViolations(XDocument.Load(path)).ShouldBeEmpty();
    }

    [Fact]
    public void A_packaging_package_reference_without_private_assets_is_flagged()
    {
        var bad = XDocument.Parse("<Project><ItemGroup><PackageReference Include=\"GitVersion.MsBuild\" /></ItemGroup></Project>");
        var good = XDocument.Parse("<Project><ItemGroup><PackageReference Include=\"GitVersion.MsBuild\" PrivateAssets=\"all\" /></ItemGroup></Project>");

        WireLiteralRules.PackagingReferenceViolations(bad).ShouldHaveSingleItem();
        WireLiteralRules.PackagingReferenceViolations(good).ShouldBeEmpty();
    }
}
