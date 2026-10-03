using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Api.Controllers;
using TechStrap.Application.Tags;
using TechStrap.Contracts.Tags;

namespace TechStrap.Api.Tests.Controllers;

public sealed class TagsControllerTests
{
    private static readonly TagDto Tag = new(Guid.CreateVersion7(), "bug", "Bug", "#DC2626");

    [Fact]
    public async Task List_DelegatesAndPassesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        IReadOnlyList<TagDto> tags = [Tag];
        var handler = Substitute.For<IListTagsRequestHandler>();
        handler.HandleAsync(cancellation.Token).Returns(Result<IReadOnlyList<TagDto>>.Success(tags));

        var result = await ControllerTestContext.For<TagsController>().List(handler, cancellation.Token);

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBe(tags);
        await handler.Received(1).HandleAsync(cancellation.Token);
    }

    [Fact]
    public async Task Create_DelegatesAndReturns201WithTheTag()
    {
        using var cancellation = new CancellationTokenSource();
        var request = new CreateTagRequest("bug", "Bug", "#DC2626");
        var handler = Substitute.For<ICreateTagRequestHandler>();
        handler.HandleAsync(request, cancellation.Token).Returns(Result<TagDto>.Success(Tag));

        var result = await ControllerTestContext.For<TagsController>().Create(request, handler, cancellation.Token);

        var objectResult = result.ShouldBeOfType<ObjectResult>();
        objectResult.StatusCode.ShouldBe(201);
        objectResult.Value.ShouldBe(Tag);
        await handler.Received(1).HandleAsync(request, cancellation.Token);
    }

    [Fact]
    public async Task Update_DelegatesTheIdAndBodyAndPassesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var request = new UpdateTagRequest("Bug", "#DC2626");
        var handler = Substitute.For<IUpdateTagRequestHandler>();
        handler.HandleAsync(Tag.Id, request, cancellation.Token).Returns(Result<TagDto>.Success(Tag));

        var result = await ControllerTestContext.For<TagsController>().Update(Tag.Id, request, handler, cancellation.Token);

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBe(Tag);
        await handler.Received(1).HandleAsync(Tag.Id, request, cancellation.Token);
    }

    [Fact]
    public async Task Delete_DelegatesTheIdAndForceFlagAndReturnsNoContent()
    {
        using var cancellation = new CancellationTokenSource();
        var handler = Substitute.For<IDeleteTagRequestHandler>();
        handler.HandleAsync(Tag.Id, true, cancellation.Token).Returns(Result.Success());

        var result = await ControllerTestContext.For<TagsController>().Delete(Tag.Id, handler, cancellation.Token, force: true);

        result.ShouldBeOfType<NoContentResult>();
        await handler.Received(1).HandleAsync(Tag.Id, true, cancellation.Token);
    }
}
