using TechStrap.Api.Startup;

namespace TechStrap.Api.Tests;

public sealed class ApplicationHandlerRegistrationTests
{
    [Fact]
    public void Worker_only_handlers_are_not_registered_in_the_api()
    {
        var handlers = ApplicationHandlerRegistration.Handlers().ToList();

        handlers.Select(pair => pair.Contract.Name).ShouldNotContain(name => ApplicationHandlerRegistration.WorkerOnly.Contains(name));
        handlers.Count.ShouldBeGreaterThanOrEqualTo(18);
    }
}
