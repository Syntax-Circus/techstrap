using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using TechStrap.Infrastructure;

namespace TechStrap.Architecture.Tests;

/// <summary>Deliberately bad (and one good) abstractions and persistence-record look-alikes, used only to prove the rules can fail.</summary>
public static class AbstractionFixtures
{
    public sealed class FixtureRow
    {
        public int Id { get; set; }
    }

    /// <summary>Named like a persistence record; as a type it is what the rules must keep out of Domain and Application.</summary>
    public sealed class WidgetRecord
    {
        public int Id { get; set; }
    }

    public interface IGoodStore
    {
        Task<IReadOnlyList<string>> ListAsync(Guid id, CancellationToken cancellationToken);

        void Add(string item);
    }

    public interface IExposesDbContext
    {
        DbContext Context { get; }
    }

    public interface IExposesDbSet
    {
        Task<DbSet<FixtureRow>> LoadAsync(CancellationToken cancellationToken);
    }

    public interface IExposesQueryable
    {
        IQueryable<string> Query();
    }

    public interface IExposesNestedQueryable
    {
        Task<List<IQueryable<string>>> LoadAsync(CancellationToken cancellationToken);
    }

    public interface IExposesHttpContext
    {
        void Use(HttpContext context);
    }

    public interface IExposesInfrastructure
    {
        InfrastructureAssemblyMarker Marker { get; }
    }

    public interface IExposesRecord
    {
        Task<WidgetRecord?> FindAsync(Guid id, CancellationToken cancellationToken);
    }

    public interface IMissingCancellationToken
    {
        Task LoadAsync(Guid id);
    }

    public interface ICancellationTokenNotLast
    {
        Task LoadAsync(CancellationToken cancellationToken, Guid id);
    }

    // Domain/Application look-alikes for the record rule.

    public sealed class HoldsRecordInField
    {
#pragma warning disable CS0169
        private WidgetRecord? _record;
#pragma warning restore CS0169
    }

    public sealed class ReturnsRecord
    {
        public WidgetRecord Get() => new();
    }

    public sealed class TakesRecordInConstructor(WidgetRecord record)
    {
        public int Id { get; } = record.Id;
    }

    public sealed class HoldsRecordsInGenericProperty
    {
        public IReadOnlyList<WidgetRecord> Items { get; } = [];
    }

    public sealed class Innocent
    {
        public string Name { get; } = string.Empty;
    }
}
