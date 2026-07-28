namespace Meridian.FinancialOperations.Reconciliation;

/// <summary>
/// Capture policy for <see cref="DefaultReconciliationIngestionScheduler"/>: how many source
/// adapters capture concurrently, how long a single capture attempt may run, and how transient
/// per-source failures retry before the run fails. Retry delays grow exponentially from
/// <see cref="RetryBaseDelay"/> (base, 2×, 4×, …) and honor cancellation.
/// </summary>
public sealed record ReconciliationIngestionOptions
{
    public static ReconciliationIngestionOptions Default { get; } = new();

    /// <summary>Single-attempt policy: any capture failure surfaces immediately.</summary>
    public static ReconciliationIngestionOptions NoRetry { get; } = new() { MaxAttemptsPerSource = 1 };

    public int MaxConcurrentCaptures { get; init; } = 4;

    public int MaxAttemptsPerSource { get; init; } = 3;

    public TimeSpan RetryBaseDelay { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>Per-attempt timeout; <c>null</c> disables the timeout.</summary>
    public TimeSpan? PerSourceTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public void Validate()
    {
        if (MaxConcurrentCaptures < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxConcurrentCaptures), MaxConcurrentCaptures, "At least one concurrent capture is required.");
        }

        if (MaxAttemptsPerSource < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxAttemptsPerSource), MaxAttemptsPerSource, "At least one capture attempt is required.");
        }

        if (RetryBaseDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(RetryBaseDelay), RetryBaseDelay, "Retry delay cannot be negative.");
        }

        if (PerSourceTimeout is { } timeout && timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(PerSourceTimeout), timeout, "Per-source timeout must be positive when set.");
        }
    }
}
