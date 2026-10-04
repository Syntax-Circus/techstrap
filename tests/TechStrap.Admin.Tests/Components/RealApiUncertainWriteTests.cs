using System.Net;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Tickets;
using TechStrap.Admin.Tests.Clients;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Tickets;
using TechStrap.Tests.Shared.AdminHost;

namespace TechStrap.Admin.Tests.Components;

/// <summary>
/// The real API answers a failed write with a 500 whose problem type is "internal-error". These tests run the real ApiConnection and ticket client (stubbed only
/// at the socket) so the pages see exactly that, and each must show its "this may have happened" copy, not a bare failure.
/// </summary>
public sealed class RealApiUncertainWriteTests : AdminComponentTest
{
    private async Task<ApiHarness> ArrangeAsync(HttpMethod method, string path, bool admin = false)
    {
        var api = await ApiHarness.CreateAsync();
        api.Stub.OnProblem(method, path, HttpStatusCode.InternalServerError, "internal-error", "An unexpected error occurred.");
        Services.AddSingleton<ITicketsClient>(new TicketsClient(api.Get<ApiConnection>()));
        Services.AddSingleton(AgentSessions.SignedIn(admin));
        Services.AddScoped<DraftStore>();
        return api;
    }

    [Fact]
    public async Task A_reply_that_the_api_answers_with_a_500_shows_the_may_already_have_been_sent_notice()
    {
        await using var api = await ArrangeAsync(HttpMethod.Post, $"/api/tickets/{TestData.TicketId}/replies");
        var cut = Render<ReplyComposer>(p => p
            .Add(c => c.TicketId, TestData.TicketId)
            .Add(c => c.TicketNumber, "ORB-42")
            .Add(c => c.RequesterEmail, "ada@example.com")
            .Add(c => c.RowVersion, 7u));
        cut.Find("textarea").Input("Maybe sent.");

        cut.FindAll(".ts-composer-actions button")[0].Click();

        cut.WaitForAssertion(() => cut.Find("[role=alert]").TextContent.ShouldStartWith("The reply may already have been sent."));
    }

    [Fact]
    public async Task A_sidebar_change_that_the_api_answers_with_a_500_shows_the_uncertain_copy()
    {
        await using var api = await ArrangeAsync(HttpMethod.Put, $"/api/tickets/{TestData.TicketId}/status");
        var cut = Render<TicketSidebar>(p => p.Add(c => c.Ticket, TestData.Model()));

        cut.Find("select[id^='ts-sidebar-status-']").Change(TicketStatuses.Solved);

        cut.WaitForAssertion(() => cut.Markup.ShouldContain(SidebarCopy.ChangeUncertain));
    }

    [Fact]
    public async Task A_delete_that_the_api_answers_with_a_500_shows_the_uncertain_copy()
    {
        await using var api = await ArrangeAsync(HttpMethod.Delete, $"/api/tickets/{TestData.TicketId}", admin: true);
        Services.AddSingleton<IRequestersClient>(new RequestersClient(api.Get<ApiConnection>()));
        var cut = Render<TicketActions>(p => p.Add(c => c.Ticket, TestData.Model()));
        cut.FindAll("ul.ts-menu button").Single(b => b.TextContent.Contains("Delete ticket")).Click();
        var dialog = cut.FindAll("dialog").Single(d => d.QuerySelector("h2")!.TextContent.Contains("Delete ORB-42"));
        dialog.QuerySelector("input")!.Input("ORB-42");

        cut.FindAll("dialog").Single(d => d.QuerySelector("h2")!.TextContent.Contains("Delete ORB-42"))
            .QuerySelector(".ts-dialog-actions button:not(.btn-outline-secondary)")!.Click();

        cut.WaitForAssertion(() => cut.Find("dialog [role=alert]").TextContent.ShouldBe(ActionsCopy.DeleteUncertain));
    }
}
