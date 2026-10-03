using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace TechStrap.Infrastructure.Persistence.Configurations;

internal static class ConfigurationExtensions
{
    /// <summary>Postgres <c>xmin</c> as the optimistic concurrency token (D-026). It is a system column, so migrations never create it.</summary>
    public static PropertyBuilder<uint> HasXminConcurrencyToken<TEntity>(this EntityTypeBuilder<TEntity> builder, Expression<Func<TEntity, uint>> property)
        where TEntity : class =>
        builder.Property(property).IsRowVersion();

    /// <summary>Stores an enum as its name in a text column.</summary>
    public static PropertyBuilder<TEnum> HasEnumAsString<TEnum>(this PropertyBuilder<TEnum> property, int maxLength = 32)
        where TEnum : struct, Enum =>
        property.HasConversion<string>().HasMaxLength(maxLength);
}
