using TechStrap.Application.Webhooks;
using TechStrap.Contracts.Webhooks;

namespace TechStrap.Application.Tests.Webhooks;

public sealed class WebhookPolicyTests
{
    [Fact]
    public void Defaults_notify_on_creation_for_both_ticket_types_and_features()
    {
        var endpoint = new WebhookEndpointRequest();
        WebhookPolicy.Matches(endpoint, "ticket.created", "Support", "New").ShouldBeTrue();
        WebhookPolicy.Matches(endpoint, "ticket.created", "FeatureRequest", "New").ShouldBeTrue();
        WebhookPolicy.Matches(endpoint, "feature.created", null, "UnderConsideration").ShouldBeTrue();
        WebhookPolicy.Matches(endpoint, "ticket.status_changed", "Support", "Open").ShouldBeFalse();
    }

    [Fact]
    public void Status_filters_are_independent_of_creation_and_ticket_type()
    {
        var endpoint = new WebhookEndpointRequest { TicketCreated = false, TicketTypes = ["Support"], TicketStatuses = ["Solved"], FeatureStatuses = ["Shipped"] };
        WebhookPolicy.Matches(endpoint, "ticket.created", "Support", "New").ShouldBeFalse();
        WebhookPolicy.Matches(endpoint, "ticket.status_changed", "Support", "Solved").ShouldBeTrue();
        WebhookPolicy.Matches(endpoint, "ticket.status_changed", "FeatureRequest", "Solved").ShouldBeFalse();
        WebhookPolicy.Matches(endpoint, "feature.status_changed", null, "Shipped").ShouldBeTrue();
        WebhookPolicy.Matches(endpoint with { Enabled = false }, "feature.created", null, "UnderConsideration").ShouldBeFalse();
    }

    [Theory]
    [InlineData("http://example.com/hook")]
    [InlineData("https://user:pass@example.com/hook")]
    [InlineData("https://127.0.0.1/hook")]
    [InlineData("https://localhost/hook")]
    [InlineData("https://example.com/hook#fragment")]
    public void Unsafe_urls_are_rejected(string url) => WebhookPolicy.ValidUrl(url).ShouldBeFalse();

    [Fact]
    public void Public_https_webhook_is_valid() => WebhookPolicy.ValidUrl("https://discord.com/api/webhooks/123/secret").ShouldBeTrue();
}
