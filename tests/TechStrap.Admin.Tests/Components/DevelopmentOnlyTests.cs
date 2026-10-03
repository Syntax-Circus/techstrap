using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using TechStrap.Admin.Components.Ui;

namespace TechStrap.Admin.Tests.Components;

public sealed class DevelopmentOnlyTests : BunitContext
{
    private sealed class FakeEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "TechStrap.Admin";

        public string ContentRootPath { get; set; } = string.Empty;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    [Fact]
    public void Renders_its_content_in_Development()
    {
        Services.AddSingleton<IHostEnvironment>(new FakeEnvironment(Environments.Development));

        var cut = Render<DevelopmentOnly>(p => p.AddChildContent("<p id=\"inside\">style guide</p>"));

        cut.Find("#inside").TextContent.ShouldBe("style guide");
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("")]
    public void Renders_nothing_and_signals_not_found_outside_Development(string environment)
    {
        Services.AddSingleton<IHostEnvironment>(new FakeEnvironment(environment));
        var navigation = Services.GetRequiredService<NavigationManager>();
        var notFound = 0;
        navigation.OnNotFound += (_, _) => notFound++;

        var cut = Render<DevelopmentOnly>(p => p.AddChildContent("<p id=\"inside\">style guide</p>"));

        cut.FindAll("#inside").ShouldBeEmpty();
        cut.Markup.Trim().ShouldBeEmpty();
        notFound.ShouldBe(1);
    }
}
