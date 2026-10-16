using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Storage;
using SyntaxCircus.Common;
using TechStrap.Client.Maui;
using TechStrap.Contracts.Intake;

namespace TechStrap.Client.Maui.Tests.Snippets;

/// <summary>The code blocks of the TechStrap.Client.Maui README, compiled so they cannot rot. Never called; ReadmeSnippets.Tests.ps1 checks the README against the regions.</summary>
internal static class MauiReadmeSnippets
{
    public static async Task SubmitAttachmentAsync(IMauiTicketSubmitter submitter, FileResult selectedFile, string submissionKey, CancellationToken ct)
    {
        #region readme:maui-attachments
        var attachment = selectedFile.ToTicketAttachment("text/plain");
        var draft = new MauiTicketDraft("Sync failed", "See the attached log.", "user@example.com")
        {
            IdempotencyKey = submissionKey,
            Attachments = new[] { attachment },
        };
        var result = await submitter.SubmitAsync(draft, ct);
        #endregion
    }

    public static void Register(IServiceCollection services)
    {
        #region readme:maui-register
        services.AddTechStrapMaui(
            client =>
            {
                client.BaseAddress = new Uri("https://support.example.com/");
                client.ApiKey = "<public key from configuration>";
            },
            context =>
            {
                context.IncludeDisplay = true;
            });
        #endregion
    }

    public static async Task SubmitAsync(IServiceProvider services, CancellationToken ct)
    {
        #region readme:maui-submit
        var submitter = services.GetRequiredService<IMauiTicketSubmitter>();
        Result<SubmitTicketResponse> result = await submitter.SubmitAsync(
            new MauiTicketDraft("Crash on startup", "Steps: open the app, tap Sync.", "user@example.com")
            {
                IdempotencyKey = Guid.NewGuid().ToString("N"),
            },
            ct);

        if (!result.IsSuccess)
        {
            // result.Errors holds the code and message of each failure (see the error codes in TechStrap.Client).
        }
        #endregion
    }
}
