using TechStrap.Domain.Rules;

namespace TechStrap.Domain.Requesters;

/// <summary>A customer. No account: identified by a case-insensitive email, optionally linked to the product app's user id.</summary>
public sealed class Requester
{
    public const string ErasedEmailDomain = "invalid";

    private Requester(Guid id, string email, string? name, string? externalUserRef, DateTimeOffset? erasedAt, uint version)
    {
        Id = id;
        Email = email;
        Name = name;
        ExternalUserRef = externalUserRef;
        ErasedAt = erasedAt;
        Version = version;
    }

    public Guid Id { get; }

    /// <summary>Lower-cased. After erasure it is <c>erased-{id}@invalid</c>.</summary>
    public string Email { get; private set; }

    public string? Name { get; private set; }

    /// <summary>The product app's own user id, set only by Trusted keys.</summary>
    public string? ExternalUserRef { get; private set; }

    public DateTimeOffset? ErasedAt { get; private set; }

    /// <summary>The row version this object was loaded at (0 when new). Persistence uses it to detect a concurrent change.</summary>
    public uint Version { get; }

    public bool IsErased => ErasedAt is not null;

    public static DomainResult<Requester> Create(string? email, string? name, string? externalUserRef, TimeProvider clock)
    {
        var address = Guard.Email(email, "email");
        var requesterName = Guard.OptionalText(name, DomainLimits.NameMaxLength, "name");
        var reference = Guard.OptionalText(externalUserRef, DomainLimits.NameMaxLength * 2, "external-user-ref");

        return Guard.FirstError(address, requesterName, reference) is { } error
            ? error
            : DomainResult<Requester>.Ok(new Requester(EntityId.New(clock), address.Value, requesterName.Value, reference.Value, null, 0));
    }

    public static Requester Restore(Guid id, string email, string? name, string? externalUserRef, DateTimeOffset? erasedAt, uint version) =>
        new(id, email, name, externalUserRef, erasedAt, version);

    public DomainResult UpdateProfile(string? name, string? externalUserRef)
    {
        if (IsErased)
        {
            return DomainErrors.Conflict("requester-erased", "An erased requester cannot be changed.");
        }

        var requesterName = Guard.OptionalText(name, DomainLimits.NameMaxLength, "name");
        var reference = Guard.OptionalText(externalUserRef, DomainLimits.NameMaxLength * 2, "external-user-ref");
        if (Guard.FirstError(requesterName, reference) is { } error)
        {
            return error;
        }

        Name = requesterName.Value;
        ExternalUserRef = reference.Value;
        return DomainResult.Ok();
    }

    /// <summary>Anonymizes the requester (D-006). Idempotent: a second call keeps the first erasure time.</summary>
    public void Erase(TimeProvider clock)
    {
        Email = $"erased-{Id}@{ErasedEmailDomain}";
        Name = null;
        ExternalUserRef = null;
        ErasedAt ??= DomainTime.Now(clock);
    }
}
