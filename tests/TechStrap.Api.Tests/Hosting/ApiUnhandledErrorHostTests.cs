using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using TechStrap.Api.Tests.Auth;

namespace TechStrap.Api.Tests.Hosting;

/// <summary>
/// Review T08: an unhandled exception in Production is the generic ProblemDetails, never the exception message, type or a stack. The Production host will not start without
/// trusted-proxy configuration, which only the process environment can give it, so the class runs in the non-parallel <see cref="ProcessEnvironmentCollection"/>.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public sealed class ApiUnhandledErrorHostTests(TestPostgres postgres)
{
    private const string TrustedNetworkVariable = "TrustedProxy__TrustedNetworks__0";
    private const string ThrowPath = "/__test/throw";

    // Not an InvalidOperationException: the exception handler answers that one with a 409 conflict, which would make this a different test.
    private sealed class SecretFailureException(string message) : Exception(message);

    private sealed class ThrowingStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            app.Map(ThrowPath, branch => branch.Run(_ => throw new SecretFailureException("password=hunter2 at SecretClass.Method")));
        };
    }

    private static ApiFactory ThrowingFactory(string environment, ApiTestDatabase database) =>
        new(
            environment,
            database.Settings, // the signed-in caller is provisioned as an agent, so the host needs a migrated database
            services => services.AddSingleton<IStartupFilter, ThrowingStartupFilter>());

    [Fact(Timeout = 60_000)]
    public async Task The_throwing_endpoint_is_reached()
    {
        await using var factory = ThrowingFactory("Development", await ApiTestDatabase.CreateAsync(postgres));
        using var client = factory.CreateClient().Bearer(TestJwt.Token("thrower", [TestJwt.AgentGroup], email: "thrower@example.com")); // the fallback policy refuses anonymous callers before the throwing branch

        using var response = await client.GetAsync(ThrowPath, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("internal-error", customMessage: "a 500 from an unmatched path can only come from the throwing branch; the generic body is the same in Development");
    }

    [Fact(Timeout = 60_000)]
    public async Task An_unhandled_exception_in_Production_is_a_problem_response_without_detail_or_stack()
    {
        Environment.SetEnvironmentVariable(TrustedNetworkVariable, "10.20.30.0/24");
        try
        {
            await using var factory = ThrowingFactory("Production", await ApiTestDatabase.CreateAsync(postgres));
            using var client = factory.CreateClient().Bearer(TestJwt.Token("thrower", [TestJwt.AgentGroup], email: "thrower@example.com")); // the fallback policy refuses anonymous callers before the throwing branch

            using var response = await client.GetAsync(ThrowPath, TestContext.Current.CancellationToken);

            var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError, text);
            response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
            using var problem = JsonDocument.Parse(text);
            problem.RootElement.GetProperty("status").GetInt32().ShouldBe(500);
            problem.RootElement.GetProperty("type").GetString().ShouldBe("internal-error");
            problem.RootElement.GetProperty("detail").GetString().ShouldBe("An unexpected error occurred."); // the fixed generic text, never the exception message
            problem.RootElement.TryGetProperty("exception", out _).ShouldBeFalse();
            problem.RootElement.TryGetProperty("stackTrace", out _).ShouldBeFalse();
            text.ShouldNotContain("hunter2");
            text.ShouldNotContain("SecretClass");
            text.ShouldNotContain(nameof(SecretFailureException));
            text.ShouldNotContain(" at ");
        }
        finally
        {
            Environment.SetEnvironmentVariable(TrustedNetworkVariable, null);
        }
    }
}
