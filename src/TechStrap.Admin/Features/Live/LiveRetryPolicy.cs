using Microsoft.AspNetCore.SignalR.Client;

namespace TechStrap.Admin.Features.Live;

/// <summary>
/// The reconnect schedule. SignalR's own <c>WithAutomaticReconnect()</c> gives up after four attempts (0, 2, 10 and 30 seconds) and stays disconnected; an agent's screen can stay open all day, so this policy
/// never gives up: 0, 2, 5, 10 seconds and then 30 seconds for ever. It stops only when <paramref name="shouldStop"/> says the session has lapsed or the token was refused; the client
/// then ends in <see cref="LiveConnectionState.Disconnected"/>. SignalR asks for a fresh access token on every attempt, so a refreshed token is picked up by the reconnect itself.
/// </summary>
/// <param name="shouldStop">True once retrying is pointless: <c>SessionExpiry.IsLapsed</c>, or the token provider returned no token.</param>
public sealed class LiveRetryPolicy(Func<bool> shouldStop) : IRetryPolicy
{
    /// <summary>The longest wait between two attempts.</summary>
    public static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(30);

    /// <summary>The wait before the first attempts; the last entry repeats.</summary>
    private static readonly TimeSpan[] Delays =
    [
        TimeSpan.Zero,
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        MaxDelay,
    ];

    /// <summary>The wait before attempt number <paramref name="attempt"/> (0 is the first retry).</summary>
    public static TimeSpan DelayFor(long attempt) => Delays[(int)Math.Clamp(attempt, 0, Delays.Length - 1)];

    public TimeSpan? NextRetryDelay(RetryContext retryContext) => shouldStop() ? null : DelayFor(retryContext.PreviousRetryCount);
}
