using TechStrap.Contracts.Intake;

namespace TechStrap.Client.Tests.Snippets;

internal static class AttachmentReadmeSnippets
{
    public static async Task SubmitAsync(ITechStrapClient client, SubmitTicketRequest request, CancellationToken ct)
    {
        #region readme:client-attachments
        var attachments = new[] { TicketAttachment.FromFile("trace.log", "text/plain") };
        // Persist this key for this logical ticket, including later caller retries.
        var submissionKey = Guid.NewGuid().ToString("N");
        var uploaded = await client.SubmitTicketAsync(request, attachments, submissionKey, ct);
        #endregion
    }
}
