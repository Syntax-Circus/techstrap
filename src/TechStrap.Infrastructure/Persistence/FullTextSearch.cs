using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;

namespace TechStrap.Infrastructure.Persistence;

/// <summary>
/// Full-text search settings (D-011, D-027). The vectors are Postgres STORED generated columns, so the database keeps them in
/// step with their row: no triggers and no application-maintained columns.
/// </summary>
internal static class FullTextSearch
{
    /// <summary>The text search configuration (Assumption A-07: English only; an i18n seam).</summary>
    public const string Config = "english";

    public const char WeightA = 'A';
    public const char WeightB = 'B';
    public const char WeightC = 'C';

    /// <summary>
    /// Maps a tsvector property to a stored generated column built from weighted text columns, e.g.
    /// <c>setweight(to_tsvector('english', coalesce(title, '')), 'A') || ...</c>, plus a GIN index on it.
    /// Npgsql's <c>HasGeneratedTsVectorColumn</c> would be the shorter spelling, but it takes no per-column weights, and the
    /// ranking rule (title above summary above body, subject above message body) needs them.
    /// </summary>
    public static void HasWeightedSearchVector<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        Expression<Func<TEntity, NpgsqlTsVector>> property,
        params (string Column, char Weight)[] parts)
        where TEntity : class
    {
        var expression = string.Join(
            " || ",
            parts.Select(part => $"setweight(to_tsvector('{Config}', coalesce({part.Column}, '')), '{part.Weight}')"));

        builder.Property(property).HasColumnType("tsvector").HasComputedColumnSql(expression, stored: true);
        builder.HasIndex(((MemberExpression)property.Body).Member.Name).HasMethod("GIN");
    }
}
