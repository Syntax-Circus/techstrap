namespace TechStrap.Client;

/// <summary>The fixed values of the submit pipeline that the options do not expose.</summary>
internal static class ResilienceDefaults
{
    public const string PipelineName = "techstrap-submit";

    /// <summary>The circuit opens when this share of the calls in the sampling window failed ...</summary>
    public const double CircuitFailureRatio = 0.5;

    /// <summary>... and at least this many calls (not attempts) were made in it.</summary>
    public const int CircuitMinimumThroughput = 5;

    public static readonly TimeSpan CircuitSamplingDuration = TimeSpan.FromSeconds(30);

    /// <summary>How long an open circuit answers without calling the API.</summary>
    public static readonly TimeSpan CircuitBreakDuration = TimeSpan.FromSeconds(30);
}
