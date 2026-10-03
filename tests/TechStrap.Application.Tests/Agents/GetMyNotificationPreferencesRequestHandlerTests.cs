using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using TechStrap.Application.Agents;
using TechStrap.Application.Persistence;
using TechStrap.Domain.Agents;
using TechStrap.Domain.Products;

namespace TechStrap.Application.Tests.Agents;

public sealed class GetMyNotificationPreferencesRequestHandlerTests
{
    [Fact]
    public async Task Every_active_product_is_listed_and_unset_products_default_to_off()
    {
        var claims = Substitute.For<ICurrentAgentClaims>();
        var agents = Substitute.For<IAgentRepository>();
        var products = Substitute.For<IProductRepository>();
        var me = Agent.Create("me", "Riley", "riley@example.com", AgentRole.Agent, new FakeTimeProvider()).Value;
        var orbitly = Product.Create("orbitly", "Orbitly", "ORB", null, new FakeTimeProvider()).Value;
        var paperplane = Product.Create("paperplane", "Paperplane", "PPL", null, new FakeTimeProvider()).Value;
        claims.Current.Returns(new AgentClaims("me", "Riley", "riley@example.com", AgentRole.Agent));
        agents.GetBySubjectAsync("me", Arg.Any<CancellationToken>()).Returns(me);
        products.ListAsync(true, Arg.Any<CancellationToken>()).Returns([orbitly, paperplane]);
        agents.ListNotificationPreferencesAsync(me.Id, Arg.Any<CancellationToken>()).Returns([new AgentNotificationPreference(me.Id, orbitly.Id, true)]);

        var preferences = (await new GetMyNotificationPreferencesRequestHandler(claims, agents, products).HandleAsync(TestContext.Current.CancellationToken)).Value;

        preferences.Select(p => (p.ProductName, p.NotifyNewTicket)).ShouldBe([("Orbitly", true), ("Paperplane", false)]);
    }
}
