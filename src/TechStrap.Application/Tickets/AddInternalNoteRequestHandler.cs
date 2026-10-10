using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Content;
using TechStrap.Application.Intake;
using TechStrap.Application.Persistence;
using TechStrap.Application.Results;
using TechStrap.Contracts.Tickets;
using TechStrap.Domain.Rules;

namespace TechStrap.Application.Tickets;

public interface IAddInternalNoteRequestHandler
{
    Task<Result<AgentMessageResponse>> HandleAsync(Guid ticketId, AddInternalNoteRequest request, CancellationToken cancellationToken);
}

/// <summary>An agent-only note: Markdown rendered then sanitized, never emailed, never changes status or first-response time.</summary>
public sealed class AddInternalNoteRequestHandler(
    ICurrentAgentClaims currentAgent,
    IAgentRepository agents,
    ITicketRepository tickets,
    IMarkdownRenderer markdown,
    IHtmlSanitizer sanitizer,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : IAddInternalNoteRequestHandler
{
    public async Task<Result<AgentMessageResponse>> HandleAsync(Guid ticketId, AddInternalNoteRequest request, CancellationToken cancellationToken)
    {
        if (request.Body is { } rawBody && rawBody.Length > DomainLimits.MessageBodyMaxLength)
        {
            return Fail(IntakeErrors.BodyTooLong());
        }

        await using var scope = await unitOfWork.BeginAsync(cancellationToken);

        var loaded = await TicketMutation.LoadAsync(ticketId, request.RowVersion, rowVersionRequired: false, currentAgent, agents, tickets, cancellationToken);
        if (loaded.IsFailure)
        {
            return Fail(loaded.Errors[0]);
        }

        var (agent, ticket) = loaded.Value;

        var html = sanitizer.Sanitize(markdown.ToHtml(request.Body ?? string.Empty));
        var message = ticket.AddInternalNote(agent.Id, html, clock);
        if (message.IsFailure)
        {
            return Fail(message.Error!.ToError());
        }

        // Staging the ticket accepts its pending changes, so the DTO is built first.
        var dto = new MessageDto(
            message.Value.Id, message.Value.AuthorType.ToWire(), message.Value.AuthorId, agent.Name ?? agent.Email, message.Value.Visibility.ToWire(),
            message.Value.Body, message.Value.CreatedAt, [], []);

        tickets.Update(ticket);

        var committed = await TicketMutation.CommitAsync(scope, ticket.Id, tickets, cancellationToken);
        return committed.IsFailure
            ? Fail(committed.Errors[0])
            : Result<AgentMessageResponse>.Success(new AgentMessageResponse(dto, committed.Value));
    }

    private static Result<AgentMessageResponse> Fail(ResultError error) => Result<AgentMessageResponse>.Failure(error);
}
