using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TechStrap.Application.Tickets.AutoClose;
using TechStrap.Worker.AutoClose;

namespace TechStrap.Api.Tests.Worker;

/// <summary>
/// The P05-T12 boundary rule, applied to the auto-close loop: the Worker loop class reaches data only through the handler it resolves from a fresh scope.
/// Lives in Api.Tests because Api.Tests already references the Worker host (ReferenceRules only checks src projects).
/// </summary>
public sealed class AutoCloseWorkerBoundaryTests
{
    [Fact]
    public void The_worker_loop_depends_only_on_the_scope_factory_options_clock_and_logger()
    {
        var constructor = typeof(AutoCloseWorker).GetConstructors().ShouldHaveSingleItem();

        var parameterTypes = constructor.GetParameters().Select(parameter => parameter.ParameterType).ToArray();

        parameterTypes.ShouldBe(
            [typeof(IServiceScopeFactory), typeof(IOptions<AutoCloseOptions>), typeof(TimeProvider), typeof(ILogger<AutoCloseWorker>)],
            ignoreOrder: true);
    }

    [Fact]
    public void The_worker_loop_has_no_repository_store_or_db_context_dependency()
    {
        var constructorTypes = typeof(AutoCloseWorker).GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType.Name);
        var fieldTypes = typeof(AutoCloseWorker)
            .GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
            .Select(field => field.FieldType.Name);

        var offenders = constructorTypes.Concat(fieldTypes)
            .Where(name => (name.StartsWith('I') && (name.EndsWith("Repository", StringComparison.Ordinal) || name.EndsWith("Store", StringComparison.Ordinal)))
                || name.EndsWith("DbContext", StringComparison.Ordinal))
            .ToArray();

        offenders.ShouldBeEmpty();
    }
}
