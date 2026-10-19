using System.ComponentModel.DataAnnotations;

namespace StockReplenishment.Api.StockChecks;

public sealed class StockCheckOptions
{
    public const string SectionName = "StockCheck";

    /// <summary>How many checks may run against the external service at the same time.</summary>
    [Range(1, 64)]
    public int MaxConcurrentChecks { get; set; } = 4;

    [Range(1, 10)]
    public int MaxAttempts { get; set; } = 3;

    /// <summary>Per-attempt timeout for a call to the external service.</summary>
    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>Delay before the first retry; doubled for every following attempt.</summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(2);
}

/// <summary>Tuning for the simulated external service.</summary>
public sealed class SimulatedStockServiceOptions
{
    public const string SectionName = "SimulatedStockService";

    public TimeSpan MinLatency { get; set; } = TimeSpan.FromSeconds(3);

    public TimeSpan MaxLatency { get; set; } = TimeSpan.FromSeconds(8);

    /// <summary>Probability (0..1) that a single call fails, to exercise the retry and failure handling.</summary>
    [Range(0.0, 1.0)]
    public double FailureRate { get; set; } = 0.15;
}
