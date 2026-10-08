using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Contracts.Products;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Hosting;
using TechStrap.Portal.Routing;
using TechStrap.Portal.Settings;

namespace TechStrap.Portal.Tests.Routing;

/// <summary>
/// P11e-T06: the one place that knows the clean-path rule. The same product as the request's host gets a clean path; another product with a host gets an absolute https address on that host; a product with no host
/// keeps its /p/{key} path. A ticket path is host-neutral. Absolute builds from the stored host or the public URL, never from a request.
/// </summary>
public sealed class PortalLinksTests
{
    private const string DragonHost = "support.dragonpoop.com";
    private const string PublicUrl = "https://portal.test";
    private static readonly Guid AttachmentId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private static async Task<PortalLinks> LinksAsync(CancellationToken ct, string? requestKey, string? requestHost)
    {
        var client = Substitute.For<IPublicProductClient>();
        IReadOnlyList<PublicProductSummaryDto> products = [new("dragon-poop", "Dragon Poop", DragonHost), new("who-flung-poo", "Who Flung Poo")];
        client.ListAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result<IReadOnlyList<PublicProductSummaryDto>>.Success(products)));
        var services = new ServiceCollection();
        services.AddScoped(_ => client);
        var provider = services.BuildServiceProvider();
        var map = new ProductHostMap(provider.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System, NullLogger<ProductHostMap>.Instance, new HttpContextAccessor());
        await map.RefreshAsync(ct);
        return new PortalLinks(new ProductHostContext { Key = requestKey, Host = requestHost }, map, Options.Create(new PortalOptions { PublicUrl = PublicUrl + "/" }));
    }

    [Fact(Timeout = 30_000)]
    public async Task On_the_products_own_host_every_link_is_a_clean_path()
    {
        var links = await LinksAsync(TestContext.Current.CancellationToken, "dragon-poop", DragonHost);

        links.ProductHome("dragon-poop").ShouldBe("/");
        links.Contact("dragon-poop").ShouldBe("/contact");
        links.ContactReceived("dragon-poop").ShouldBe("/contact/received");
        links.ContactReceived("dragon-poop", "a b").ShouldBe("/contact/received?ref=a%20b");
        links.LostLink("dragon-poop").ShouldBe("/lost-link");
        links.LostLinkSent("dragon-poop").ShouldBe("/lost-link?sent=1");
        links.KbHome("dragon-poop").ShouldBe("/kb");
        links.KbCategory("dragon-poop", "accounts").ShouldBe("/kb/accounts");
        links.KbCategory("dragon-poop", "accounts", 3).ShouldBe("/kb/accounts?page=3");
        links.KbArticle("dragon-poop", "accounts", "reset").ShouldBe("/kb/accounts/reset");
        links.KbSearch("dragon-poop").ShouldBe("/kb/search");
        links.KbSearch("dragon-poop", "paper jam", 2).ShouldBe("/kb/search?q=paper%20jam&page=2");
        links.Suggest("dragon-poop").ShouldBe("/suggest");
    }

    [Fact(Timeout = 30_000)]
    public async Task Another_product_with_a_host_is_an_absolute_https_address_on_that_host()
    {
        var links = await LinksAsync(TestContext.Current.CancellationToken, "who-flung-poo", null);

        links.ProductHome("dragon-poop").ShouldBe("https://support.dragonpoop.com/");
        links.KbArticle("dragon-poop", "accounts", "reset").ShouldBe("https://support.dragonpoop.com/kb/accounts/reset");
        links.KbSearch("dragon-poop", "x", 2).ShouldBe("https://support.dragonpoop.com/kb/search?q=x&page=2");
    }

    [Theory(Timeout = 30_000)]
    [InlineData(null, null)]
    [InlineData("dragon-poop", DragonHost)]
    public async Task A_product_without_a_host_keeps_its_prefix_path(string? requestKey, string? requestHost)
    {
        var links = await LinksAsync(TestContext.Current.CancellationToken, requestKey, requestHost);

        links.ProductHome("who-flung-poo").ShouldBe("/p/who-flung-poo");
        links.Contact("who-flung-poo").ShouldBe("/p/who-flung-poo/contact");
        links.KbArticle("who-flung-poo", "accounts", "reset").ShouldBe("/p/who-flung-poo/kb/accounts/reset");
        links.KbSearch("who-flung-poo", "x", 1).ShouldBe("/p/who-flung-poo/kb/search?q=x");
        links.Suggest("unknown-key").ShouldBe("/p/unknown-key/suggest");
    }

    [Fact(Timeout = 30_000)]
    public async Task On_the_default_host_a_product_with_a_host_is_on_its_host()
    {
        var links = await LinksAsync(TestContext.Current.CancellationToken, null, null);

        links.Contact("dragon-poop").ShouldBe("https://support.dragonpoop.com/contact");
        links.ProductHome("dragon-poop").ShouldBe("https://support.dragonpoop.com/");
    }

    [Theory(Timeout = 30_000)]
    [InlineData(null, null)]
    [InlineData("dragon-poop", DragonHost)]
    public async Task A_ticket_link_is_always_relative(string? requestKey, string? requestHost)
    {
        var links = await LinksAsync(TestContext.Current.CancellationToken, requestKey, requestHost);

        links.Ticket("abc").ShouldBe("/t/abc");
        links.TicketAttachment("abc", AttachmentId).ShouldBe("/t/abc/attachments/11111111-2222-3333-4444-555555555555");
    }

    [Fact(Timeout = 30_000)]
    public async Task Absolute_uses_the_stored_host_on_a_product_host_and_the_public_url_elsewhere()
    {
        var onProduct = await LinksAsync(TestContext.Current.CancellationToken, "dragon-poop", DragonHost);
        var onDefault = await LinksAsync(TestContext.Current.CancellationToken, null, null);

        onProduct.Absolute("/kb/a/b").ShouldBe("https://support.dragonpoop.com/kb/a/b");
        onDefault.Absolute("/p/who-flung-poo/kb/a/b").ShouldBe("https://portal.test/p/who-flung-poo/kb/a/b");
        onProduct.Absolute("https://other.example/x").ShouldBe("https://other.example/x");
        onDefault.Absolute("https://support.dragonpoop.com/kb").ShouldBe("https://support.dragonpoop.com/kb");
    }

    [Fact(Timeout = 30_000)]
    public async Task A_jump_link_on_a_product_host_leaves_the_prefix_of_the_rewritten_request_off()
    {
        var onProduct = await LinksAsync(TestContext.Current.CancellationToken, "dragon-poop", DragonHost);
        var onDefault = await LinksAsync(TestContext.Current.CancellationToken, null, null);

        onProduct.ToFragment("https://support.dragonpoop.com/p/dragon-poop/contact?subject=a&utm=1", "email", keepQuery: true).ShouldBe("/contact?subject=a#email");
        onProduct.ToFragment("https://support.dragonpoop.com/p/dragon-poop", "main", keepQuery: false).ShouldBe("/#main");
        onProduct.ToFragment("https://support.dragonpoop.com/t/abc", "reply", keepQuery: false).ShouldBe("/t/abc#reply");
        onDefault.ToFragment("https://portal.test/p/dragon-poop/contact?subject=a", "email", keepQuery: true).ShouldBe("/p/dragon-poop/contact?subject=a#email");
    }
}
