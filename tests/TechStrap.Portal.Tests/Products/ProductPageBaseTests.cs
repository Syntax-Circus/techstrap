using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using SyntaxCircus.Common;
using TechStrap.Contracts.Products;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Components.Pages;
using TechStrap.Portal.Products;
using TechStrap.Portal.Tests.Routing;

namespace TechStrap.Portal.Tests.Products;

/// <summary>The product page base on its own, with a counting fake in place of the client: what it asks, and what it shows when the answer is a failure.</summary>
public sealed class ProductPageBaseTests : BunitContext
{
    private sealed class FakeProducts(Result<PublicProductDto> answer) : IPublicProductClient
    {
        public List<string> Calls { get; } = [];

        public Task<Result<PublicProductDto>> GetAsync(string key, CancellationToken cancellationToken)
        {
            Calls.Add(key);
            return Task.FromResult(answer);
        }

        public Task<Result<IReadOnlyList<PublicProductSummaryDto>>> ListAsync(CancellationToken cancellationToken) => throw new NotSupportedException("a page never lists the products");
    }

    private sealed class Environment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;

        public string ApplicationName { get; set; } = "Portal";

        public string ContentRootPath { get; set; } = string.Empty;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private FakeProducts Arrange(Result<PublicProductDto> answer)
    {
        var fake = new FakeProducts(answer);
        Services.AddSingleton<IPublicProductClient>(fake);
        Services.AddSingleton<ProductScope>();
        Services.AddPortalLinks();
        Services.AddSingleton<IHostEnvironment>(new Environment());
        Services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor());
        return fake;
    }

    [Theory]
    [InlineData("Bad_Key")]
    [InlineData("UPPER")]
    [InlineData("-a")]
    [InlineData("a--b")]
    [InlineData("../etc")]
    [InlineData("a b")]
    [InlineData("")]
    public void A_key_that_is_not_a_slug_makes_no_client_call_and_ends_in_not_found(string key)
    {
        var fake = Arrange(Result<PublicProductDto>.Failure(new ResultError("x", "x", ResultErrorKind.Failure)));
        var notFound = false;
        Services.GetRequiredService<NavigationManager>().OnNotFound += (_, _) => notFound = true;

        Render<ProductHome>(p => p.Add(c => c.Key, key));

        fake.Calls.ShouldBeEmpty();
        notFound.ShouldBeTrue();
    }

    [Fact]
    public void A_400_with_its_own_detail_text_shows_only_the_fixed_copy()
    {
        var fake = Arrange(Result<PublicProductDto>.Failure(new ResultError(ApiErrorCodes.ValidationFailed, "Table products column secret_notes violated a constraint", ResultErrorKind.Validation)));

        var cut = Render<ProductHome>(p => p.Add(c => c.Key, "paperplane"));

        fake.Calls.ShouldBe(["paperplane"]);
        cut.Markup.ShouldContain(ProblemCopy.ApiUnavailable);
        cut.Markup.ShouldNotContain("secret_notes");
    }

    [Fact]
    public void A_429_shows_the_rate_limited_copy()
    {
        Arrange(Result<PublicProductDto>.Failure(new ResultError(ApiErrorCodes.RateLimited, ProblemCopy.RateLimited, ResultErrorKind.Failure)));

        var cut = Render<ProductHome>(p => p.Add(c => c.Key, "paperplane"));

        cut.Markup.ShouldContain(ProblemCopy.RateLimited);
    }
}
