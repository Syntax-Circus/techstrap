using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SyntaxCircus.Common;
using TechStrap.Client;
using TechStrap.Contracts.Intake;

// Configuration comes from appsettings.json, TECHSTRAP__* environment variables and the two switches below.
var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddCommandLine(args, new Dictionary<string, string>
{
    ["--base-address"] = "TechStrap:BaseAddress",
    ["--api-key"] = "TechStrap:ApiKey",
});

#region readme:client-register
builder.Services.AddTechStrapClient(builder.Configuration.GetSection(TechStrapClientDefaults.ConfigurationSection));
#endregion

using var host = builder.Build();
var client = host.Services.GetRequiredService<ITechStrapClient>();

try
{
    #region readme:client-submit
    var request = new SubmitTicketRequest(
        Email: "ana@example.com",
        Name: "Ana",
        Subject: "Printer on floor 2 is offline",
        Body: "It stopped answering this morning.",
        ExternalUserRef: null,
        Metadata: null);

    // A stable key makes a retry safe: the same key never creates a second ticket.
    Result<SubmitTicketResponse> result = await client.SubmitTicketAsync(request, Guid.NewGuid().ToString("N"));
    #endregion

    #region readme:client-errors
    if (result.IsSuccess)
    {
        Console.WriteLine($"Ticket {result.Value.TicketNumber}");
        Console.WriteLine($"View: {result.Value.ViewUrl}");
        return 0;
    }

    foreach (var error in result.Errors)
    {
        Console.Error.WriteLine($"{error.Code}: {error.Message}");
    }

    return 1;
    #endregion
}
catch (OptionsValidationException ex)
{
    // The options are validated the first time the client is used; the messages name the key, never the API key.
    foreach (var failure in ex.Failures)
    {
        Console.Error.WriteLine($"Configuration problem: {failure}");
    }

    return 2;
}
