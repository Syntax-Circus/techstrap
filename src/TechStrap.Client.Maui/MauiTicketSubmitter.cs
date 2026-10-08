using SyntaxCircus.Common;
using TechStrap.Contracts.Intake;

namespace TechStrap.Client.Maui;

internal sealed class MauiTicketSubmitter(ITechStrapClient client, IDeviceContextCollector collector) : IMauiTicketSubmitter
{
    public Task<Result<SubmitTicketResponse>> SubmitAsync(MauiTicketDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var merged = MauiMetadataMerger.Merge(collector.Collect(), draft.Metadata);
        if (!merged.IsSuccess)
        {
            return Task.FromResult(Result<SubmitTicketResponse>.Failure(merged.Errors[0]));
        }

        var request = new SubmitTicketRequest(draft.RequesterEmail, draft.RequesterName, draft.Subject, draft.Message, null, merged.Value);
        return draft.IdempotencyKey is null
            ? client.SubmitTicketAsync(request, cancellationToken)
            : client.SubmitTicketAsync(request, draft.IdempotencyKey, cancellationToken);
    }
}
