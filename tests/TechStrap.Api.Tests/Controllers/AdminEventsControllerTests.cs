using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Api.Controllers;
using TechStrap.Application.AdminEvents;
using TechStrap.Contracts.AdminEvents;
using TechStrap.Contracts.Paging;

namespace TechStrap.Api.Tests.Controllers;

public sealed class AdminEventsControllerTests
{
    [Fact]
    public async Task List_DelegatesFiltersAndPassesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var actor = Guid.CreateVersion7();
        var asOf = new DateTimeOffset(2026, 10, 3, 9, 0, 0, TimeSpan.Zero);
        var page = new PagedResponse<AdminEventDto>([], 2, 10, 0);
        var handler = Substitute.For<IListAdminEventsRequestHandler>();
        handler.HandleAsync("Tag", actor, asOf, 2, 10, cancellation.Token).Returns(Result<PagedResponse<AdminEventDto>>.Success(page));

        var result = await ControllerTestContext.For<AdminEventsController>().List(handler, cancellation.Token, "Tag", actor, asOf, 2, 10);

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBe(page);
        await handler.Received(1).HandleAsync("Tag", actor, asOf, 2, 10, cancellation.Token);
    }
}
