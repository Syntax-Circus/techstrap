using Microsoft.Extensions.Time.Testing;
using TechStrap.Domain.Agents;
using TechStrap.Domain.Products;
using TechStrap.Domain.Requesters;
using TechStrap.Domain.Tickets;

namespace TechStrap.Domain.Tests.Rules;

/// <summary>Every stored timestamp is whole microseconds (the resolution of timestamptz), even when the clock has sub-microsecond ticks.</summary>
public sealed class DomainTimeTruncationTests
{
    // 3 ticks past a whole microsecond: not a multiple of 10 ticks.
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.Zero).AddTicks(3));

    private static void ShouldBeWholeMicroseconds(DateTimeOffset? value) => (value!.Value.Ticks % 10).ShouldBe(0);

    [Fact]
    public void An_api_key_stamps_whole_microseconds_for_create_revoke_and_use()
    {
        var used = ProductApiKey.Create(Guid.NewGuid(), ApiKeyKind.Trusted, "tsk_abcd", "h", null, _clock).Value;
        var revoked = ProductApiKey.Create(Guid.NewGuid(), ApiKeyKind.Trusted, "tsk_abcd", "h", null, _clock).Value;

        used.RecordUse(_clock);
        revoked.Revoke(_clock);

        ShouldBeWholeMicroseconds(used.CreatedAt);
        ShouldBeWholeMicroseconds(used.LastUsedAt);
        ShouldBeWholeMicroseconds(revoked.RevokedAt);
    }

    [Fact]
    public void An_erased_requester_stamps_whole_microseconds()
    {
        var requester = Requester.Create("ann@example.com", "Ann", null, _clock).Value;

        requester.Erase(_clock);

        ShouldBeWholeMicroseconds(requester.ErasedAt);
    }

    [Fact]
    public void An_attachment_stamps_whole_microseconds()
    {
        ShouldBeWholeMicroseconds(Attachment.Create(Guid.NewGuid(), Guid.NewGuid(), "a.txt", "text/plain", 1, "k", _clock).Value.CreatedAt);
    }

    [Fact]
    public void A_message_stamps_whole_microseconds()
    {
        ShouldBeWholeMicroseconds(Message.Create(Guid.NewGuid(), AuthorType.System, null, MessageVisibility.Public, "x", _clock).Value.CreatedAt);
    }

    [Fact]
    public void An_access_token_stamps_whole_microseconds_for_issue_use_and_revoke()
    {
        var token = TicketAccessToken.Issue(Guid.NewGuid(), Guid.NewGuid(), "hash", _clock).Value;
        ShouldBeWholeMicroseconds(token.ExpiresAt);

        token.RecordUse(_clock).IsSuccess.ShouldBeTrue();
        ShouldBeWholeMicroseconds(token.LastUsedAt);
        ShouldBeWholeMicroseconds(token.ExpiresAt);

        token.Revoke(_clock);
        ShouldBeWholeMicroseconds(token.RevokedAt);
    }

    [Fact]
    public void An_agent_sighting_stamps_whole_microseconds()
    {
        var agent = Agent.Create("oidc|sam", "Sam", "sam@example.com", AgentRole.Agent, _clock).Value;

        agent.RecordSeen(_clock);

        ShouldBeWholeMicroseconds(agent.LastSeenAt);
    }
}
