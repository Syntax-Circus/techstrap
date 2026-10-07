using SyntaxCircus.Common;
using TechStrap.Portal.Forms;

namespace TechStrap.Portal.Tests.Forms;

/// <summary>
/// The double-send guard on its own (D-045 09d addendum, Review Focus 1 and 2): one write per id however the posts arrive, a repeat goes where the first went, an aborted first request cannot cut the write short, a
/// failure lets a retry through, an unknown answer sends a repeat to the fallback, and an id belongs to one form and one subject only.
/// </summary>
public sealed class SubmitGuardTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly SubmitTarget Fallback = new("/fallback");
    private static readonly string Id = SubmitIds.New();

    private static SubmitKey Key(string form = "contact", string scope = "paperplane", string? id = null) => SubmitKey.TryCreate(form, scope, id ?? Id).ShouldNotBeNull();

    private static Task<Result<SubmitTarget>> Ok(string path) => Task.FromResult(Result<SubmitTarget>.Success(new SubmitTarget(path)));

    private static Task<Result<SubmitTarget>> Fail(string code) => Task.FromResult(Result<SubmitTarget>.Failure(new ResultError(code, "failed", ResultErrorKind.Failure)));

    /// <summary>A write that counts its calls and finishes when the test says so.</summary>
    private sealed class GatedWrite(string path)
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public CancellationToken Token { get; private set; }

        public void Release() => _release.SetResult();

        public async Task<Result<SubmitTarget>> RunAsync(CancellationToken token)
        {
            Interlocked.Increment(ref _calls);
            Token = token;
            await _release.Task.WaitAsync(token);
            return Result<SubmitTarget>.Success(new SubmitTarget(path));
        }
    }

    // ---- ids and keys ----

    [Fact]
    public void An_id_is_22_url_safe_characters_and_never_repeats()
    {
        var ids = Enumerable.Range(0, 2000).Select(_ => SubmitIds.New()).ToList();

        ids.ShouldAllBe(id => id.Length == SubmitIds.Length && SubmitIds.IsWellFormed(id));
        ids.Distinct().Count().ShouldBe(ids.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("AbC-_0123456789AbC-_01")]
    [InlineData("AbC-_0123456789AbC-_012")]
    [InlineData("AbC-_0123456789AbC-_0=")]
    [InlineData("AbC-_0123456789AbC-_0 ")]
    [InlineData("AbC-_0123456789AbC-_0\n")]
    public void Only_an_id_of_exactly_the_made_shape_is_well_formed(string? id)
    {
        SubmitIds.IsWellFormed(id).ShouldBe(id is "AbC-_0123456789AbC-_01");
        (SubmitKey.TryCreate("contact", "paperplane", id) is not null).ShouldBe(id is "AbC-_0123456789AbC-_01");
    }

    [Fact]
    public void A_key_depends_on_the_form_the_subject_and_the_id_and_holds_none_of_them()
    {
        var key = Key("reply", "AbC-_0123456789AbC-_0123456789AbC-_01234567");

        Key("reply", "AbC-_0123456789AbC-_0123456789AbC-_01234567").Hash.ShouldBe(key.Hash);
        Key("contact", "AbC-_0123456789AbC-_0123456789AbC-_01234567").Hash.ShouldNotBe(key.Hash);
        Key("reply", "Zk9_-qqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqq").Hash.ShouldNotBe(key.Hash);
        Key("reply", "AbC-_0123456789AbC-_0123456789AbC-_01234567", SubmitIds.New()).Hash.ShouldNotBe(key.Hash);
        key.Hash.ShouldNotContain("AbC-_0123456789");
        key.Hash.ShouldNotContain(Id);
    }

    // ---- one write per id ----

    [Fact]
    public async Task No_key_means_no_guard_and_the_write_runs_on_the_requests_own_token()
    {
        using var guard = new SubmitGuard();
        using var source = new CancellationTokenSource();
        var seen = new List<CancellationToken>();

        for (var i = 0; i < 2; i++)
        {
            var outcome = await guard.RunAsync(null, Fallback, token =>
            {
                seen.Add(token);
                return Ok("/first");
            }, source.Token);
            outcome.Status.ShouldBe(SubmitStatus.Done);
        }

        seen.Count.ShouldBe(2);
        seen.ShouldAllBe(token => token == source.Token);
    }

    [Fact(Timeout = 10000)]
    public async Task A_repeat_after_the_first_finished_goes_where_the_first_went_without_a_second_write()
    {
        using var guard = new SubmitGuard();
        var calls = 0;

        var first = await guard.RunAsync(Key(), Fallback, _ => { calls++; return Ok("/received?ref=one"); }, TestContext.Current.CancellationToken);
        var second = await guard.RunAsync(Key(), Fallback, _ => { calls++; return Ok("/received?ref=two"); }, TestContext.Current.CancellationToken);

        calls.ShouldBe(1);
        first.Target.Path.ShouldBe("/received?ref=one");
        second.Status.ShouldBe(SubmitStatus.Done);
        second.Target.Path.ShouldBe("/received?ref=one");
    }

    [Fact(Timeout = 10000)]
    public async Task Concurrent_repeats_make_one_write_and_all_go_to_the_same_place()
    {
        using var guard = new SubmitGuard();
        var write = new GatedWrite("/received?ref=one");

        var posts = Enumerable.Range(0, 8).Select(_ => Task.Run(() => guard.RunAsync(Key(), Fallback, write.RunAsync, TestContext.Current.CancellationToken), TestContext.Current.CancellationToken)).ToList();
        await Task.Delay(200, TestContext.Current.CancellationToken);
        write.Calls.ShouldBe(1, "one claim, one write, however many posts are waiting");
        write.Release();
        var outcomes = await Task.WhenAll(posts);

        write.Calls.ShouldBe(1);
        outcomes.ShouldAllBe(outcome => outcome.Status == SubmitStatus.Done && outcome.Target.Path == "/received?ref=one");
    }

    [Fact(Timeout = 10000)]
    public async Task The_write_runs_on_its_own_token_that_the_request_cannot_cancel_and_the_first_request_still_waits_for_it()
    {
        using var guard = new SubmitGuard();
        var write = new GatedWrite("/received?ref=one");
        using var aborted = new CancellationTokenSource();

        var first = guard.RunAsync(Key(), Fallback, write.RunAsync, aborted.Token);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        await aborted.CancelAsync();
        await Task.Delay(100, TestContext.Current.CancellationToken);

        write.Token.IsCancellationRequested.ShouldBeFalse("the browser going away must not cancel a write the API may already have");
        write.Token.CanBeCanceled.ShouldBeTrue("it has its own timeout");
        first.IsCompleted.ShouldBeFalse("the first request keeps waiting so what it is reading stays valid");
        write.Release();
        (await first).Target.Path.ShouldBe("/received?ref=one");
    }

    [Fact(Timeout = 10000)]
    public async Task A_repeat_that_arrives_after_the_first_was_aborted_gets_the_first_ones_result()
    {
        using var guard = new SubmitGuard();
        var write = new GatedWrite("/received?ref=one");
        using var aborted = new CancellationTokenSource();
        var first = guard.RunAsync(Key(), Fallback, write.RunAsync, aborted.Token);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        await aborted.CancelAsync();

        var second = guard.RunAsync(Key(), Fallback, _ => throw new InvalidOperationException("a repeat must not write"), TestContext.Current.CancellationToken);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        write.Release();

        (await second).Target.Path.ShouldBe("/received?ref=one");
        (await first).Target.Path.ShouldBe("/received?ref=one");
        write.Calls.ShouldBe(1);
    }

    [Fact(Timeout = 10000)]
    public async Task A_repeat_whose_own_request_is_aborted_stops_waiting_and_the_write_is_not_affected()
    {
        using var guard = new SubmitGuard();
        var write = new GatedWrite("/received?ref=one");
        var first = guard.RunAsync(Key(), Fallback, write.RunAsync, TestContext.Current.CancellationToken);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        using var gone = new CancellationTokenSource();

        var second = guard.RunAsync(Key(), Fallback, _ => throw new InvalidOperationException("a repeat must not write"), gone.Token);
        await gone.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => second);
        write.Release();
        (await first).Target.Path.ShouldBe("/received?ref=one");
    }

    // ---- failures ----

    [Fact(Timeout = 10000)]
    public async Task A_failure_releases_the_claim_so_a_retry_with_the_same_id_writes_again()
    {
        using var guard = new SubmitGuard();
        var calls = 0;

        var failed = await guard.RunAsync(Key(), Fallback, _ => { calls++; return Fail("rate-limited"); }, TestContext.Current.CancellationToken);
        var retry = await guard.RunAsync(Key(), Fallback, _ => { calls++; return Ok("/received"); }, TestContext.Current.CancellationToken);

        calls.ShouldBe(2);
        failed.Status.ShouldBe(SubmitStatus.Failed);
        failed.Errors[0].Code.ShouldBe("rate-limited");
        retry.Status.ShouldBe(SubmitStatus.Done);
    }

    [Fact(Timeout = 10000)]
    public async Task A_repeat_that_was_already_waiting_gets_the_same_failure_and_does_not_write()
    {
        using var guard = new SubmitGuard();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var first = guard.RunAsync(Key(), Fallback, async _ => { calls++; await release.Task; return await Fail("api-unavailable"); }, TestContext.Current.CancellationToken);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        var second = guard.RunAsync(Key(), Fallback, _ => { calls++; return Ok("/never"); }, TestContext.Current.CancellationToken);
        await Task.Delay(100, TestContext.Current.CancellationToken);

        release.SetResult();

        (await first).Status.ShouldBe(SubmitStatus.Failed);
        var repeated = await second;
        repeated.Status.ShouldBe(SubmitStatus.Failed);
        repeated.Errors[0].Code.ShouldBe("api-unavailable");
        calls.ShouldBe(1);
    }

    [Fact(Timeout = 10000)]
    public async Task A_write_that_times_out_is_unknown_and_a_repeat_goes_to_the_fallback_without_writing()
    {
        using var guard = new SubmitGuard(SubmitGuard.Lifetime, TimeSpan.FromMilliseconds(200), 100);
        var calls = 0;

        var first = await guard.RunAsync(Key(), Fallback, async token => { calls++; await Task.Delay(Timeout.Infinite, token); return await Ok("/never"); }, TestContext.Current.CancellationToken);
        var repeat = await guard.RunAsync(Key(), Fallback, _ => { calls++; return Ok("/never"); }, TestContext.Current.CancellationToken);

        calls.ShouldBe(1);
        first.Status.ShouldBe(SubmitStatus.Unknown);
        repeat.Status.ShouldBe(SubmitStatus.Done);
        repeat.Target.Path.ShouldBe("/fallback");
    }

    [Fact(Timeout = 10000)]
    public async Task A_write_that_throws_keeps_the_claim_as_unknown_and_the_first_request_sees_the_exception()
    {
        using var guard = new SubmitGuard();
        var calls = 0;

        await Should.ThrowAsync<InvalidOperationException>(() => guard.RunAsync(Key(), Fallback, _ => { calls++; throw new InvalidOperationException("boom"); }, TestContext.Current.CancellationToken));
        var repeat = await guard.RunAsync(Key(), Fallback, _ => { calls++; return Ok("/never"); }, TestContext.Current.CancellationToken);

        calls.ShouldBe(1);
        repeat.Target.Path.ShouldBe("/fallback");
    }

    // ---- scope ----

    [Fact(Timeout = 10000)]
    public async Task An_id_cannot_cross_forms_products_or_tickets()
    {
        using var guard = new SubmitGuard();
        var calls = 0;
        Task<Result<SubmitTarget>> Write(CancellationToken _)
        {
            calls++;
            return Ok("/x");
        }

        await guard.RunAsync(Key("contact", "paperplane"), Fallback, Write, TestContext.Current.CancellationToken);
        await guard.RunAsync(Key("lost-link", "paperplane"), Fallback, Write, TestContext.Current.CancellationToken);
        await guard.RunAsync(Key("contact", "other"), Fallback, Write, TestContext.Current.CancellationToken);
        await guard.RunAsync(Key("reply", "AbC-_0123456789AbC-_0123456789AbC-_01234567"), Fallback, Write, TestContext.Current.CancellationToken);
        await guard.RunAsync(Key("reply", "Zk9_-qqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqq"), Fallback, Write, TestContext.Current.CancellationToken);

        calls.ShouldBe(5, "five different (form, subject) pairs with one id are five different claims");
    }

    [Fact(Timeout = 10000)]
    public async Task The_product_key_is_compared_without_regard_to_case()
    {
        using var guard = new SubmitGuard();
        var calls = 0;

        await guard.RunAsync(Key("contact", "Paperplane"), Fallback, _ => { calls++; return Ok("/x"); }, TestContext.Current.CancellationToken);
        await guard.RunAsync(Key("contact", "paperplane"), Fallback, _ => { calls++; return Ok("/x"); }, TestContext.Current.CancellationToken);

        calls.ShouldBe(1);
    }

    // ---- lifetime and cap ----

    [Fact(Timeout = 10000)]
    public async Task A_claim_expires_after_its_lifetime()
    {
        using var guard = new SubmitGuard(TimeSpan.FromMilliseconds(300), SubmitGuard.WriteTimeout, 100);
        var calls = 0;

        await guard.RunAsync(Key(), Fallback, _ => { calls++; return Ok("/x"); }, TestContext.Current.CancellationToken);
        await Task.Delay(700, TestContext.Current.CancellationToken);
        await guard.RunAsync(Key(), Fallback, _ => { calls++; return Ok("/x"); }, TestContext.Current.CancellationToken);

        calls.ShouldBe(2);
    }

    [Fact(Timeout = 10000)]
    public async Task The_cache_is_capped_and_a_full_cache_lets_a_new_post_through_unguarded_without_evicting_a_claim()
    {
        using var guard = new SubmitGuard(SubmitGuard.Lifetime, SubmitGuard.WriteTimeout, 2);
        var calls = 0;
        Task<Result<SubmitTarget>> Write(CancellationToken _)
        {
            calls++;
            return Ok("/x");
        }

        var a = Key(id: SubmitIds.New());
        var b = Key(id: SubmitIds.New());
        var c = Key(id: SubmitIds.New());
        await guard.RunAsync(a, Fallback, Write, TestContext.Current.CancellationToken);
        await guard.RunAsync(b, Fallback, Write, TestContext.Current.CancellationToken);
        var unguarded = await guard.RunAsync(c, Fallback, Write, TestContext.Current.CancellationToken);
        await guard.RunAsync(c, Fallback, Write, TestContext.Current.CancellationToken);
        await guard.RunAsync(a, Fallback, Write, TestContext.Current.CancellationToken);
        await guard.RunAsync(b, Fallback, Write, TestContext.Current.CancellationToken);

        unguarded.Status.ShouldBe(SubmitStatus.Done, "over the cap a post is not refused");
        calls.ShouldBe(4, "a, b and the two unguarded posts of c wrote; the repeats of a and b did not");
    }

    [Fact]
    public void The_guard_has_a_cache_of_its_own_and_never_logs()
    {
        // The shared IMemoryCache has no size limit and holds the sitemap (setting one would break it), and a log line could carry a target.
        var constructors = typeof(SubmitGuard).GetConstructors().SelectMany(c => c.GetParameters()).Select(p => p.ParameterType).ToList();

        constructors.ShouldNotContain(typeof(Microsoft.Extensions.Caching.Memory.IMemoryCache));
        constructors.ShouldNotContain(t => t.Name.StartsWith("ILogger", StringComparison.Ordinal));
        (SubmitGuard.Lifetime < ReceivedReference.Lifetime).ShouldBeTrue("a stored reference must outlive the claim that holds it");
    }
}
