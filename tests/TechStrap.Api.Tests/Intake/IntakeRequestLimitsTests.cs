using TechStrap.Api.Startup;
using TechStrap.Contracts.Intake;

namespace TechStrap.Api.Tests.Intake;

/// <summary>The Portal's form size limit is Contracts' <see cref="IntakeLimits.FormBodyBytes"/>; the API's own endpoint limit must be the same number, so the Portal never accepts a body the API would refuse (or the reverse).</summary>
public sealed class IntakeRequestLimitsTests
{
    [Fact]
    public void The_api_form_limit_is_the_contract_limit()
    {
        IntakeRequestLimits.FormBodyBytes.ShouldBe(IntakeLimits.FormBodyBytes);
    }
}
