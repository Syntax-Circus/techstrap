using Microsoft.AspNetCore.Components;
using TechStrap.Portal.Forms;
using TechStrap.Portal.Products;

namespace TechStrap.Portal.Components.Pages;

/// <summary>
/// The confirmation after the contact form (P09-T06). The ticket number arrives in a protected, 10-minute <c>?ref=</c> value (<see cref="ReceivedReference"/>). A value that is missing, expired, tampered with or
/// not a ticket number is not an error: the page shows its generic confirmation without a number, so a visitor who reloads late, or opens the address from history, still reads something true. The link to the ticket
/// is never shown here: it is proved by owning the mailbox.
/// </summary>
public partial class ContactReceived : ProductPageBase
{
    [SupplyParameterFromQuery(Name = "ref")]
    private string? Reference { get; set; }

    [Inject]
    private ReceivedReference References { get; set; } = default!;

    private string? TicketNumber { get; set; }

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        if (Theme is not null && References.TryUnprotect(Reference, out var number))
        {
            TicketNumber = number;
        }
    }
}
