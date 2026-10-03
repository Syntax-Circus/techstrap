using Microsoft.Extensions.DependencyInjection;
using TechStrap.Application.Persistence;
using TechStrap.Application.Tickets;
using TechStrap.Domain.Agents;
using TechStrap.Domain.Knowledge;
using TechStrap.Domain.Requesters;
using TechStrap.Domain.Tickets;

namespace TechStrap.Infrastructure.IntegrationTests;

/// <summary>The reads ticket operations need: summaries with tags, view counts, single messages and attachments, fresh state, article links.</summary>
public sealed class TicketOperationReadTests(PostgresFixture postgres) : PostgresIntegrationTestBase(postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Summaries_carry_the_requester_name_and_the_ticket_tags()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        var ann = Requester.Create("ann.lee@example.com", "Ann Lee", null, host.Clock).Value;
        var bug = Tag.Create("bug", "Bug", "#FF0000", host.Clock).Value;
        var vip = Tag.Create("vip", "VIP", "#00FF00", host.Clock).Value;
        await host.CommitAsync(sp =>
        {
            sp.GetRequiredService<IRequesterRepository>().Add(ann);
            var tags = sp.GetRequiredService<ITagRepository>();
            tags.Add(bug);
            tags.Add(vip);
            return Task.CompletedTask;
        });
        Guid ticketId = Guid.Empty;
        (await host.CommitAsync(async sp =>
        {
            var number = (await sp.GetRequiredService<ITicketNumberAllocator>().AllocateAsync(scenario.Acme.Id, Ct)).Value;
            var ticket = Ticket.Create(number, scenario.Acme.Id, ann.Id, "Tagged", TicketChannel.Web, null, false, host.Clock).Value;
            ticket.AddCustomerReply(ann.Id, "<p>hi</p>", host.Clock).IsSuccess.ShouldBeTrue();
            ticket.AddTag(bug.Id, scenario.AgentActor, host.Clock).IsSuccess.ShouldBeTrue();
            ticket.AddTag(vip.Id, scenario.AgentActor, host.Clock).IsSuccess.ShouldBeTrue();
            sp.GetRequiredService<ITicketRepository>().Add(ticket);
            ticketId = ticket.Id;
        })).IsSuccess.ShouldBeTrue();

        var page = await host.ReadAsync(sp => sp.GetRequiredService<ITicketRepository>().ListAsync(new TicketQuery(TicketView.All), Ct));

        var row = page.Items.ShouldHaveSingleItem();
        row.Id.ShouldBe(ticketId);
        row.RequesterName.ShouldBe("Ann Lee");
        row.TagIds.Order().ShouldBe(new[] { bug.Id, vip.Id }.Order());
    }

    [Fact]
    public async Task View_counts_match_the_lists_for_every_view()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        var other = Agent.Create("oidc|bo", "Bo B.", "bo@example.com", AgentRole.Agent, host.Clock).Value;
        await host.CommitAsync(sp => { sp.GetRequiredService<IAgentRepository>().Add(other); return Task.CompletedTask; });
        var actor = scenario.AgentActor;

        var ids = new List<Guid>();
        for (var i = 0; i < 12; i++)
        {
            ids.Add((await scenario.CreateTicketAsync($"T{i}")).Id);
        }

        async Task Mutate(int index, Action<Ticket> change) => (await scenario.UpdateAsync(ids[index], change)).IsSuccess.ShouldBeTrue();
        await Mutate(0, t => { t.ChangeStatus(TicketStatus.Open, actor, host.Clock); t.Assign(scenario.Agent.Id, actor, host.Clock); });
        await Mutate(1, t => { t.ChangeStatus(TicketStatus.Pending, actor, host.Clock); t.Assign(scenario.Agent.Id, actor, host.Clock); });
        await Mutate(2, t => t.Assign(scenario.Agent.Id, actor, host.Clock));
        await Mutate(3, t => t.Assign(other.Id, actor, host.Clock));
        await Mutate(4, t => { t.ChangeStatus(TicketStatus.Open, actor, host.Clock); t.Assign(other.Id, actor, host.Clock); });
        await Mutate(5, t => t.ChangeStatus(TicketStatus.Open, actor, host.Clock));
        await Mutate(6, t => t.ChangeStatus(TicketStatus.Pending, actor, host.Clock));
        await Mutate(7, t => t.ChangeStatus(TicketStatus.Solved, actor, host.Clock));
        await Mutate(8, t => { t.ChangeStatus(TicketStatus.Solved, actor, host.Clock); t.ChangeStatus(TicketStatus.Closed, Actor.System, host.Clock); });
        await Mutate(9, t => t.MarkSpam(true, actor, host.Clock));

        var counts = await host.ReadAsync(sp => sp.GetRequiredService<ITicketRepository>().CountViewsAsync(scenario.Agent.Id, Ct));
        async Task<int> Listed(TicketView view) =>
            (await host.ReadAsync(sp => sp.GetRequiredService<ITicketRepository>()
                .ListAsync(new TicketQuery(view, AgentId: scenario.Agent.Id, PageSize: 100), Ct))).TotalCount;

        counts.Unassigned.ShouldBe(await Listed(TicketView.Unassigned));
        counts.Mine.ShouldBe(await Listed(TicketView.Mine));
        counts.Open.ShouldBe(await Listed(TicketView.Open));
        counts.Pending.ShouldBe(await Listed(TicketView.Pending));
        counts.All.ShouldBe(await Listed(TicketView.All));
        counts.Spam.ShouldBe(await Listed(TicketView.Spam));
        counts.Spam.ShouldBe(1);
        counts.Mine.ShouldBe(3);
    }

    [Fact]
    public async Task An_attachment_and_a_message_can_be_read_by_their_own_ids()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        var ticket = await scenario.CreateTicketAsync();
        Guid messageId = Guid.Empty;
        Guid attachmentId = Guid.Empty;
        await scenario.UpdateAsync(ticket.Id, t =>
        {
            var message = t.AddAgentReply(scenario.Agent.Id, "<p>See attached</p>", host.Clock).Value;
            messageId = message.Id;
            attachmentId = message.AddAttachment("log.txt", "text/plain", 120, "tickets/1/log.txt", host.Clock).Value.Id;
        });

        var repository = (Func<IServiceProvider, ITicketRepository>)(sp => sp.GetRequiredService<ITicketRepository>());
        var attachment = await host.ReadAsync(sp => repository(sp).GetAttachmentByIdAsync(attachmentId, Ct));
        var message = await host.ReadAsync(sp => repository(sp).GetMessageAsync(messageId, Ct));

        attachment!.FileName.ShouldBe("log.txt");
        attachment.TicketId.ShouldBe(ticket.Id);
        message!.Id.ShouldBe(messageId);
        message.TicketId.ShouldBe(ticket.Id);
        (await host.ReadAsync(sp => repository(sp).GetAttachmentByIdAsync(Guid.NewGuid(), Ct))).ShouldBeNull();
        (await host.ReadAsync(sp => repository(sp).GetMessageAsync(Guid.NewGuid(), Ct))).ShouldBeNull();
    }

    [Fact]
    public async Task Ticket_state_after_a_commit_has_the_new_version()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        var created = await scenario.CreateTicketAsync();

        await using var scope = host.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ITicketRepository>();
        await using var unitOfWork = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync(Ct);
        var ticket = (await repository.GetByIdAsync(created.Id, Ct))!;
        var before = ticket.Version;
        ticket.ChangePriority(TicketPriority.High, scenario.AgentActor, host.Clock).IsSuccess.ShouldBeTrue();
        repository.Update(ticket);
        (await unitOfWork.CommitAsync(Ct)).IsSuccess.ShouldBeTrue();

        var state = await repository.GetStateAsync(created.Id, Ct);

        state!.Version.ShouldNotBe(before);
        state.Priority.ShouldBe(TicketPriority.High);
        (await repository.GetStateAsync(Guid.NewGuid(), Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Ticket_article_links_are_listed_per_message()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        var article = KbArticle.Create(scenario.Acme.Id, null, "guide", "Guide", "summary", "# body", scenario.Agent.Id, host.Clock).Value;
        await host.CommitAsync(sp => { sp.GetRequiredService<IKbRepository>().AddArticle(article); return Task.CompletedTask; });
        var ticket = await scenario.CreateTicketAsync();
        Guid second = Guid.Empty;
        await scenario.UpdateAsync(ticket.Id, t =>
        {
            t.AddAgentReply(scenario.Agent.Id, "<p>one</p>", host.Clock).IsSuccess.ShouldBeTrue();
            second = t.AddAgentReply(scenario.Agent.Id, "<p>two</p>", host.Clock).Value.Id;
        });
        (await host.CommitAsync(sp =>
        {
            sp.GetRequiredService<IKbRepository>().AddTicketArticle(new TicketArticle(ticket.Id, second, article.Id));
            return Task.CompletedTask;
        })).IsSuccess.ShouldBeTrue();

        var links = await host.ReadAsync(sp => sp.GetRequiredService<IKbRepository>().ListTicketArticlesAsync(ticket.Id, Ct));

        links.ShouldHaveSingleItem().ShouldBe(new TicketArticle(ticket.Id, second, article.Id));
    }
}
