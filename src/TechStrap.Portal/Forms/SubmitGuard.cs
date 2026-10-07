using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using SyntaxCircus.Common;
using TechStrap.Portal.Clients;

namespace TechStrap.Portal.Forms;

/// <summary>Where a visitor goes after a guarded write. A null <see cref="Path"/> means "nowhere: show the confirmation in place" (a follow-up whose link could not be read).</summary>
public readonly record struct SubmitTarget(string? Path)
{
    public static SubmitTarget None { get; } = new(null);

    /// <summary>A path can be another ticket's access token (a follow-up), so it never prints.</summary>
    public override string ToString() => "[target]";
}

/// <summary>
/// What a guarded form post is identified by: the form's name, what the post is about (the product key, or the ticket's access token) and the id the form carried, hashed together. Only the hash is kept, so the
/// cache never holds a token, and an id made for one form can never claim another form, product or ticket.
/// </summary>
public readonly record struct SubmitKey
{
    private SubmitKey(string hash) => Hash = hash;

    public string Hash { get; }

    /// <summary>The key for a post, or null when the id is missing or malformed (the post is then not guarded and goes through as it always did).</summary>
    /// <remarks>A product key is case-insensitive (<paramref name="foldScope"/>, the default); a ticket's access token is not, so the reply form passes false and the token is hashed as it is.</remarks>
    public static SubmitKey? TryCreate(string form, string scope, string? id, bool foldScope = true)
    {
        if (!SubmitIds.IsWellFormed(id))
        {
            return null;
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{form}\0{(foldScope ? scope.ToLowerInvariant() : scope)}\0{id}"));
        return new SubmitKey(Convert.ToBase64String(bytes));
    }
}

public enum SubmitStatus
{
    /// <summary>The write succeeded (this request's, or the first request's that this one repeats), or the first request's result is unknown and this is the fallback: go to <see cref="SubmitOutcome.Target"/>.</summary>
    Done,

    /// <summary>The write failed; <see cref="SubmitOutcome.Errors"/> says how, exactly as an unguarded call would have.</summary>
    Failed,

    /// <summary>Only for the request that made the write: it timed out, so nobody knows whether it happened. The page shows its calm "unavailable" notice, and a repeat of the id is sent to the fallback instead of writing again.</summary>
    Unknown,
}

public sealed record SubmitOutcome(SubmitStatus Status, SubmitTarget Target, IReadOnlyList<ResultError> Errors)
{
    /// <summary>The target can hold another ticket's access token, so a log call that formats an outcome never prints it.</summary>
    public override string ToString() => $"SubmitOutcome {{ Status = {Status} }}";

    internal static SubmitOutcome Done(SubmitTarget target) => new(SubmitStatus.Done, target, []);

    internal static SubmitOutcome Failed(IReadOnlyList<ResultError> errors) => new(SubmitStatus.Failed, default, errors);

    internal static SubmitOutcome Unknown { get; } = new(SubmitStatus.Unknown, default, []);
}

/// <summary>
/// Makes one visitor's double click send once (D-045 09d addendum, PHASE-09 T09). The first post with an id claims it, atomically, and does the write; a repeat of the id never writes: it waits for the first's answer
/// when that is still running, or reads the stored one, and goes where the first goes (or to a fallback when the answer is unknown).
/// <list type="bullet">
/// <item>The write runs on its own token with a timeout, never on the request's: a real double click aborts the first POST, but the API may already have the ticket, and a request that stops waiting could also lose the
/// files it is reading (a precaution, not reproduced). So the first request waits for its own write without its abort token (bounded by the timeout), and the write cannot be cut short by the browser.</item>
/// <item>A failure releases the claim, so a retry writes. A repeat that was already waiting gets the same failure, never a second write. A timeout or a fault keeps the claim as "unknown".</item>
/// <item>The cache is this class's own <see cref="MemoryCache"/> with a size cap, never the shared one (the sitemap cache lives there and has no size). A claim lives <see cref="Lifetime"/>, shorter than the
/// reference it may hold (<see cref="ReceivedReference.Lifetime"/>). When the cap is reached a new post is simply not guarded (the old behaviour), never refused.</item>
/// <item>The stored target can hold another ticket's access token (a follow-up). It lives in memory only, for <see cref="Lifetime"/>, and this class never logs and never takes a logger.</item>
/// </list>
/// Per instance: a restart or a second Portal replica forgets the claims (documented in PORTAL-APP.md, like the sitemap cache).
/// </summary>
public sealed class SubmitGuard : IDisposable
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(6);

    public static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(ApiClientRegistration.WriteTimeoutSeconds);

    public const int MaxClaims = 10_000;

    private readonly MemoryCache _claims;
    private readonly TimeSpan _lifetime;
    private readonly TimeSpan _writeTimeout;
    private readonly Lock _gate = new();

    public SubmitGuard()
        : this(Lifetime, WriteTimeout, MaxClaims)
    {
    }

    public SubmitGuard(TimeSpan lifetime, TimeSpan writeTimeout, int maxClaims)
    {
        _lifetime = lifetime;
        _writeTimeout = writeTimeout;
        _claims = new MemoryCache(new MemoryCacheOptions { SizeLimit = maxClaims, ExpirationScanFrequency = TimeSpan.FromSeconds(15) });
    }

    /// <summary>
    /// Runs <paramref name="write"/> at most once for <paramref name="key"/>. With no key (a missing or malformed id) it is the old behaviour: the write runs on <paramref name="requestAborted"/>, unguarded.
    /// <paramref name="fallback"/> is where a repeat goes when the first answer is unknown.
    /// </summary>
    public async Task<SubmitOutcome> RunAsync(SubmitKey? key, SubmitTarget fallback, Func<CancellationToken, Task<Result<SubmitTarget>>> write, CancellationToken requestAborted)
    {
        ArgumentNullException.ThrowIfNull(write);
        if (key is not { } submitKey)
        {
            return From(await write(requestAborted));
        }

        Claim? existing;
        Claim? mine = null;
        lock (_gate)
        {
            if (!_claims.TryGetValue(submitKey.Hash, out existing))
            {
                var claim = new Claim();
                using (var entry = _claims.CreateEntry(submitKey.Hash))
                {
                    entry.Value = claim;
                    entry.Size = 1;
                    entry.Priority = CacheItemPriority.NeverRemove;
                    entry.AbsoluteExpirationRelativeToNow = _lifetime;
                }

                // Over the cap the cache keeps nothing: this post goes unguarded rather than being refused.
                mine = _claims.TryGetValue(submitKey.Hash, out Claim? stored) && ReferenceEquals(stored, claim) ? claim : null;
            }
        }

        if (existing is not null)
        {
            var earlier = await existing.Done.Task.WaitAsync(requestAborted);
            return earlier.Status switch
            {
                AttemptStatus.Succeeded => SubmitOutcome.Done(earlier.Target),
                AttemptStatus.Failed => SubmitOutcome.Failed(earlier.Errors),
                _ => SubmitOutcome.Done(fallback),
            };
        }

        if (mine is null)
        {
            return From(await write(requestAborted));
        }

        var attempt = await WriteAsync(submitKey, mine, write);
        return attempt.Status switch
        {
            AttemptStatus.Succeeded => SubmitOutcome.Done(attempt.Target),
            AttemptStatus.Failed => SubmitOutcome.Failed(attempt.Errors),
            _ when attempt.Fault is { } fault => Rethrow(fault),
            _ => SubmitOutcome.Unknown,
        };
    }

    public void Dispose() => _claims.Dispose();

    private static SubmitOutcome From(Result<SubmitTarget> result) => result.IsSuccess ? SubmitOutcome.Done(result.Value) : SubmitOutcome.Failed(result.Errors);

    private static SubmitOutcome Rethrow(Exception fault)
    {
        ExceptionDispatchInfo.Capture(fault).Throw();
        return SubmitOutcome.Unknown;
    }

    private async Task<Attempt> WriteAsync(SubmitKey key, Claim claim, Func<CancellationToken, Task<Result<SubmitTarget>>> write)
    {
        Attempt attempt;
        using var timeout = new CancellationTokenSource(_writeTimeout);
        try
        {
            var result = await write(timeout.Token);
            attempt = result.IsSuccess ? new Attempt(AttemptStatus.Succeeded, result.Value, [], null) : new Attempt(AttemptStatus.Failed, default, result.Errors, null);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            attempt = new Attempt(AttemptStatus.Unknown, default, [], null);
        }
        catch (Exception ex)
        {
            attempt = new Attempt(AttemptStatus.Unknown, default, [], ex);
        }

        if (attempt.Status == AttemptStatus.Failed)
        {
            // Released before anyone is told, so a request that arrives after this finds no claim and may write. Every failure releases, a 5xx or a transport error too: holding the claim would send a refresh to a
            // "received" page for a write that may not have happened. Accepted (D-045): a 5xx after which the API had in fact applied the write can still lead to a duplicate on a deliberate retry.
            try
            {
                lock (_gate)
                {
                    _claims.Remove(key.Hash);
                }
            }
            finally
            {
                // Waiting repeats are always released, even if the cache is already disposed at shutdown.
                claim.Done.TrySetResult(attempt);
            }

            return attempt;
        }

        claim.Done.TrySetResult(attempt);
        return attempt;
    }

    private enum AttemptStatus
    {
        Succeeded,
        Failed,
        Unknown,
    }

    private sealed record Attempt(AttemptStatus Status, SubmitTarget Target, IReadOnlyList<ResultError> Errors, Exception? Fault);

    private sealed class Claim
    {
        public TaskCompletionSource<Attempt> Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
