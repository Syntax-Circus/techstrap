namespace TechStrap.Architecture.Tests.ContractNamingFixtureTypes;

public sealed record GoodWidgetDto(Guid Id);

public sealed record GoodCreateWidgetRequest(string Name);

public sealed record GoodCreateWidgetResponse(Guid Id);

public sealed record GoodPageResponse<T>(IReadOnlyList<T> Items);

public static class GoodWidgetConstants
{
    public const string Kind = "widget";
}

public sealed record BadWidget(Guid Id);

public enum BadWidgetKind
{
    One,
}

public sealed class BadWidgetModel;
