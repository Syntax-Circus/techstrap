using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SyntaxCircus.Common;
using TechStrap.Application.Persistence;
using TechStrap.Domain.Requesters;
using TechStrap.Domain.Tickets;
using TechStrap.Infrastructure.Persistence.Records;

namespace TechStrap.Infrastructure.IntegrationTests;

public sealed class RequesterAndTagRepositoryTests(PostgresFixture postgres) : PostgresIntegrationTestBase(postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_requester_is_found_by_email_in_any_letter_case()
    {
        await using var host = new PersistenceTestHost(Database);
        var requester = Requester.Create("Ann@Example.com", "Ann", "user-7", host.Clock).Value;
        await host.CommitAsync(sp => { sp.GetRequiredService<IRequesterRepository>().Add(requester); return Task.CompletedTask; });

        var found = await host.ReadAsync(sp => sp.GetRequiredService<IRequesterRepository>().GetByEmailAsync("  ANN@EXAMPLE.COM ", Ct));

        found.ShouldNotBeNull();
        found.Id.ShouldBe(requester.Id);
        found.ExternalUserRef.ShouldBe("user-7");
        (await host.ReadAsync(sp => sp.GetRequiredService<IRequesterRepository>().GetByEmailAsync("other@example.com", Ct))).ShouldBeNull();
        (await host.ReadAsync(sp => sp.GetRequiredService<IRequesterRepository>().GetByIdAsync(requester.Id, Ct))).ShouldNotBeNull();
    }

    [Fact]
    public async Task The_database_treats_emails_that_differ_only_by_case_as_the_same_requester()
    {
        await using var context = Database.CreateDbContext();
        context.Set<RequesterRecord>().Add(new RequesterRecord { Id = Guid.CreateVersion7(), Email = "ann@example.com" });
        await context.SaveChangesAsync(Ct);
        context.Set<RequesterRecord>().Add(new RequesterRecord { Id = Guid.CreateVersion7(), Email = "ANN@example.com" });

        await Should.ThrowAsync<DbUpdateException>(async () => await context.SaveChangesAsync(Ct));
    }

    [Fact]
    public async Task A_second_requester_with_the_same_email_is_a_duplicate_conflict()
    {
        await using var host = new PersistenceTestHost(Database);
        await host.CommitAsync(sp => { sp.GetRequiredService<IRequesterRepository>().Add(Requester.Create("ann@example.com", null, null, host.Clock).Value); return Task.CompletedTask; });

        var result = await host.CommitAsync(sp => { sp.GetRequiredService<IRequesterRepository>().Add(Requester.Create("Ann@Example.com", null, null, host.Clock).Value); return Task.CompletedTask; });

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(PersistenceErrorCodes.Duplicate);
    }

    [Fact]
    public async Task Erasing_a_requester_persists_the_anonymised_values()
    {
        await using var host = new PersistenceTestHost(Database);
        var requester = Requester.Create("ann@example.com", "Ann", "user-7", host.Clock).Value;
        await host.CommitAsync(sp => { sp.GetRequiredService<IRequesterRepository>().Add(requester); return Task.CompletedTask; });

        await host.CommitAsync(async sp =>
        {
            var repository = sp.GetRequiredService<IRequesterRepository>();
            var loaded = (await repository.GetByIdAsync(requester.Id, Ct))!;
            loaded.Erase(host.Clock);
            repository.Update(loaded);
        });

        var reloaded = (await host.ReadAsync(sp => sp.GetRequiredService<IRequesterRepository>().GetByIdAsync(requester.Id, Ct)))!;
        reloaded.IsErased.ShouldBeTrue();
        reloaded.Email.ShouldBe($"erased-{requester.Id}@invalid");
        reloaded.Name.ShouldBeNull();
        reloaded.ExternalUserRef.ShouldBeNull();
        (await host.ReadAsync(sp => sp.GetRequiredService<IRequesterRepository>().GetByEmailAsync("ann@example.com", Ct))).ShouldBeNull();
    }

    [Fact]
    public async Task A_stale_profile_update_loaded_before_an_erasure_conflicts_and_leaves_the_row_erased()
    {
        await using var host = new PersistenceTestHost(Database);
        var requester = Requester.Create("ann@example.com", "Ann", "user-7", host.Clock).Value;
        await host.CommitAsync(sp => { sp.GetRequiredService<IRequesterRepository>().Add(requester); return Task.CompletedTask; });
        Requester stale;
        await using (var a = host.CreateScope())
        {
            stale = (await a.ServiceProvider.GetRequiredService<IRequesterRepository>().GetByIdAsync(requester.Id, Ct))!;
        }

        await host.CommitAsync(async sp =>
        {
            var repository = sp.GetRequiredService<IRequesterRepository>();
            var loaded = (await repository.GetByIdAsync(requester.Id, Ct))!;
            loaded.Erase(host.Clock);
            repository.Update(loaded);
        });

        await using var c = host.CreateScope();
        var cRepository = c.ServiceProvider.GetRequiredService<IRequesterRepository>();
        await cRepository.GetByIdAsync(requester.Id, Ct);
        stale.UpdateProfile("Ann Again", "user-8").IsSuccess.ShouldBeTrue();
        await using var work = await c.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync(Ct);
        cRepository.Update(stale);
        var result = await work.CommitAsync(Ct);

        var error = result.Errors.ShouldHaveSingleItem();
        error.Kind.ShouldBe(ResultErrorKind.Conflict);
        error.Code.ShouldBe(PersistenceErrorCodes.ConcurrencyConflict);
        var reloaded = (await host.ReadAsync(sp => sp.GetRequiredService<IRequesterRepository>().GetByIdAsync(requester.Id, Ct)))!;
        reloaded.IsErased.ShouldBeTrue();
        reloaded.Name.ShouldBeNull();
        reloaded.ExternalUserRef.ShouldBeNull();
    }

    [Fact]
    public async Task A_profile_update_from_an_earlier_scope_saves_when_the_row_did_not_change()
    {
        await using var host = new PersistenceTestHost(Database);
        var requester = Requester.Create("ann@example.com", "Ann", "user-7", host.Clock).Value;
        await host.CommitAsync(sp => { sp.GetRequiredService<IRequesterRepository>().Add(requester); return Task.CompletedTask; });
        Requester held;
        await using (var a = host.CreateScope())
        {
            held = (await a.ServiceProvider.GetRequiredService<IRequesterRepository>().GetByIdAsync(requester.Id, Ct))!;
        }

        var result = await host.CommitAsync(async sp =>
        {
            var repository = sp.GetRequiredService<IRequesterRepository>();
            await repository.GetByIdAsync(requester.Id, Ct);
            held.UpdateProfile("Ann Again", "user-8").IsSuccess.ShouldBeTrue();
            repository.Update(held);
        });

        result.IsSuccess.ShouldBeTrue();
        (await host.ReadAsync(sp => sp.GetRequiredService<IRequesterRepository>().GetByIdAsync(requester.Id, Ct)))!.Name.ShouldBe("Ann Again");
    }

    [Fact]
    public async Task Tags_are_found_by_slug_listed_by_name_and_updated()
    {
        await using var host = new PersistenceTestHost(Database);
        var billing = Tag.Create("billing", "Billing", "#aa00ff", host.Clock).Value;
        var bug = Tag.Create("bug", "Bug", "#ff0000", host.Clock).Value;
        await host.CommitAsync(sp =>
        {
            var tags = sp.GetRequiredService<ITagRepository>();
            tags.Add(bug);
            tags.Add(billing);
            return Task.CompletedTask;
        });
        await host.CommitAsync(async sp =>
        {
            var tags = sp.GetRequiredService<ITagRepository>();
            var loaded = (await tags.GetBySlugAsync("bug", Ct))!;
            loaded.Update("Defect", "#00ff00");
            tags.Update(loaded);
        });

        var list = await host.ReadAsync(sp => sp.GetRequiredService<ITagRepository>().ListAsync(Ct));

        list.Select(t => t.Name).ShouldBe(["Billing", "Defect"]);
        list[1].Colour.ShouldBe("#00FF00");
        (await host.ReadAsync(sp => sp.GetRequiredService<ITagRepository>().GetByIdAsync(billing.Id, Ct)))!.Slug.ShouldBe("billing");
        (await host.ReadAsync(sp => sp.GetRequiredService<ITagRepository>().GetBySlugAsync("missing", Ct))).ShouldBeNull();
    }

    [Fact]
    public async Task A_duplicate_tag_slug_is_a_conflict()
    {
        await using var host = new PersistenceTestHost(Database);
        await host.CommitAsync(sp => { sp.GetRequiredService<ITagRepository>().Add(Tag.Create("bug", "Bug", "#FF0000", host.Clock).Value); return Task.CompletedTask; });

        var result = await host.CommitAsync(sp => { sp.GetRequiredService<ITagRepository>().Add(Tag.Create("bug", "Other", "#FF0000", host.Clock).Value); return Task.CompletedTask; });

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(PersistenceErrorCodes.Duplicate);
    }

    [Fact]
    public async Task An_unused_tag_can_be_removed()
    {
        await using var host = new PersistenceTestHost(Database);
        var tag = Tag.Create("bug", "Bug", "#FF0000", host.Clock).Value;
        await host.CommitAsync(sp => { sp.GetRequiredService<ITagRepository>().Add(tag); return Task.CompletedTask; });

        var removed = await host.CommitAsync(async sp =>
        {
            var tags = sp.GetRequiredService<ITagRepository>();
            tags.Remove((await tags.GetByIdAsync(tag.Id, Ct))!);
        });

        removed.IsSuccess.ShouldBeTrue();
        (await host.ReadAsync(sp => sp.GetRequiredService<ITagRepository>().ListAsync(Ct))).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_tag_that_tickets_still_carry_cannot_be_removed_and_commit_reports_a_reference_conflict()
    {
        await using var host = new PersistenceTestHost(Database);
        var tag = Tag.Create("bug", "Bug", "#FF0000", host.Clock).Value;
        await host.CommitAsync(sp => { sp.GetRequiredService<ITagRepository>().Add(tag); return Task.CompletedTask; });
        await using (var context = Database.CreateDbContext())
        {
            var product = await RecordSeed.ProductAsync(context);
            var requester = await RecordSeed.RequesterAsync(context);
            var ticket = await RecordSeed.TicketAsync(context, product, requester, "ACME-1");
            context.Set<TicketTagRecord>().Add(new TicketTagRecord { TicketId = ticket.Id, TagId = tag.Id });
            await context.SaveChangesAsync(Ct);
        }

        var result = await host.CommitAsync(async sp =>
        {
            var tags = sp.GetRequiredService<ITagRepository>();
            tags.Remove((await tags.GetByIdAsync(tag.Id, Ct))!);
        });

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(PersistenceErrorCodes.ReferenceViolation);
        result.Errors[0].Kind.ShouldBe(ResultErrorKind.Conflict);
        (await host.ReadAsync(sp => sp.GetRequiredService<ITagRepository>().GetByIdAsync(tag.Id, Ct))).ShouldNotBeNull();
    }
}
