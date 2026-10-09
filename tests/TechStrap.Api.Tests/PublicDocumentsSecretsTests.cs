using System.Net;
using System.Text.RegularExpressions;
using Npgsql;
using TechStrap.Api.Tests.Auth;

namespace TechStrap.Api.Tests;

/// <summary>
/// Review T08 (owner decision D-051: the OpenAPI document stays anonymous in Production): the two anonymous documents, the OpenAPI description and readiness, never carry a
/// configured secret or an API key. Production needs the trusted network from the process environment, so the class runs in the non-parallel <see cref="ProcessEnvironmentCollection"/>.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public sealed partial class PublicDocumentsSecretsTests(TestPostgres postgres)
{
    private const string TrustedNetworkVariable = "TrustedProxy__TrustedNetworks__0";
    private const string SmtpCanary = "canary-smtp-secret";
    private const string SentryCanary = "https://canary@sentry.invalid/1";
    private const string JwtSigningKey = "techstrap-test-signing-key-not-a-secret-0123456789";

    [GeneratedRegex("ts[kp]_[A-Za-z0-9_-]{43}")]
    private static partial Regex ApiKeyShape();

    [Fact(Timeout = 120_000)]
    public async Task The_openapi_document_and_readiness_reveal_no_secrets()
    {
        Environment.SetEnvironmentVariable(TrustedNetworkVariable, "10.20.30.0/24");
        try
        {
            var database = await ApiTestDatabase.CreateAsync(postgres);
            var settings = new Dictionary<string, string?>(database.Settings)
            {
                ["Email:Smtp:Password"] = SmtpCanary,
                ["Sentry:Dsn"] = SentryCanary,
            };
            var password = new NpgsqlConnectionStringBuilder(database.ConnectionString).Password;
            password.ShouldNotBeNullOrWhiteSpace();
            await using var factory = new ApiFactory(environment: "Production", settings: settings);
            using var client = factory.CreateClient();

            foreach (var path in new[] { "/openapi/v1.json", "/health/ready" })
            {
                using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);
                var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

                response.StatusCode.ShouldBe(HttpStatusCode.OK, $"{path}: {text}");
                text.ShouldNotBeNullOrWhiteSpace(path);
                text.ShouldNotContain(database.ConnectionString, customMessage: path);
                text.ShouldNotContain(password!, customMessage: path);
                text.ShouldNotContain("Password=", customMessage: path);
                text.ShouldNotContain(JwtSigningKey, customMessage: path);
                text.ShouldNotContain(SmtpCanary, customMessage: path);
                text.ShouldNotContain(SentryCanary, customMessage: path);
                text.ShouldNotContain("canary@sentry", customMessage: path);
                ApiKeyShape().IsMatch(text).ShouldBeFalse($"{path} carries something shaped like an API key");
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(TrustedNetworkVariable, null);
        }
    }
}
