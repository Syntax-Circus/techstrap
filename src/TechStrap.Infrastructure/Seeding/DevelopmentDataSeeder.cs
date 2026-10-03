using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SyntaxCircus.Common;
using TechStrap.Application.Persistence;
using TechStrap.Application.Seeding;
using TechStrap.Domain;
using TechStrap.Domain.Agents;
using TechStrap.Domain.Knowledge;
using TechStrap.Domain.Products;
using TechStrap.Domain.Requesters;
using TechStrap.Domain.Tickets;
using TechStrap.Infrastructure.Persistence;

namespace TechStrap.Infrastructure.Seeding;

/// <summary>
/// Demo data for development (02-ARCHITECTURE section 7.6): two products with both API key kinds, two agents, three requesters,
/// tags, tickets in every status plus a spam ticket and a follow-up pair, and knowledge-base articles. It goes through the same
/// repositories, numbering and domain methods as real traffic. It runs in two units of work, because ticket numbers are allocated
/// from products that must already be committed: first the foundation (products, keys, agents, requesters, tags), then the
/// tickets and knowledge base. Each part skips itself when its marker already exists, so running it again does nothing and a
/// run that stopped between the two parts finishes on the next start. The whole run holds a Postgres advisory lock
/// (<see cref="TechStrapDatabase.SeedLockKey"/>), so two API instances starting together seed one after the other: the second
/// waits, finds the markers and skips, with no deadlock and no duplicate data.
/// </summary>
public sealed class DevelopmentDataSeeder(
    TechStrapDbContext context,
    IUnitOfWork unitOfWork,
    IProductRepository products,
    IAgentRepository agents,
    IRequesterRepository requesters,
    ITagRepository tags,
    ITicketRepository tickets,
    IKbRepository knowledgeBase,
    ITicketNumberAllocator numbers,
    TimeProvider clock,
    ILogger<DevelopmentDataSeeder> logger) : IDevelopmentDataSeeder
{
    /// <summary>Marks the foundation part as done.</summary>
    public const string MarkerProductKey = "orbitly";

    /// <summary>Marks the tickets and knowledge-base part as done (the last thing it creates).</summary>
    public const string MarkerArticleSlug = "welcome";

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        // One instance at a time: a second API instance starting together waits here, then finds the marker and skips.
        await using var seedLock = await AdvisoryLock.AcquireAsync(context.Database.GetConnectionString()!, TechStrapDatabase.SeedLockKey, cancellationToken);

        if (await products.GetByKeyAsync(MarkerProductKey, cancellationToken) is null)
        {
            await CommitAsync("foundation", () => StageFoundationAsync(cancellationToken), cancellationToken);
        }

        if (await knowledgeBase.GetArticleBySlugAsync(null, MarkerArticleSlug, cancellationToken) is null)
        {
            await CommitAsync("tickets and knowledge base", () => StageTicketsAndKnowledgeBaseAsync(cancellationToken), cancellationToken);
        }
        else
        {
            logger.LogInformation("Development data is already present; seeding skipped.");
        }
    }

    private async Task CommitAsync(string part, Func<Task> stage, CancellationToken cancellationToken)
    {
        await using var work = await unitOfWork.BeginAsync(cancellationToken);
        await stage();
        var result = await work.CommitAsync(cancellationToken);

        if (result.IsSuccess)
        {
            logger.LogInformation("Development data seeded: {Part}.", part);
        }
        else if (result.Errors[0].Code == PersistenceErrorCodes.Duplicate)
        {
            logger.LogInformation("Another instance seeded the development data first ({Part}); skipped.", part);
        }
        else
        {
            throw new InvalidOperationException($"Seeding development data ({part}) failed: {result.Errors[0].Code}.");
        }
    }

    private static T Must<T>(DomainResult<T> result) =>
        result.IsSuccess ? result.Value : throw new InvalidOperationException($"Seed data is invalid: {result.Error!.Code}.");

    private static void Must(DomainResult result)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"Seed data is invalid: {result.Error!.Code}.");
        }
    }

    private static T Found<T>(T? value, string what)
        where T : class =>
        value ?? throw new InvalidOperationException($"Seed data needs {what}, which was not found.");

    private async Task StageFoundationAsync(CancellationToken cancellationToken)
    {
        var orbitly = Must(Product.Create(
            MarkerProductKey, "Orbitly", "ORB", Must(ProductBranding.Create("Orbitly", null, "#7C3AED", "support@orbitly.example", "support@orbitly.example")), clock));
        var paperplane = Must(Product.Create(
            "paperplane", "Paperplane", "PPL", Must(ProductBranding.Create("Paperplane", null, "#0EA5E9", null, null)), clock));
        products.Add(orbitly);
        products.Add(paperplane);
        products.AddApiKey(Must(ProductApiKey.Create(orbitly.Id, ApiKeyKind.Trusted, "tsk_dev1", "dev-trusted-key-hash-1", "Orbitly server (dev)", clock)));
        products.AddApiKey(Must(ProductApiKey.Create(orbitly.Id, ApiKeyKind.Public, "tsp_dev1", "dev-public-key-hash-1", "Orbitly app (dev)", clock)));
        products.AddApiKey(Must(ProductApiKey.Create(paperplane.Id, ApiKeyKind.Trusted, "tsk_dev2", "dev-trusted-key-hash-2", "Paperplane server (dev)", clock)));

        var sam = Must(Agent.Create("dev|sam", "Sam Whitfield", "sam@example.com", AgentRole.Admin, clock));
        var riley = Must(Agent.Create("dev|riley", "Riley Chen", "riley@example.com", AgentRole.Agent, clock));
        Must(riley.SetPublicDisplayName("Ry"));
        agents.Add(sam);
        agents.Add(riley);
        await agents.SetNotificationPreferenceAsync(new AgentNotificationPreference(riley.Id, orbitly.Id, true), cancellationToken);

        requesters.Add(Must(Requester.Create("ann@example.com", "Ann Lee", "user-1001", clock)));
        requesters.Add(Must(Requester.Create("bo@example.com", "Bo Park", null, clock)));
        requesters.Add(Must(Requester.Create("cy@example.com", null, null, clock)));

        tags.Add(Must(Tag.Create("bug", "Bug", "#DC2626", clock)));
        tags.Add(Must(Tag.Create("billing", "Billing", "#16A34A", clock)));
        tags.Add(Must(Tag.Create("feature-request", "Feature request", "#2563EB", clock)));
    }

    private async Task StageTicketsAndKnowledgeBaseAsync(CancellationToken cancellationToken)
    {
        var orbitly = Found(await products.GetByKeyAsync(MarkerProductKey, cancellationToken), "the Orbitly product");
        var paperplane = Found(await products.GetByKeyAsync("paperplane", cancellationToken), "the Paperplane product");
        var sam = Found(await agents.GetBySubjectAsync("dev|sam", cancellationToken), "agent Sam");
        var riley = Found(await agents.GetBySubjectAsync("dev|riley", cancellationToken), "agent Riley");
        var ann = Found(await requesters.GetByEmailAsync("ann@example.com", cancellationToken), "requester Ann");
        var bo = Found(await requesters.GetByEmailAsync("bo@example.com", cancellationToken), "requester Bo");
        var cy = Found(await requesters.GetByEmailAsync("cy@example.com", cancellationToken), "requester Cy");
        var bug = Found(await tags.GetBySlugAsync("bug", cancellationToken), "the bug tag");
        var billing = Found(await tags.GetBySlugAsync("billing", cancellationToken), "the billing tag");

        var samActor = Actor.ForAgent(sam.Id);
        var rileyActor = Actor.ForAgent(riley.Id);

        var fresh = await NewTicketAsync(orbitly, ann, "Cannot sign in on iOS 19", "<p>The sign in button does nothing since the update.</p>", cancellationToken);
        Must(fresh.AddTag(bug.Id, samActor, clock));
        tickets.Add(fresh);

        var open = await NewTicketAsync(orbitly, bo, "Export to CSV fails", "<p>Exporting my report gives an empty file.</p>", cancellationToken);
        Must(open.Assign(sam.Id, samActor, clock));
        Must(open.ChangeStatus(TicketStatus.Open, samActor, clock));
        Must(open.ChangePriority(TicketPriority.High, samActor, clock));
        tickets.Add(open);

        var pending = await NewTicketAsync(orbitly, cy, "Invoice shows the wrong VAT", "<p>My invoice charges 25 percent VAT.</p>", cancellationToken);
        Must(pending.Assign(riley.Id, rileyActor, clock));
        Must(pending.AddTag(billing.Id, rileyActor, clock));
        Must(pending.AddAgentReply(riley.Id, "<p>Thanks, can you send the invoice number?</p>", clock));
        tickets.Add(pending);

        var solved = await NewTicketAsync(paperplane, ann, "Please add a dark mode", "<p>A dark theme would be easier on my eyes.</p>", cancellationToken);
        Must(solved.AddAgentReply(sam.Id, "<p>Dark mode shipped in 2.4.</p>", clock));
        Must(solved.ChangeStatus(TicketStatus.Solved, samActor, clock));
        tickets.Add(solved);

        var closed = await NewTicketAsync(orbitly, bo, "Password reset email never arrives", "<p>I did not get the reset email.</p>", cancellationToken);
        Must(closed.AddAgentReply(riley.Id, "<p>Fixed on our side, please try again.</p>", clock));
        Must(closed.ChangeStatus(TicketStatus.Solved, rileyActor, clock));
        Must(closed.ChangeStatus(TicketStatus.Closed, Actor.System, clock));
        var followUpNumber = Must(await ToDomainAsync(numbers.AllocateAsync(orbitly.Id, cancellationToken)));
        var followUp = Must(closed.CreateFollowUp(followUpNumber, clock));
        Must(followUp.AddCustomerReply(bo.Id, "<p>It is happening again today.</p>", clock));
        tickets.Add(closed);
        tickets.Add(followUp);

        var spam = await NewTicketAsync(paperplane, cy, "Cheap watches online!!!", "<p>Visit my shop now.</p>", cancellationToken);
        Must(spam.MarkSpam(true, samActor, clock));
        tickets.Add(spam);

        var general = Must(KbCategory.Create(null, "getting-started", "Getting started", 1, clock));
        var account = Must(KbCategory.Create(orbitly.Id, "account", "Account", 2, clock));
        knowledgeBase.AddCategory(general);
        knowledgeBase.AddCategory(account);

        var welcome = Must(KbArticle.Create(null, general.Id, "welcome", "Welcome to support", "How to reach us.", "# Welcome\n\nUse the contact form and we will reply by email.", sam.Id, clock));
        Must(welcome.Publish(clock));
        var reset = Must(KbArticle.Create(orbitly.Id, account.Id, "reset-password", "Reset your password", "Get back into your account.", "# Reset your password\n\n1. Open the app.\n2. Choose **Forgot password**.", sam.Id, clock));
        Must(reset.Publish(clock));
        var export = Must(KbArticle.Create(orbitly.Id, null, "export-csv", "Exporting to CSV", null, "# Exporting\n\nDraft in progress.", riley.Id, clock));
        var pricing = Must(KbArticle.Create(orbitly.Id, null, "old-pricing", "Old pricing", null, "# Pricing\n\nReplaced.", sam.Id, clock));
        Must(pricing.Archive(clock));
        knowledgeBase.AddArticle(welcome);
        knowledgeBase.AddArticle(reset);
        knowledgeBase.AddArticle(export);
        knowledgeBase.AddArticle(pricing);
    }

    private async Task<Ticket> NewTicketAsync(Product product, Requester requester, string subject, string firstMessage, CancellationToken cancellationToken)
    {
        var number = Must(await ToDomainAsync(numbers.AllocateAsync(product.Id, cancellationToken)));
        var ticket = Must(Ticket.Create(number, product.Id, requester.Id, subject, TicketChannel.Web, null, false, clock));
        Must(ticket.AddCustomerReply(requester.Id, firstMessage, clock));
        return ticket;
    }

    private static async Task<DomainResult<TicketNumber>> ToDomainAsync(Task<Result<TicketNumber>> allocation)
    {
        var result = await allocation;
        return result.IsSuccess
            ? DomainResult<TicketNumber>.Ok(result.Value)
            : DomainErrors.Conflict(result.Errors[0].Code, result.Errors[0].Message);
    }
}
