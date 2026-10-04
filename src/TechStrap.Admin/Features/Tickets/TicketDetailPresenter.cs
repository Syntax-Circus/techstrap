using System.Text.Json;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Admin.Features.Tickets;

/// <summary>
/// Assembles the ticket screen's model: the ticket, the products, agents and tags its ids resolve against, and (for a follow-up) the parent's number.
/// It exists because the assembly is asynchronous and uses four clients (PHASE-07 allows exactly this presenter, the timeline factory and the audit summary factory).
/// The ticket's own failure wins over a lookup failure, so a missing ticket is reported as missing. Components never call the clients for lookups themselves.
/// </summary>
public sealed class TicketDetailPresenter(ITicketsClient tickets, IProductsClient products, IAgentsClient agents, ITagsClient tags)
{
    /// <summary>Metadata is caller-controlled, so what is shown is bounded: at most this many items.</summary>
    public const int MaxMetadataItems = 50;

    /// <summary>...and each value is cut to this many characters (then an ellipsis).</summary>
    public const int MaxMetadataValueLength = 500;

    private const string Ellipsis = "\u2026";

    /// <param name="reference">The ticket id or number.</param>
    /// <param name="reusableLookups">The lookups of the model already on screen. A silent refresh passes them so only the ticket is fetched again; the first load and an explicit Retry pass null.</param>
    public Task<Result<TicketDetailViewModel>> LoadAsync(string reference, CancellationToken cancellationToken) => LoadAsync(reference, null, cancellationToken);

    public async Task<Result<TicketDetailViewModel>> LoadAsync(string reference, TicketLookups? reusableLookups, CancellationToken cancellationToken)
    {
        if (reusableLookups is not null)
        {
            var reused = await tickets.GetAsync(reference, cancellationToken);
            if (reused.IsFailure)
            {
                return Result<TicketDetailViewModel>.Failure(reused.Errors[0]);
            }

            var reusedParent = await LoadParentNumberAsync(reused.Value.ParentTicketId, cancellationToken);
            return Result<TicketDetailViewModel>.Success(Map(reused.Value, reusableLookups, reusedParent));
        }

        var detailTask = tickets.GetAsync(reference, cancellationToken);
        var productsTask = products.ListAsync(cancellationToken);
        var agentsTask = agents.ListAllAsync(cancellationToken);
        var tagsTask = tags.ListAsync(cancellationToken);
        await Task.WhenAll(detailTask, productsTask, agentsTask, tagsTask);

        var detail = detailTask.Result;
        if (detail.IsFailure)
        {
            return Result<TicketDetailViewModel>.Failure(detail.Errors[0]);
        }

        foreach (var lookup in new Result[] { productsTask.Result.ToResult(), agentsTask.Result.ToResult(), tagsTask.Result.ToResult() })
        {
            if (lookup.IsFailure)
            {
                // A lookup that 404s must not read as "ticket not found" (the page maps NotFound to that view), so it becomes a plain failure.
                var error = lookup.Errors[0];
                return Result<TicketDetailViewModel>.Failure(error.Kind == ResultErrorKind.NotFound ? new ResultError(error.Code, error.Message, ResultErrorKind.Failure) : error);
            }
        }

        var lookups = new TicketLookups(productsTask.Result.Value, agentsTask.Result.Value, tagsTask.Result.Value);
        var ticket = detail.Value;
        var parentNumber = await LoadParentNumberAsync(ticket.ParentTicketId, cancellationToken);
        return Result<TicketDetailViewModel>.Success(Map(ticket, lookups, parentNumber));
    }

    // One extra read. A parent that was deleted (or is not visible) simply shows no link.
    private async Task<string?> LoadParentNumberAsync(Guid? parentId, CancellationToken cancellationToken)
    {
        if (parentId is null)
        {
            return null;
        }

        var parent = await tickets.GetAsync(parentId.Value.ToString(), cancellationToken);
        return parent.IsSuccess ? parent.Value.Number : null;
    }

    private static TicketDetailViewModel Map(TicketDetailDto ticket, TicketLookups lookups, string? parentNumber) => new(
        ticket.Id,
        ticket.Number,
        ticket.Subject,
        ticket.Status,
        ticket.Priority,
        ticket.IsSpam,
        ticket.ProductId,
        ticket.ProductName,
        ticket.AssigneeId,
        ticket.AssigneeName,
        ticket.Tags,
        ticket.Requester,
        ticket.Channel,
        ticket.CreatedAt,
        ticket.ParentTicketId,
        parentNumber,
        ReadMetadata(ticket.MetadataJson, ticket.MetadataTrusted),
        ticket.RowVersion,
        TimelineEntryFactory.Build(ticket.Messages, ticket.Events, lookups),
        lookups);

    private static string Cap(string value) =>
        value.Length <= MaxMetadataValueLength ? value : value[..MaxMetadataValueLength] + Ellipsis;

    private static MetadataViewModel? ReadMetadata(string? json, bool trusted)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new MetadataViewModel(trusted, Readable: false, []);
            }

            var items = document.RootElement.EnumerateObject()
                .Take(MaxMetadataItems)
                .Select(p => new MetadataItem(p.Name, Cap(p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() ?? string.Empty : p.Value.GetRawText())))
                .ToList();
            return items.Count == 0 ? null : new MetadataViewModel(trusted, Readable: true, items);
        }
        catch (JsonException)
        {
            return new MetadataViewModel(trusted, Readable: false, []);
        }
    }
}

internal static class ResultConversions
{
    /// <summary>Drops the value so differently-typed results can be checked in one loop.</summary>
    public static Result ToResult<T>(this Result<T> result) =>
        result.IsSuccess ? Result.Success() : Result.Failure(result.Errors[0]);
}
