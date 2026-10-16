using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Features;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Features;
using TechStrap.Contracts.Paging;
using TechStrap.Contracts.Products;

namespace TechStrap.Admin.Tests.Components.Features;

public sealed class FeaturePageTests : AdminComponentTest
{
    private readonly IFeaturesClient _features = Substitute.For<IFeaturesClient>();
    private readonly IProductsClient _products = Substitute.For<IProductsClient>();
    private static readonly Guid ProductId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
    private static readonly Guid FeatureId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    public FeaturePageTests()
    {
        Services.AddSingleton(_features);
        Services.AddSingleton(_products);
        Services.AddSingleton(Substitute.For<IKbClient>());
        _products.ListAsync(Arg.Any<CancellationToken>()).Returns(Result<IReadOnlyList<ProductDto>>.Success([]));
        _features.ListAsync(Arg.Any<Guid?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result<PagedResponse<FeatureSummaryDto>>.Success(new([], 1, 25, 0)));
    }

    [Fact]
    public void Empty_board_offers_creation_and_explains_that_publication_is_curated()
    {
        var cut = Render<FeatureListPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("No features yet"));
        cut.Find("a[href='/features/new']").TextContent.ShouldContain("New feature");
    }

    [Fact]
    public void Failed_list_can_be_reloaded_without_pretending_the_board_is_empty()
    {
        _features.ListAsync(Arg.Any<Guid?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result<PagedResponse<FeatureSummaryDto>>.Failure(new ResultError("api-unavailable", "Unavailable", ResultErrorKind.Failure)));
        var cut = Render<FeatureListPage>();
        cut.WaitForAssertion(() => cut.Find("[role='alert']").TextContent.ShouldContain("could not"));
        cut.Markup.ShouldNotContain("No features yet");
    }

    [Fact]
    public void Published_editor_holds_publication_actions_until_changes_are_saved()
    {
        _features.GetAsync(FeatureId, Arg.Any<CancellationToken>()).Returns(Result<FeatureDto>.Success(new(FeatureId, ProductId, "offline", "Offline mode", "Work without a connection", "Published", "Planned", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 7)));
        _features.TicketsAsync(FeatureId, Arg.Any<CancellationToken>()).Returns(Result<IReadOnlyList<LinkedFeatureTicketDto>>.Success([]));
        var cut = Render<FeatureEditorPage>(p => p.Add(x => x.Id, FeatureId));
        cut.WaitForElement("#feature-title").Change("Offline everywhere");
        cut.Find("button[data-action='publication']").HasAttribute("disabled").ShouldBeTrue();
        cut.Find("button[type='submit']").HasAttribute("disabled").ShouldBeFalse();
    }
    [Fact]
    public void A_stale_save_preserves_edits_and_holds_writes_until_reload()
    {
        var feature = new FeatureDto(FeatureId, ProductId, "offline", "Offline mode", "Work without a connection", "Draft", "Planned", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 7);
        _features.GetAsync(FeatureId, Arg.Any<CancellationToken>()).Returns(Result<FeatureDto>.Success(feature));
        _features.TicketsAsync(FeatureId, Arg.Any<CancellationToken>()).Returns(Result<IReadOnlyList<LinkedFeatureTicketDto>>.Success([]));
        _features.UpdateAsync(FeatureId, Arg.Any<UpdateFeatureRequest>(), Arg.Any<CancellationToken>()).Returns(Result<FeatureDto>.Failure(new ResultError("concurrency-conflict", "Changed", ResultErrorKind.Conflict)));
        var cut = Render<FeatureEditorPage>(p => p.Add(x => x.Id, FeatureId));
        cut.WaitForElement("#feature-title").Change("Offline everywhere");
        cut.Find("form").Submit();
        cut.WaitForAssertion(() => cut.Find("[role='alert']").TextContent.ShouldContain("Reload"));
        cut.Find("#feature-title").GetAttribute("value").ShouldBe("Offline everywhere");
        cut.Find("button[type='submit']").HasAttribute("disabled").ShouldBeTrue();
        cut.Find("button[data-action='publication']").HasAttribute("disabled").ShouldBeTrue();
    }

    [Fact]
    public void Unpublishing_uses_the_saved_version_and_updates_the_visible_state()
    {
        var feature = new FeatureDto(FeatureId, ProductId, "offline", "Offline mode", "Work without a connection", "Published", "Planned", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 7);
        _features.GetAsync(FeatureId, Arg.Any<CancellationToken>()).Returns(Result<FeatureDto>.Success(feature));
        _features.TicketsAsync(FeatureId, Arg.Any<CancellationToken>()).Returns(Result<IReadOnlyList<LinkedFeatureTicketDto>>.Success([]));
        _features.PublishAsync(FeatureId, 7, false, Arg.Any<CancellationToken>()).Returns(Result<FeatureDto>.Success(feature with { PublicationState = "Draft", Version = 8 }));
        var cut = Render<FeatureEditorPage>(p => p.Add(x => x.Id, FeatureId));
        cut.WaitForElement("button[data-action='publication']").Click();
        cut.WaitForAssertion(() => cut.Find("[role='status']").TextContent.ShouldContain("unpublished"));
        cut.Find("button[data-action='publication']").TextContent.ShouldBe("Publish");
    }

    [Fact]
    public void Failed_link_search_can_recover_without_retaining_a_false_failure_message()
    {
        _features.ListAsync(ProductId, Arg.Any<string?>(), Arg.Is<string?>(s => s == null), 1, Arg.Any<CancellationToken>())
            .Returns(Result<PagedResponse<FeatureSummaryDto>>.Failure(new ResultError("api-unavailable", "Unavailable", ResultErrorKind.Failure)), Result<PagedResponse<FeatureSummaryDto>>.Success(new([], 1, 25, 0)));
        var cut = Render<FeatureTicketLinkPanel>(p => p.Add(x => x.TicketId, Guid.NewGuid()).Add(x => x.ProductId, ProductId));
        cut.WaitForElement("[role='alert']");
        cut.FindAll("button").Single(b => b.TextContent == "Search").Click();
        cut.WaitForAssertion(() => cut.FindAll("[role='alert']").ShouldBeEmpty());
    }    [Fact]
    public async Task Navigating_to_another_feature_during_a_save_cannot_apply_the_old_response()
    {
        var secondId = Guid.Parse("cccccccc-0000-0000-0000-000000000002");
        var first = new FeatureDto(FeatureId, ProductId, "offline", "Offline mode", "First feature", "Draft", "Planned", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 7);
        var second = first with { Id = secondId, Slug = "export", Title = "Export data", DescriptionMarkdown = "Second feature" };
        _features.GetAsync(FeatureId, Arg.Any<CancellationToken>()).Returns(Result<FeatureDto>.Success(first));
        _features.GetAsync(secondId, Arg.Any<CancellationToken>()).Returns(Result<FeatureDto>.Success(second));
        _features.TicketsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Result<IReadOnlyList<LinkedFeatureTicketDto>>.Success([]));
        var save = new TaskCompletionSource<Result<FeatureDto>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _features.UpdateAsync(FeatureId, Arg.Any<UpdateFeatureRequest>(), Arg.Any<CancellationToken>()).Returns(save.Task);
        var cut = Render<FeatureEditorPage>(p => p.Add(x => x.Id, FeatureId));
        cut.WaitForElement("#feature-title").Change("Edited first feature");
        var pending = cut.Find("form").SubmitAsync();
        cut.Render(p => p.Add(x => x.Id, secondId));
        save.SetResult(Result<FeatureDto>.Success(first with { Title = "Edited first feature", Version = 8 }));
        await pending;
        cut.WaitForAssertion(() => cut.Find("#feature-title").GetAttribute("value").ShouldBe("Export data"));
    }    [Fact]
    public void Closed_linked_requests_show_their_link_without_offering_an_impossible_unlink()
    {
        _features.GetAsync(FeatureId, Arg.Any<CancellationToken>()).Returns(Result<FeatureDto>.Success(new(FeatureId, ProductId, "offline", "Offline mode", "First feature", "Draft", "Planned", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 7)));
        _features.TicketsAsync(FeatureId, Arg.Any<CancellationToken>()).Returns(Result<IReadOnlyList<LinkedFeatureTicketDto>>.Success([new(Guid.NewGuid(), "ORB-10", "Offline please", "Closed", 12)]));
        var cut = Render<FeatureEditorPage>(p => p.Add(x => x.Id, FeatureId));
        var unlink = cut.WaitForElement("section[aria-label='Linked requests'] button");
        unlink.HasAttribute("disabled").ShouldBeTrue();
        cut.Find("section[aria-label='Linked requests']").TextContent.ShouldContain("Closed requests are read-only");
    }}





