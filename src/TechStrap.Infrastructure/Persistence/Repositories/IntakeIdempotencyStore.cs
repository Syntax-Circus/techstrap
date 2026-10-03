using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using TechStrap.Application.Persistence;
using TechStrap.Infrastructure.Persistence.Records;

namespace TechStrap.Infrastructure.Persistence.Repositories;

/// <summary>Idempotency-Key store (D-020). Only the SHA-256 of a key is ever persisted.</summary>
internal sealed class IntakeIdempotencyStore(TechStrapDbContext context) : IIntakeIdempotencyStore
{
    public async Task<IntakeIdempotencyEntry?> FindAsync(Guid apiKeyId, string idempotencyKey, CancellationToken cancellationToken)
    {
        var keyHash = HashKey(idempotencyKey);
        var record = await context.Set<IntakeIdempotencyKeyRecord>()
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.ApiKeyId == apiKeyId && r.KeyHash == keyHash, cancellationToken);
        return record is null ? null : new IntakeIdempotencyEntry(record.Id, record.ApiKeyId, record.TicketId, record.Response, record.CreatedAt);
    }

    public void Add(Guid apiKeyId, string idempotencyKey, Guid ticketId, string responseJson, DateTimeOffset createdAt) =>
        context.Set<IntakeIdempotencyKeyRecord>().Add(new IntakeIdempotencyKeyRecord
        {
            Id = Guid.CreateVersion7(createdAt),
            ApiKeyId = apiKeyId,
            KeyHash = HashKey(idempotencyKey),
            TicketId = ticketId,
            Response = responseJson,
            CreatedAt = createdAt,
        });

    public void Remove(IntakeIdempotencyEntry entry) =>
        context.Set<IntakeIdempotencyKeyRecord>().Remove(new IntakeIdempotencyKeyRecord
        {
            Id = entry.Id,
            ApiKeyId = entry.ApiKeyId,
            TicketId = entry.TicketId,
            CreatedAt = entry.CreatedAt,
        });

    public async Task<int> PruneAsync(DateTimeOffset olderThan, int limit, CancellationToken cancellationToken)
    {
        var ids = await context.Set<IntakeIdempotencyKeyRecord>()
            .Where(r => r.CreatedAt < olderThan)
            .OrderBy(r => r.CreatedAt)
            .Select(r => r.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
        if (ids.Count == 0)
        {
            return 0;
        }

        return await context.Set<IntakeIdempotencyKeyRecord>()
            .Where(r => ids.Contains(r.Id))
            .ExecuteDeleteAsync(cancellationToken);
    }

    private static string HashKey(string idempotencyKey) =>
        "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(idempotencyKey)));
}
