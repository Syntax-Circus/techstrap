using System.Net.Http.Json;
using System.Text.Json.Serialization;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace TechStrap.Infrastructure.IntegrationTests.Support;

/// <summary>One message as Mailpit's <c>GET /api/v1/messages</c> reports it. <see cref="MessageId"/> has no angle brackets.</summary>
public sealed record MailpitMessage(string Id, string MessageId, string Subject, string FromName, string FromAddress, IReadOnlyList<string> To);

/// <summary>
/// One <c>axllent/mailpit</c> container (SMTP on 1025, HTTP API on 8025) for the whole collection. Requires Docker.
/// The tag matches the pin in docs/architecture/03-PACKAGE-MAP.md.
/// </summary>
public sealed class MailpitContainer : IAsyncLifetime
{
    public const string Image = "axllent/mailpit:v1.31.4";
    private const int SmtpContainerPort = 1025;
    private const int ApiContainerPort = 8025;
    private const int PageSize = 50;

    private readonly IContainer _container = new ContainerBuilder(Image)
        .WithPortBinding(SmtpContainerPort, true)
        .WithPortBinding(ApiContainerPort, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request.ForPort(ApiContainerPort).ForPath("/api/v1/info")))
        .Build();

    private readonly HttpClient _http = new();

    public string SmtpHost => _container.Hostname;

    public int SmtpPort => _container.GetMappedPublicPort(SmtpContainerPort);

    private string ApiBase => $"http://{_container.Hostname}:{_container.GetMappedPublicPort(ApiContainerPort)}";

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        await _container.DisposeAsync();
    }

    /// <summary>Every message, reading every page.</summary>
    public async Task<IReadOnlyList<MailpitMessage>> MessagesAsync(CancellationToken cancellationToken)
    {
        var all = new List<MailpitMessage>();
        while (true)
        {
            var page = await _http.GetFromJsonAsync<ListResponse>($"{ApiBase}/api/v1/messages?start={all.Count}&limit={PageSize}", cancellationToken)
                ?? throw new InvalidOperationException("Mailpit returned no body.");
            all.AddRange(page.Messages.Select(m => new MailpitMessage(
                m.Id, m.MessageId, m.Subject, m.From.Name, m.From.Value, m.To.Select(t => t.Value).ToList())));
            if (page.Messages.Count == 0 || all.Count >= page.Total)
            {
                return all;
            }
        }
    }

    /// <summary>The HTML body of one message (<c>GET /api/v1/message/{ID}</c>, field <c>HTML</c>).</summary>
    public async Task<string> HtmlAsync(string id, CancellationToken cancellationToken)
    {
        var detail = await _http.GetFromJsonAsync<DetailResponse>($"{ApiBase}/api/v1/message/{id}", cancellationToken)
            ?? throw new InvalidOperationException("Mailpit returned no body.");
        return detail.Html;
    }

    public async Task ClearAsync(CancellationToken cancellationToken) =>
        (await _http.DeleteAsync($"{ApiBase}/api/v1/messages", cancellationToken)).EnsureSuccessStatusCode();

    private sealed record Address([property: JsonPropertyName("Name")] string Name, [property: JsonPropertyName("Address")] string Value);

    private sealed record ListItem(string Id, string MessageId, string Subject, Address From, List<Address> To);

    private sealed record ListResponse(int Total, List<ListItem> Messages);

    private sealed record DetailResponse([property: JsonPropertyName("HTML")] string Html);
}

[CollectionDefinition(Name)]
public sealed class MailpitCollection : ICollectionFixture<MailpitContainer>
{
    public const string Name = "Mailpit";
}
