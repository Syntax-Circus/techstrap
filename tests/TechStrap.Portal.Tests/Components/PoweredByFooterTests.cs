using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TechStrap.Portal.Components.Ui;

namespace TechStrap.Portal.Tests.Components;

public sealed class PoweredByFooterTests : BunitContext
{
    [Fact]
    public void Shows_a_plain_link_to_the_GitHub_repository_with_the_16px_head_mark_by_default()
    {
        Services.AddSingleton(Options.Create(new PoweredByOptions()));

        var cut = Render<PoweredByFooter>();

        var link = cut.Find("footer.ts-powered a");
        link.GetAttribute("href").ShouldBe("https://github.com/Syntax-Circus/techstrap");
        link.TextContent.ShouldBe("TechStrap");
        cut.Find("footer.ts-powered").TextContent.Trim().ShouldBe("Powered by TechStrap");
        var mark = cut.Find("footer.ts-powered img");
        mark.GetAttribute("src").ShouldBe("brand/mark.svg");
        mark.GetAttribute("width").ShouldBe("16");
        mark.GetAttribute("alt").ShouldBe(string.Empty);
    }

    [Fact]
    public void Renders_nothing_when_the_installation_setting_hides_it()
    {
        Services.AddSingleton(Options.Create(new PoweredByOptions { Show = false }));

        Render<PoweredByFooter>().Markup.Trim().ShouldBeEmpty();
    }
}
