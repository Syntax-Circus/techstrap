using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Settings.Webhooks;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Webhooks;

namespace TechStrap.Admin.Tests.Components;

public sealed class WebhookSettingsTests : AdminComponentTest
{
    private readonly IWebhooksClient _client = Substitute.For<IWebhooksClient>();
    private readonly Guid _endpointId = Guid.NewGuid();
    public WebhookSettingsTests()
    {
        Services.AddSingleton(_client);
        _client.GetAsync(Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(Result<WebhookSettingsDto>.Success(Settings()));
        _client.ListAsync(Arg.Any<Guid?>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Result<WebhookDeliveryPageDto>.Success(new([], 1, false)));
    }
    private WebhookSettingsDto Settings(string mode = "Replace") => new(mode, 7, [new(new WebhookEndpointRequest { Id = _endpointId, Name = "Discord channel" }, "discord.com/…")], [new(new WebhookEndpointRequest { Id = _endpointId, Name = "Discord channel" }, "discord.com/…")]);

    [Fact]
    public void Loaded_configuration_masks_secret_and_shows_creation_defaults_and_status_filters()
    {
        var cut = Render<WebhookSettingsEditor>();
        cut.WaitForElement("input[type=password]").GetAttribute("value").ShouldBeNullOrEmpty();
        cut.Markup.ShouldContain("New tickets");
        cut.Markup.ShouldContain("New feature-board entries");
        cut.FindAll("input[type=checkbox]").Count(x => x.HasAttribute("checked")).ShouldBe(5);
        cut.FindAll("input[type=checkbox]").Count.ShouldBe(15);
    }

    [Fact]
    public void Failed_save_preserves_draft_and_carries_version_and_null_unchanged_secret()
    {
        _client.UpdateAsync(Arg.Any<Guid?>(), Arg.Any<UpdateWebhookSettingsRequest>(), Arg.Any<CancellationToken>()).Returns(Result<WebhookSettingsDto>.Failure(new ResultError("webhook-stale", "Reload before saving.", ResultErrorKind.Conflict)));
        var cut = Render<WebhookSettingsEditor>();
        cut.WaitForElement("input[maxlength='100']").Change("Changed channel");
        cut.Find("form").Submit();
        cut.WaitForAssertion(() => cut.Find("[role=alert]").TextContent.ShouldContain("Reload"));
        cut.Find("input[maxlength='100']").GetAttribute("value").ShouldBe("Changed channel");
        var request = (UpdateWebhookSettingsRequest)_client.ReceivedCalls().Single(x => x.GetMethodInfo().Name == nameof(IWebhooksClient.UpdateAsync)).GetArguments()[1]!;
        request.Version.ShouldBe(7);
        request.Endpoints[0].Url.ShouldBeNull();
        request.Endpoints[0].Name.ShouldBe("Changed channel");
    }

    [Fact]
    public void Product_inheritance_shows_effective_endpoints_and_can_be_disabled()
    {
        _client.GetAsync(Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(Result<WebhookSettingsDto>.Success(Settings("Inherit")));
        var product = Guid.NewGuid();
        var cut = Render<WebhookSettingsEditor>(p => p.Add(x => x.ProductId, product));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Discord channel"));
        cut.FindAll("input[type=password]").ShouldBeEmpty();
        cut.Find("select").Change("Disabled");
        cut.FindAll("input[type=password]").ShouldBeEmpty();
        _client.UpdateAsync(product, Arg.Any<UpdateWebhookSettingsRequest>(), Arg.Any<CancellationToken>()).Returns(Result<WebhookSettingsDto>.Success(Settings("Disabled")));
        cut.Find("form").Submit();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Webhook settings saved."));
    }

    [Fact]
    public void A_plain_agent_cannot_render_global_webhook_settings()
    {
        var cut = Render<WebhooksPage>();
        cut.FindAll("section[aria-label='Webhook settings']").ShouldBeEmpty();
        _client.ReceivedCalls().ShouldBeEmpty();
    }
}
