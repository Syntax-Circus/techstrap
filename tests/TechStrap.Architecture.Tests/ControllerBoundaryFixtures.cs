using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace TechStrap.Architecture.Tests.ControllerBoundaryFixtureTypes;

public interface IFixtureRequestHandler
{
    Task<int> HandleAsync(CancellationToken cancellationToken);
}

public interface IOtherFixtureRequestHandler
{
    Task<int> HandleAsync(CancellationToken cancellationToken);
}

public interface IFixtureRepository;

[Authorize(Policy = "Agent")]
public sealed class GoodBoundaryController : ControllerBase
{
    public Task<IActionResult> Get([FromServices] IFixtureRequestHandler handler, CancellationToken cancellationToken) => Task.FromResult<IActionResult>(Ok());
}

public sealed class NoPolicyController : ControllerBase
{
    public Task<IActionResult> Get([FromServices] IFixtureRequestHandler handler, CancellationToken cancellationToken) => Task.FromResult<IActionResult>(Ok());
}

[Authorize(Policy = "Agent")]
public sealed class RepositoryParameterController : ControllerBase
{
    public Task<IActionResult> Get([FromServices] IFixtureRequestHandler handler, [FromServices] IFixtureRepository repository, CancellationToken cancellationToken) =>
        Task.FromResult<IActionResult>(Ok());
}

[Authorize(Policy = "Agent")]
public sealed class TwoHandlersController : ControllerBase
{
    public Task<IActionResult> Get([FromServices] IFixtureRequestHandler first, [FromServices] IOtherFixtureRequestHandler second, CancellationToken cancellationToken) =>
        Task.FromResult<IActionResult>(Ok());
}

[Authorize(Policy = "Agent")]
public sealed class NoCancellationController : ControllerBase
{
    public Task<IActionResult> Get([FromServices] IFixtureRequestHandler handler) => Task.FromResult<IActionResult>(Ok());
}

[Authorize(Policy = "Agent")]
public sealed class ConstructorDependencyController(IFixtureRequestHandler handler) : ControllerBase
{
    public Task<IActionResult> Get([FromServices] IFixtureRequestHandler other, CancellationToken cancellationToken) => Task.FromResult<IActionResult>(Ok(handler));
}

public sealed class FixtureRecord;

[Authorize(Policy = "Agent")]
public sealed class UnitOfWorkParameterController : ControllerBase
{
    public Task<IActionResult> Get([FromServices] IFixtureRequestHandler handler, [FromServices] TechStrap.Application.Persistence.IUnitOfWork unitOfWork, CancellationToken cancellationToken) =>
        Task.FromResult<IActionResult>(Ok());
}

[Authorize(Policy = "Agent")]
public sealed class RecordParameterController : ControllerBase
{
    public Task<IActionResult> Get([FromServices] IFixtureRequestHandler handler, FixtureRecord record, CancellationToken cancellationToken) =>
        Task.FromResult<IActionResult>(Ok());
}

[Authorize(Policy = "Agent")]
public sealed class DbContextParameterController : ControllerBase
{
    public Task<IActionResult> Get([FromServices] IFixtureRequestHandler handler, [FromServices] Microsoft.EntityFrameworkCore.DbContext context, CancellationToken cancellationToken) =>
        Task.FromResult<IActionResult>(Ok());
}

[Authorize(Policy = "Agent")]
public sealed class InfrastructureParameterController : ControllerBase
{
    public Task<IActionResult> Get([FromServices] IFixtureRequestHandler handler, [FromServices] TechStrap.Infrastructure.InfrastructureAssemblyMarker infrastructure, CancellationToken cancellationToken) =>
        Task.FromResult<IActionResult>(Ok());
}
