using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Application.Persistence;
using TechStrap.Application.Security;
using TechStrap.Application.Tests.Support;
using TechStrap.Application.Tickets.Customer;
using TechStrap.Domain.Requesters;
using TechStrap.Domain.Tickets;

namespace TechStrap.Application.Tests.Tickets.Customer;

public sealed class CustomerAccessTests
{
    private const string Raw = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQ";
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private readonly FakeTimeProvider _clock = new();
    private readonly IAccessTokenService _tokens = Substitute.For<IAccessTokenService>();
    private readonly ITicketRepository _tickets = Substitute.For<ITicketRepository>();
    private readonly IRequesterRepository _requesters = Substitute.For<IRequesterRepository>();
    private readonly Requester _ann;
    private readonly Ticket _ticket;
    private readonly TicketAccessToken _token;

    public CustomerAccessTests()
    {
        _ann = Requester.Create("ann@example.com", "Ann", null, _clock).Value;
        _ticket = TicketBuilder.New(_clock, Guid.NewGuid(), _ann.Id);
        _token = TicketAccessToken.Issue(_ticket.Id, _ann.Id, "sha256:abc", _clock).Value;
        _tokens.Hash(Raw).Returns("sha256:abc");
        _tickets.GetAccessTokenByHashAsync("sha256:abc", Arg.Any<CancellationToken>()).Returns(_token);
        _tickets.GetByIdAsync(_ticket.Id, Arg.Any<CancellationToken>()).Returns(_ticket);
        _requesters.GetByIdAsync(_ann.Id, Arg.Any<CancellationToken>()).Returns(_ann);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_missing_token_is_not_found_without_a_lookup(string? raw)
    {
        var result = await Resolve(raw);

        AssertNotFound(result);
        _tokens.DidNotReceiveWithAnyArgs().Hash(default!);
        await _tickets.DidNotReceiveWithAnyArgs().GetAccessTokenByHashAsync(default!, Ct);
    }

    [Fact]
    public async Task An_overlong_token_is_not_found_without_a_lookup()
    {
        var result = await Resolve(new string('a', CustomerAccess.MaxTokenLength + 1));

        AssertNotFound(result);
        _tokens.DidNotReceiveWithAnyArgs().Hash(default!);
        await _tickets.DidNotReceiveWithAnyArgs().GetAccessTokenByHashAsync(default!, Ct);
    }

    [Fact]
    public async Task An_unknown_token_is_not_found()
    {
        _tickets.GetAccessTokenByHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((TicketAccessToken?)null);

        AssertNotFound(await Resolve(Raw));
    }

    [Fact]
    public async Task A_revoked_or_expired_token_is_not_found()
    {
        _token.Revoke(_clock);
        AssertNotFound(await Resolve(Raw));

        var expired = TicketAccessToken.Issue(_ticket.Id, _ann.Id, "sha256:abc", _clock).Value;
        _tickets.GetAccessTokenByHashAsync("sha256:abc", Arg.Any<CancellationToken>()).Returns(expired);
        _clock.Advance(TicketAccessToken.Lifetime + TimeSpan.FromSeconds(1));
        AssertNotFound(await Resolve(Raw));
    }

    [Fact]
    public async Task A_token_whose_ticket_is_gone_or_whose_requester_is_erased_is_not_found()
    {
        _tickets.GetByIdAsync(_ticket.Id, Arg.Any<CancellationToken>()).Returns((Ticket?)null);
        AssertNotFound(await Resolve(Raw));

        _tickets.GetByIdAsync(_ticket.Id, Arg.Any<CancellationToken>()).Returns(_ticket);
        _requesters.GetByIdAsync(_ann.Id, Arg.Any<CancellationToken>()).Returns((Requester?)null);
        AssertNotFound(await Resolve(Raw));

        _ann.Erase(_clock);
        _requesters.GetByIdAsync(_ann.Id, Arg.Any<CancellationToken>()).Returns(_ann);
        AssertNotFound(await Resolve(Raw));
    }

    [Fact]
    public async Task Every_failure_is_the_same_error()
    {
        var errors = new List<ResultError>();
        errors.Add((await Resolve(null)).Errors.Single());
        errors.Add((await Resolve(new string('a', 500))).Errors.Single());
        _tickets.GetAccessTokenByHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((TicketAccessToken?)null);
        errors.Add((await Resolve(Raw)).Errors.Single());
        _tickets.GetAccessTokenByHashAsync("sha256:abc", Arg.Any<CancellationToken>()).Returns(_token);
        _token.Revoke(_clock);
        errors.Add((await Resolve(Raw)).Errors.Single());

        errors.Select(e => (e.Code, e.Message, e.Kind)).Distinct().Count().ShouldBe(1);
        errors[0].Code.ShouldBe("not-found");
        errors[0].Message.ShouldBe("Not found.");
        errors[0].Kind.ShouldBe(ResultErrorKind.NotFound);
    }

    [Fact]
    public async Task A_valid_token_resolves_its_ticket_and_requester()
    {
        var result = await Resolve($"  {Raw}  ");

        result.IsSuccess.ShouldBeTrue();
        result.Value.Token.ShouldBeSameAs(_token);
        result.Value.Ticket.ShouldBeSameAs(_ticket);
        result.Value.Requester.ShouldBeSameAs(_ann);
    }

    private Task<Result<CustomerContext>> Resolve(string? raw) =>
        CustomerAccess.ResolveAsync(raw, _tokens, _tickets, _requesters, _clock, Ct);

    private static void AssertNotFound(Result<CustomerContext> result)
    {
        result.IsFailure.ShouldBeTrue();
        var error = result.Errors.Single();
        error.Code.ShouldBe(CustomerErrors.NotFoundCode);
        error.Kind.ShouldBe(ResultErrorKind.NotFound);
    }
}
