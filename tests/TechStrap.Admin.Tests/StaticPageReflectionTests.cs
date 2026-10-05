using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using TechStrap.Admin.Components.Layout;

namespace TechStrap.Admin.Tests;

/// <summary>
/// The authoritative half of the static-page rule (PHASE-07c T1). A page with [ExcludeFromInteractiveRouting] has no circuit and no
/// GET /api/agents/me, so AgentGate skips the NoAccess check for it. Reflection reads what the compiler really emitted, so it cannot be fooled by
/// the way the attribute is spelled; the text rule in TechStrap.Architecture.Tests is the early, readable gate.
/// </summary>
public sealed class StaticPageReflectionTests
{
    private static readonly string[] StaticPageNames =
    [
        "TechStrap.Admin.Components.Pages.Error",
        "TechStrap.Admin.Components.Pages.NotFound",
        "TechStrap.Admin.Components.Pages.StyleGuide",
    ];

    /// <summary>Routable pages that are deliberately anonymous without being static. Empty today (the signed-out landing page is not routable).</summary>
    private static readonly string[] AnonymousRoutablePages = [];

    private static Type[] AdminTypes() => typeof(AgentGate).Assembly.GetTypes();

    [Fact]
    public void Exactly_Error_NotFound_and_StyleGuide_are_excluded_from_interactive_routing()
    {
        var excluded = AdminTypes()
            .Where(t => t.IsDefined(typeof(ExcludeFromInteractiveRoutingAttribute), inherit: true))
            .Select(t => t.FullName!)
            .Order(StringComparer.Ordinal)
            .ToList();

        excluded.ShouldBe(StaticPageNames.Order(StringComparer.Ordinal).ToList());
    }

    [Fact]
    public void Each_static_page_is_anonymous_on_the_type_itself()
    {
        foreach (var name in StaticPageNames)
        {
            var type = AdminTypes().SingleOrDefault(t => t.FullName == name);

            type.ShouldNotBeNull(name);
            type.IsDefined(typeof(AllowAnonymousAttribute), inherit: false).ShouldBeTrue($"{name} must be [AllowAnonymous]");
        }
    }

    [Fact]
    public void No_other_routable_page_is_anonymous()
    {
        var routable = AdminTypes().Where(t => t.IsDefined(typeof(RouteAttribute), inherit: false)).ToList();

        routable.Count.ShouldBeGreaterThan(StaticPageNames.Length, "the scan must see the data pages as well as the static ones");
        var anonymous = routable
            .Where(t => t.IsDefined(typeof(AllowAnonymousAttribute), inherit: true))
            .Select(t => t.FullName!)
            .Except(StaticPageNames)
            .Except(AnonymousRoutablePages)
            .ToList();

        anonymous.ShouldBeEmpty("a data page must require the agent session");
    }
}
