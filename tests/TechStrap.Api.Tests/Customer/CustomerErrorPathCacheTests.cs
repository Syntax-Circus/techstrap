using System.Net;
using System.Net.Http.Headers;
using System.Text;
using TechStrap.Api.Startup;
using TechStrap.Api.Tests.Auth;
using TechStrap.Contracts.Http;

namespace TechStrap.Api.Tests.Customer;

/// <summary>Every customer response sets Cache-Control: no-store, including the failures that happen before the action runs.</summary>
public sealed class CustomerErrorPathCacheTests(TestPostgres postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Theory]
    // The filter answers a non-multipart body with 415 after the route's own (Public) policy has run.
    [InlineData("reply-415", HttpStatusCode.UnsupportedMediaType)]
    [InlineData("reply-413", HttpStatusCode.RequestEntityTooLarge)]
    [InlineData("reply-malformed-multipart", HttpStatusCode.BadRequest)]
    [InlineData("lost-link-malformed-json", HttpStatusCode.BadRequest)]
    public async Task Customer_surface_errors_before_the_action_are_no_store(string scenario, HttpStatusCode expected)
    {
        var database = await ApiTestDatabase.CreateAsync(postgres);
        await using var factory = new ApiFactory(settings: database.Settings);
        factory.UseKestrel(0); // RequestSizeLimit (413) needs the real server.
        var seed = await CustomerTestData.SeedAsync(factory, Ct);
        using var client = factory.CreateClient();

        using var request = scenario switch
        {
            "reply-415" => Reply(seed, new StringContent("{}", Encoding.UTF8, "application/json")),
            "reply-413" => Reply(seed, Oversized(), expectContinue: true),
            "reply-malformed-multipart" => Reply(seed, Malformed()),
            _ => new HttpRequestMessage(HttpMethod.Post, "/api/customer/access-link") { Content = new StringContent("{not json", Encoding.UTF8, "application/json") },
        };

        using var response = await client.SendAsync(request, Ct);

        response.StatusCode.ShouldBe(expected, await response.Content.ReadAsStringAsync(Ct));
        response.Headers.CacheControl.ShouldNotBeNull().NoStore.ShouldBeTrue();
    }

    // Expect: 100-continue lets Kestrel reject on Content-Length before the client streams 25 MiB into a closing socket.
    private static HttpRequestMessage Reply(CustomerSeed seed, HttpContent content, bool expectContinue = false)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/customer/ticket/replies") { Content = content };
        request.Headers.ExpectContinue = expectContinue;
        request.Headers.TryAddWithoutValidation(HeaderNames.TicketToken, seed.ValidToken);
        return request;
    }

    private static MultipartFormDataContent Oversized()
    {
        var form = new MultipartFormDataContent { { new StringContent("Hello"), "body" } };
        var file = new ByteArrayContent(new byte[(int)IntakeRequestLimits.FormBodyBytes + 1]);
        file.Headers.ContentType = MediaTypeHeaderValue.Parse("image/png");
        form.Add(file, "attachments", "big.png");
        return form;
    }

    private static StringContent Malformed()
    {
        // No boundary parameter: the form reader throws InvalidDataException, which model binding reports as a 400.
        var content = new StringContent("this is not a multipart body", Encoding.UTF8);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse("multipart/form-data");
        return content;
    }
}
