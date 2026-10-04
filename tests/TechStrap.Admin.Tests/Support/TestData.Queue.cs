using SyntaxCircus.Common;
using TechStrap.Contracts.Paging;
using TechStrap.Contracts.Products;
using TechStrap.Contracts.Tags;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Admin.Tests.Support;

/// <summary>Small builders for DTOs and <see cref="Result"/> values, shared by the component tests (one file per feature, all one partial class).</summary>
internal static partial class TestData
{
    public static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);
    public static readonly Guid OrbitlyId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    public static readonly Guid BugTagId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");

    public static Result<T> Ok<T>(T value) => Result<T>.Success(value);

    public static Result Ok() => Result.Success();

    public static Result<T> Fail<T>(string code, string message = "Something went wrong.", ResultErrorKind kind = ResultErrorKind.Failure) =>
        Result<T>.Failure(new ResultError(code, message, kind));

    public static Result Fail(string code, string message = "Something went wrong.", ResultErrorKind kind = ResultErrorKind.Failure) =>
        Result.Failure(new ResultError(code, message, kind));

    public static ProductDto Product(string name = "Orbitly", Guid? id = null) => new(
        id ?? OrbitlyId, name.ToLowerInvariant(), name, name[..3].ToUpperInvariant(), IsActive: true,
        new ProductBrandingDto(name, null, "#1D4ED8", "#FFFFFF", "#1D4ED8", null, null), Version: 1);

    public static TagDto Tag(string name = "bug", Guid? id = null, string colour = "#DC2626") => new(id ?? BugTagId, name, name, colour);

    public static TicketSummaryDto Summary(
        string number = "ORB-1",
        string subject = "Cannot log in",
        string status = TicketStatuses.Open,
        string priority = TicketPriorities.Normal,
        bool isSpam = false,
        string? assignee = null,
        string? requesterName = "Ada Lovelace",
        IReadOnlyList<TicketTagDto>? tags = null) => new(
            Guid.NewGuid(), number, subject, status, priority, OrbitlyId, "Orbitly",
            Guid.NewGuid(), "ada@example.com", requesterName,
            assignee is null ? null : Guid.NewGuid(), assignee,
            isSpam, tags ?? [], Now.AddDays(-1), Now.AddMinutes(-5));

    public static PagedResponse<TicketSummaryDto> Page(IReadOnlyList<TicketSummaryDto> items, int page = 1, int total = -1) =>
        new(items, page, 25, total < 0 ? items.Count : total);

    public static TicketViewCountsResponse Counts(int unassigned = 3, int mine = 2, int open = 5, int pending = 1, int all = 11, int spam = 4) =>
        new(unassigned, mine, open, pending, all, spam);
}
