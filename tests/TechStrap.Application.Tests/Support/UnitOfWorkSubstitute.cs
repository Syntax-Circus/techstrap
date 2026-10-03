using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Application.Persistence;

namespace TechStrap.Application.Tests.Support;

/// <summary>An IUnitOfWork whose scopes commit with the given results in order (success when the list runs out).</summary>
public static class UnitOfWorkSubstitute
{
    public static IUnitOfWork Create(params Result[] commits)
    {
        var queue = new Queue<Result>(commits);
        var unitOfWork = Substitute.For<IUnitOfWork>();
        unitOfWork.BeginAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            var scope = Substitute.For<IUnitOfWorkScope>();
            scope.CommitAsync(Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult(queue.Count > 0 ? queue.Dequeue() : Result.Success()));
            return Task.FromResult(scope);
        });
        return unitOfWork;
    }

    public static Result Conflict(string code) =>
        Result.Failure(new ResultError(code, "The change conflicts with the stored data.", ResultErrorKind.Conflict));
}
