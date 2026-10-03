using TechStrap.Architecture.Tests.ContractNamingFixtureTypes;
using TechStrap.Contracts.Paging;

namespace TechStrap.Architecture.Tests;

public sealed class ContractNamingTests
{
    [Fact]
    public void Every_public_contract_type_ends_in_Dto_Request_or_Response()
    {
        var types = typeof(PagedResponse<>).Assembly.GetTypes();

        ContractNamingRules.FindViolations(types).ShouldBeEmpty();
    }

    [Fact]
    public void The_contract_scan_is_not_vacuous()
    {
        typeof(PagedResponse<>).Assembly.GetTypes()
            .Count(type => type.IsPublic && type.Name.EndsWith("Dto", StringComparison.Ordinal))
            .ShouldBeGreaterThanOrEqualTo(8);
    }

    [Fact]
    public void Well_named_types_and_static_constant_classes_pass()
    {
        ContractNamingRules.FindViolations(
            [typeof(GoodWidgetDto), typeof(GoodCreateWidgetRequest), typeof(GoodCreateWidgetResponse), typeof(GoodPageResponse<>), typeof(GoodWidgetConstants)])
            .ShouldBeEmpty();
    }

    [Theory]
    [InlineData(typeof(BadWidget))]
    [InlineData(typeof(BadWidgetKind))]
    [InlineData(typeof(BadWidgetModel))]
    public void A_type_without_a_contract_suffix_is_flagged(Type type) =>
        ContractNamingRules.FindViolations([type]).ShouldHaveSingleItem().ShouldContain(type.Name);
}
