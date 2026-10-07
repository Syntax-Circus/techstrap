using Microsoft.Extensions.Options;
using TechStrap.Contracts.Http;

namespace TechStrap.Client.Tests.Registration;

public sealed class ApiKeyHandlerTests
{
    private const string Key = "sk_live_0123456789abcdef";

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }
    }

    private static (HttpMessageInvoker Invoker, RecordingHandler Inner) Create(string baseAddress = "https://support.example.com/")
    {
        var options = Microsoft.Extensions.Options.Options.Create(new TechStrapClientOptions { BaseAddress = new Uri(baseAddress), ApiKey = Key });
        var inner = new RecordingHandler();
        var handler = new ApiKeyHandler(options) { InnerHandler = inner };
        return (new HttpMessageInvoker(handler), inner);
    }

    [Fact]
    public async Task The_handler_sets_the_key_header()
    {
        var (invoker, inner) = Create();
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://support.example.com/api/intake/tickets");

        using var response = await invoker.SendAsync(request, Xunit.TestContext.Current.CancellationToken);

        inner.Requests.ShouldHaveSingleItem().Headers.GetValues(HeaderNames.ApiKey).ShouldBe([Key]);
    }

    [Fact]
    public async Task The_handler_replaces_a_key_the_caller_set()
    {
        var (invoker, inner) = Create();
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://support.example.com/api/intake/tickets");
        request.Headers.Add(HeaderNames.ApiKey, "caller-supplied");

        using var response = await invoker.SendAsync(request, Xunit.TestContext.Current.CancellationToken);

        inner.Requests.ShouldHaveSingleItem().Headers.GetValues(HeaderNames.ApiKey).ShouldBe([Key]);
    }

    [Theory]
    [InlineData("https://evil.example.com/api/intake/tickets")]
    [InlineData("http://support.example.com/api/intake/tickets")]
    [InlineData("https://support.example.com:8443/api/intake/tickets")]
    [InlineData("https://support.example.com.evil.test/api/intake/tickets")]
    public async Task A_foreign_authority_throws_without_sending_and_without_naming_the_key(string url)
    {
        var (invoker, inner) = Create();
        using var request = new HttpRequestMessage(HttpMethod.Post, url);

        var failure = await Should.ThrowAsync<InvalidOperationException>(() => invoker.SendAsync(request, Xunit.TestContext.Current.CancellationToken));

        inner.Requests.ShouldBeEmpty();
        failure.Message.ShouldContain(new Uri(url).GetLeftPart(UriPartial.Authority));
        failure.Message.ShouldNotContain(Key);
        request.Headers.Contains(HeaderNames.ApiKey).ShouldBeFalse();
    }

    [Fact]
    public async Task The_authority_comparison_ignores_case_and_the_path()
    {
        var (invoker, inner) = Create("https://Support.Example.com/base/");
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://SUPPORT.example.com/elsewhere");

        using var response = await invoker.SendAsync(request, Xunit.TestContext.Current.CancellationToken);

        inner.Requests.Count.ShouldBe(1);
    }
}
