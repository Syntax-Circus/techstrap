using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TechStrap.Api.Live;
using TechStrap.Api.Security;
using TechStrap.Application.Live;
using TechStrap.Contracts.Live;

namespace TechStrap.Api.Tests.Live;

/// <summary>
/// <c>RoutePolicyCoverageTests</c> only looks at routes that start with <c>api/</c>, so the hub gets its own: every hub endpoint names exactly the Agent policy and is never
/// anonymous, every hub class carries the policy too, and a hub is only a thin adapter over the presence handler.
/// </summary>
public sealed class HubPolicyCoverageTests
{
    [Fact]
    public void Every_hub_endpoint_declares_exactly_the_agent_policy_and_is_not_anonymous()
    {
        using var factory = new ApiFactory();
        var hubs = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.TrimStart('/').StartsWith("hubs/", StringComparison.Ordinal) == true)
            .ToList();

        hubs.ShouldNotBeEmpty();
        hubs.ShouldContain(endpoint => endpoint.RoutePattern.RawText!.TrimStart('/').StartsWith("hubs/tickets", StringComparison.Ordinal));
        foreach (var endpoint in hubs)
        {
            var name = endpoint.RoutePattern.RawText;
            endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(data => data.Policy).Distinct().ShouldBe([AuthorizationPolicies.Agent], name);
            endpoint.Metadata.GetMetadata<IAllowAnonymous>().ShouldBeNull(name);
        }
    }

    [Fact]
    public void The_hub_path_constant_is_the_mapped_path()
    {
        using var factory = new ApiFactory();

        factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .ShouldContain(endpoint => endpoint.RoutePattern.RawText!.TrimStart('/') == TicketHubRoutes.Path.TrimStart('/'));
    }

    [Fact]
    public void Every_hub_class_in_the_api_requires_the_agent_policy_itself()
    {
        var hubs = typeof(TicketHub).Assembly.GetTypes().Where(type => type is { IsClass: true, IsAbstract: false } && typeof(Hub).IsAssignableFrom(type)).ToList();

        hubs.ShouldContain(typeof(TicketHub));
        foreach (var hub in hubs)
        {
            hub.GetCustomAttributes<AuthorizeAttribute>().Select(attribute => attribute.Policy).ShouldContain(AuthorizationPolicies.Agent, hub.Name);
        }
    }

    [Fact]
    public void The_hub_methods_are_the_three_contract_names_and_nothing_else()
    {
        var methods = typeof(TicketHub).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Select(method => method.Name).ToList();

        methods.ShouldBe(
            [TicketHubMethods.JoinTicket, TicketHubMethods.LeaveTicket, TicketHubMethods.SetComposing, nameof(Hub.OnConnectedAsync), nameof(Hub.OnDisconnectedAsync)],
            ignoreOrder: true);
    }

    [Fact]
    public void The_hub_depends_only_on_the_presence_handler_and_the_agent_options()
    {
        var parameters = typeof(TicketHub).GetConstructors().Single().GetParameters().Select(parameter => parameter.ParameterType).ToList();

        parameters.ShouldBe([typeof(IUpdateTicketPresenceHandler), typeof(IOptions<TechStrap.Api.Options.AgentAccessOptions>)], ignoreOrder: true);
    }

    [Fact]
    public void The_handlers_the_hub_and_the_listener_need_are_registered_by_the_api()
    {
        using var factory = new ApiFactory();
        using var scope = factory.Services.CreateScope();

        scope.ServiceProvider.GetRequiredService<IUpdateTicketPresenceHandler>().ShouldNotBeNull();
        scope.ServiceProvider.GetRequiredService<IRelayTicketChangeHandler>().ShouldNotBeNull();
    }

    [Fact]
    public void Presence_is_one_store_for_the_whole_process()
    {
        using var factory = new ApiFactory();

        var first = factory.Services.GetRequiredService<ITicketPresenceStore>();
        using var scope = factory.Services.CreateScope();

        scope.ServiceProvider.GetRequiredService<ITicketPresenceStore>().ShouldBeSameAs(first);
    }
}
