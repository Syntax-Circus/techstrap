using TechStrap.Contracts.Skins;
using TechStrap.Domain.Rules;

namespace TechStrap.Application.Tests.Skins;

/// <summary>Contracts cannot reference Domain, so the shared skin limits are pinned here: the two copies must agree (D-053).</summary>
public sealed class SkinLimitsParityTests
{
    [Fact]
    public void The_skin_json_limit_matches_the_domain_limit() =>
        SkinRules.MaxJsonLength.ShouldBe(DomainLimits.SkinJsonMaxLength);

    [Fact]
    public void Every_pack_key_fits_the_domain_pack_key_limit() =>
        SkinPacks.All.ShouldAllBe(pack => pack.Key.Length <= DomainLimits.PackKeyMaxLength);

    [Fact]
    public void Every_pack_key_is_lower_case_ascii_letters() =>
        SkinPacks.All.ShouldAllBe(pack => pack.Key.All(c => c >= 'a' && c <= 'z'));
}
