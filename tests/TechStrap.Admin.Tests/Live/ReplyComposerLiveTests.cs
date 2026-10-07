using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Tickets;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Tickets;
using static TechStrap.Admin.Tests.Live.LiveTestData;

namespace TechStrap.Admin.Tests.Live;

/// <summary>
/// The composer's "I am replying" hint (T15): sent when the agent types, at most once per four seconds so the server's ten-second lease stays alive, and cleared on blur, on submit, when the text is emptied, when the ticket changes and
/// when the composer goes. A failing hub never breaks the composer.
/// </summary>
public sealed class ReplyComposerLiveTests : AdminComponentTest
{
    // The requirement is four seconds, written as a literal on purpose: a change of the constant must fail these tests.
    private static readonly TimeSpan FourSeconds = TimeSpan.FromSeconds(4);

    private readonly ITicketsClient _tickets = Substitute.For<ITicketsClient>();
    private readonly RecordingLoggerProvider _logs = new();

    public ReplyComposerLiveTests()
    {
        Services.AddLogging(logging => logging.AddProvider(_logs));
        Services.AddSingleton(_tickets);
        Services.AddScoped<DraftStore>();
        _tickets.ReplyAsync(Arg.Any<Guid>(), Arg.Any<AddAgentReplyRequest>(), Arg.Any<IReadOnlyList<ReplyAttachment>>(), Arg.Any<CancellationToken>())
            .Returns(_ => TestData.Ok(new AgentMessageResponse(TestData.Message(MessageAuthorTypes.Agent), TestData.State(rowVersion: 8))));
    }

    private IRenderedComponent<ReplyComposer> RenderComposer(Guid? ticketId = null) =>
        Render<ReplyComposer>(p => p
            .Add(c => c.TicketId, ticketId ?? TicketId)
            .Add(c => c.TicketNumber, "ORB-42")
            .Add(c => c.RequesterEmail, "ada@example.com")
            .Add(c => c.RowVersion, 7u)
            .Add(c => c.ProductId, TestData.OrbitlyId));

    [Fact]
    public void Typing_sends_one_composing_hint_and_more_typing_inside_the_window_sends_none()
    {
        var cut = RenderComposer();

        cut.Find("textarea").Input("H");
        cut.Find("textarea").Input("He");
        Time.Advance(FourSeconds - TimeSpan.FromMilliseconds(1));
        cut.Find("textarea").Input("Hel");

        LiveClient.Composing.ShouldBe([(TicketId, true)]);
    }

    [Fact]
    public void Typing_after_the_window_sends_the_hint_again_so_the_lease_stays_alive()
    {
        var cut = RenderComposer();
        cut.Find("textarea").Input("H");

        Time.Advance(FourSeconds);
        cut.Find("textarea").Input("He");
        Time.Advance(FourSeconds - TimeSpan.FromMilliseconds(1));
        cut.Find("textarea").Input("Hel");
        Time.Advance(TimeSpan.FromMilliseconds(1));
        cut.Find("textarea").Input("Hell");

        LiveClient.Composing.ShouldBe([(TicketId, true), (TicketId, true), (TicketId, true)]);
    }

    [Fact]
    public void Blur_clears_the_hint_once_and_typing_again_sends_a_new_one_at_once()
    {
        var cut = RenderComposer();
        cut.Find("textarea").Input("Hello");

        cut.Find("textarea").Blur();
        cut.Find("textarea").Blur();
        cut.Find("textarea").Input("Hello again");

        LiveClient.Composing.ShouldBe([(TicketId, true), (TicketId, false), (TicketId, true)]);
    }

    [Fact]
    public void Emptying_the_text_clears_the_hint()
    {
        var cut = RenderComposer();
        cut.Find("textarea").Input("Hello");

        cut.Find("textarea").Input(string.Empty);

        LiveClient.Composing.ShouldBe([(TicketId, true), (TicketId, false)]);
    }

    [Fact]
    public void Nothing_is_sent_when_the_agent_has_not_typed()
    {
        var cut = RenderComposer();

        cut.Find("textarea").Blur();
        cut.Instance.Dispose();

        LiveClient.Composing.ShouldBeEmpty();
    }

    [Fact]
    public void Sending_clears_the_hint()
    {
        var cut = RenderComposer();
        cut.Find("textarea").Input("A reply");

        cut.FindAll(".ts-composer-actions button")[0].Click();

        cut.WaitForAssertion(() => LiveClient.Composing.ShouldBe([(TicketId, true), (TicketId, false)]));
    }

    [Fact]
    public void Disposal_clears_a_hint_that_is_on()
    {
        var cut = RenderComposer();
        cut.Find("textarea").Input("Hello");

        cut.Instance.Dispose();

        LiveClient.Composing.ShouldBe([(TicketId, true), (TicketId, false)]);
    }

    [Fact]
    public void A_composer_that_moves_to_another_ticket_clears_the_hint_for_the_old_one()
    {
        var cut = RenderComposer();
        cut.Find("textarea").Input("Hello");
        var other = Guid.Parse("dddddddd-0000-0000-0000-000000000043");

        cut.Render(p => p.Add(c => c.TicketId, other));

        LiveClient.Composing.ShouldBe([(TicketId, true), (TicketId, false)]);
    }

    [Fact]
    public void Switched_off_the_composer_sends_nothing()
    {
        LiveClient.IsEnabled = false;
        var cut = RenderComposer();

        cut.Find("textarea").Input("Hello");
        cut.Find("textarea").Blur();

        LiveClient.Composing.ShouldBeEmpty();
    }

    [Fact]
    public void A_failing_client_never_breaks_the_composer_and_only_the_type_is_logged()
    {
        LiveClient.Failure = new InvalidOperationException("hub down");
        var cut = RenderComposer();

        cut.Find("textarea").Input("Hello");
        cut.Find("textarea").Blur();
        cut.Instance.Dispose();

        cut.Find("textarea").GetAttribute("value").ShouldBe("Hello");
        _logs.Lines.ShouldContain(line => line.Contains("InvalidOperationException", StringComparison.Ordinal));
        _logs.Lines.ShouldNotContain(line => line.Contains("hub down", StringComparison.Ordinal));
    }
}
