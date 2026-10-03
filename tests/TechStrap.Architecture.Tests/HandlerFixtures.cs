using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TechStrap.Infrastructure;

namespace TechStrap.Architecture.Tests;

/// <summary>
/// Deliberately bad (and a few good) types that live only in this test assembly. The rule tests feed
/// them to HandlerRules to prove each rule can fail. The real scans look at the Application and
/// Api assemblies only, so these are never picked up as production code.
/// </summary>
public static class HandlerFixtures
{
    public sealed record SampleRequest(string Name);

    public interface ISampleRepository;

    public sealed class FixtureEntity
    {
        public int Id { get; set; }
    }

    // ---- shape fixtures ----

    public interface IGoodSampleHandler
    {
        Task HandleAsync(SampleRequest request, CancellationToken cancellationToken);
    }

    public sealed class GoodSampleHandler(ISampleRepository repository) : IGoodSampleHandler
    {
        public ISampleRepository Repository { get; } = repository;

        public Task HandleAsync(SampleRequest request, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    public interface IUnsealedHandler
    {
        Task HandleAsync(SampleRequest request, CancellationToken cancellationToken);
    }

    public class UnsealedHandler : IUnsealedHandler
    {
        public Task HandleAsync(SampleRequest request, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    public sealed class NoInterfaceHandler
    {
        public Task HandleAsync(SampleRequest request, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    public interface INoCancellationHandler
    {
        Task HandleAsync(SampleRequest request);
    }

    public sealed class NoCancellationHandler : INoCancellationHandler
    {
        public Task HandleAsync(SampleRequest request) => Task.CompletedTask;
    }

    // ---- constructor dependency fixtures ----

    public sealed class DbContextHandler(DbContext context)
    {
        public DbContext Context { get; } = context;
    }

    public sealed class DbSetHandler(DbSet<FixtureEntity> entities)
    {
        public DbSet<FixtureEntity> Entities { get; } = entities;
    }

    public sealed class HttpContextHandler(HttpContext context)
    {
        public HttpContext Context { get; } = context;
    }

    public sealed class HttpContextAccessorHandler(IHttpContextAccessor accessor)
    {
        public IHttpContextAccessor Accessor { get; } = accessor;
    }

    public sealed class ActionResultHandler(IActionResult result)
    {
        public IActionResult Result { get; } = result;
    }

    public sealed class ControllerBaseHandler(ControllerBase controller)
    {
        public ControllerBase Controller { get; } = controller;
    }

    public sealed class ConcreteInfrastructureHandler(InfrastructureAssemblyMarker infrastructure)
    {
        public InfrastructureAssemblyMarker Infrastructure { get; } = infrastructure;
    }

    public sealed class NestedGenericDependencyHandler(IEnumerable<DbSet<FixtureEntity>> entities)
    {
        public IEnumerable<DbSet<FixtureEntity>> Entities { get; } = entities;
    }

    /// <summary>Stands in for a persistence record such as TicketRecord (the real ones are internal to Infrastructure).</summary>
    public sealed class TicketRecord
    {
        public int Id { get; set; }
    }

    public sealed class RecordHandler(TicketRecord record)
    {
        public TicketRecord Record { get; } = record;
    }

    public sealed class NestedRecordHandler(IReadOnlyList<TicketRecord> records)
    {
        public IReadOnlyList<TicketRecord> Records { get; } = records;
    }

    // ---- controller fixtures ----

    public sealed class GoodController : ControllerBase
    {
        public Task<IActionResult> Create(SampleRequest request, [FromServices] IGoodSampleHandler handler, CancellationToken ct) =>
            Task.FromResult<IActionResult>(Ok());
    }

    public sealed class ConstructorInjectedController(IGoodSampleHandler handler) : ControllerBase
    {
        public IGoodSampleHandler Handler { get; } = handler;
    }

    public sealed class MissingFromServicesController : ControllerBase
    {
        public Task<IActionResult> Create(SampleRequest request, IGoodSampleHandler handler, CancellationToken ct) =>
            Task.FromResult<IActionResult>(Ok());
    }

    public sealed class ConcreteHandlerController : ControllerBase
    {
        public Task<IActionResult> Create(SampleRequest request, [FromServices] GoodSampleHandler handler, CancellationToken ct) =>
            Task.FromResult<IActionResult>(Ok());
    }
}
