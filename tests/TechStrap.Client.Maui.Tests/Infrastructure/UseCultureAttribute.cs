using System.Globalization;
using System.Reflection;
using Xunit.v3;

namespace TechStrap.Client.Maui.Tests.Infrastructure;

/// <summary>Pins the current culture for each test in the class (or the one method) and restores it afterwards, so a host without LANG (an empty culture name) behaves like a developer machine.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
internal sealed class UseCultureAttribute(string cultureName) : BeforeAfterTestAttribute
{
    private CultureInfo? _originalCulture;
    private CultureInfo? _originalUiCulture;

    public override void Before(MethodInfo methodUnderTest, IXunitTest test)
    {
        _originalCulture = CultureInfo.CurrentCulture;
        _originalUiCulture = CultureInfo.CurrentUICulture;
        var culture = CultureInfo.GetCultureInfo(cultureName);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    public override void After(MethodInfo methodUnderTest, IXunitTest test)
    {
        if (_originalCulture is not null)
        {
            CultureInfo.CurrentCulture = _originalCulture;
        }

        if (_originalUiCulture is not null)
        {
            CultureInfo.CurrentUICulture = _originalUiCulture;
        }
    }
}
