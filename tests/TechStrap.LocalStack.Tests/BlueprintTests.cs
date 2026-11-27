using System.Text.RegularExpressions;
using TechStrap.AppHost.Authentik;
using TechStrap.Tests.Shared;

namespace TechStrap.LocalStack.Tests;

/// <summary>
/// The local Authentik blueprint, read as text: the settings sign-in depends on, no literal secret, and logins that
/// match the development data seeder.
/// </summary>
public sealed class BlueprintTests
{
    private static string Blueprint() => ReadNormalized("tools", "TechStrap.AppHost", "Authentik", "blueprints", "techstrap.yaml");

    // The YAML without comment lines, so that a comment can never satisfy or break an assertion.
    private static string Settings() => string.Join(
        '\n', Blueprint().Split('\n').Where(line => !line.TrimStart().StartsWith('#')));

    private static List<string> Entries(string model) =>
        [.. Regex.Split(Settings(), @"(?m)^  - model: ").Where(entry => entry.StartsWith(model + "\n", StringComparison.Ordinal))];

    private static string Provider() => Entries("authentik_providers_oauth2.oauth2provider").ShouldHaveSingleItem();

    private static string User(string username) => Entries("authentik_core.user")
        .Where(entry => entry.Contains($"\n      username: {username}\n", StringComparison.Ordinal))
        .ShouldHaveSingleItem();

    [Fact]
    public void The_top_comment_says_it_is_local_only_and_differs_from_the_deployment_guide()
    {
        var header = string.Join('\n', Blueprint().Split('\n').TakeWhile(line => line.StartsWith('#')));

        header.ShouldContain("LOCAL ONLY");
        header.ShouldContain("user_upn");
        header.ShouldContain("docs/self-hosting/AUTHENTIK.md");
    }

    [Fact]
    public void The_provider_is_the_confidential_techstrap_client()
    {
        var provider = Provider();

        provider.ShouldContain("client_type: confidential\n");
        provider.ShouldContain($"client_id: {AuthentikResources.ClientId}\n");
        provider.ShouldContain("issuer_mode: per_provider\n");
        provider.ShouldContain("include_claims_in_id_token: true\n");
        provider.ShouldContain("signing_key: !Find [authentik_crypto.certificatekeypair, [name, authentik Self-signed Certificate]]");
        Entries("authentik_core.application").ShouldHaveSingleItem().ShouldContain($"slug: {AuthentikResources.Slug}\n");
    }

    [Fact]
    public void The_subject_comes_from_the_upn()
    {
        Provider().ShouldContain("sub_mode: user_upn\n");
    }

    [Fact]
    public void Sign_out_ends_the_Authentik_session()
    {
        Provider().ShouldContain("invalidation_flow: !Find [authentik_flows.flow, [slug, default-invalidation-flow]]\n");
    }

    [Fact]
    public void The_grant_types_are_code_and_refresh()
    {
        Provider().ShouldContain("grant_types:\n        - authorization_code\n        - refresh_token\n      redirect_uris:");
    }

    [Fact]
    public void The_four_scope_mappings_are_attached()
    {
        var scopes = Regex.Matches(Provider(), @"managed, goauthentik\.io/providers/oauth2/scope-(\w+)")
            .Select(match => match.Groups[1].Value);

        scopes.ShouldBe(["openid", "profile", "email", "offline_access"]);
    }

    [Fact]
    public void The_redirect_and_logout_URIs_are_strict_and_come_from_the_Admin_origin()
    {
        var provider = Provider();

        provider.ShouldContain(
            "redirect_uris:\n" +
            "        - matching_mode: strict\n" +
            "          url: !Format [\"%s/signin-oidc\", !Env TS_ADMIN_ORIGIN]\n" +
            "          redirect_uri_type: authorization\n" +
            "        - matching_mode: strict\n" +
            "          url: !Format [\"%s/signout-callback-oidc\", !Env TS_ADMIN_ORIGIN]\n" +
            "          redirect_uri_type: logout\n" +
            "      access_token_validity:");
        Regex.Matches(provider, "matching_mode:").Count.ShouldBe(2);
        Settings().ShouldNotContain("://localhost");
    }

    [Fact]
    public void Passwords_and_the_client_secret_come_only_from_the_environment()
    {
        var secrets = Regex.Matches(Settings(), @"(?m)^\s*(password|client_secret):\s*(.*)$")
            .Select(match => $"{match.Groups[1].Value}: {match.Groups[2].Value}");

        secrets.ShouldBe(
        [
            "password: !Env TS_SAM_PASSWORD",
            "password: !Env TS_RILEY_PASSWORD",
            "password: !Env TS_CASEY_PASSWORD",
            "client_secret: !Env TS_CLIENT_SECRET",
        ]);
        Blueprint().ShouldNotContain("TechStrap-Local-Dev");
    }

    [Fact]
    public void Casey_has_no_group_and_no_upn()
    {
        var casey = User("casey");

        casey.ShouldContain("email: casey@example.com\n");
        casey.ShouldContain("groups: []");
        casey.ShouldNotContain("upn");
        casey.ShouldNotContain("!KeyOf");
    }

    [Fact]
    public void Seeded_logins_match_the_seeder()
    {
        var seeder = ReadNormalized("src", "TechStrap.Infrastructure", "Seeding", "DevelopmentDataSeeder.cs");
        var blueprint = Settings();

        foreach (var value in new[] { "dev|sam", "Sam Whitfield", "sam@example.com", "dev|riley", "Riley Chen", "riley@example.com" })
        {
            seeder.ShouldContain(value, Case.Sensitive);
            blueprint.ShouldContain(value, Case.Sensitive);
        }

        ShouldBeSeeded("sam", "Sam Whitfield", "sam@example.com", "dev|sam", "group-admins");
        ShouldBeSeeded("riley", "Riley Chen", "riley@example.com", "dev|riley", "group-agents");
        Group("group-admins").ShouldContain("name: techstrap-admins\n");
        Group("group-agents").ShouldContain("name: techstrap-agents\n");
    }

    private static void ShouldBeSeeded(string username, string name, string email, string subject, string group)
    {
        var user = User(username);

        user.ShouldContain($"name: {name}\n", Case.Sensitive);
        user.ShouldContain($"email: {email}\n", Case.Sensitive);
        user.ShouldContain($"upn: {subject}\n", Case.Sensitive);
        Regex.Matches(user, @"!KeyOf (\S+)").Select(match => match.Groups[1].Value).ShouldBe([group]);
    }

    private static string Group(string id) => Entries("authentik_core.group")
        .Where(entry => entry.Contains($"\n    id: {id}\n", StringComparison.Ordinal))
        .ShouldHaveSingleItem();

    private static string ReadNormalized(params string[] path) =>
        File.ReadAllText(RepositoryRoot.Combine(path)).ReplaceLineEndings("\n");
}
