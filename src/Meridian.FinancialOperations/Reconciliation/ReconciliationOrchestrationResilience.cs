namespace Meridian.FinancialOperations.Reconciliation;

public sealed record ReconciliationOrchestrationResilienceOptions(
    int MaxRetries = 3,
    TimeSpan RetryBaseDelay = default,
    int MaxInFlightJobs = 32,
    bool DeadLetterEnabled = true)
{
    public TimeSpan RetryBaseDelay { get; init; } = RetryBaseDelay == default ? TimeSpan.FromSeconds(2) : RetryBaseDelay;

    public TimeSpan GetRetryDelay(int attempt)
    {
        var normalizedAttempt = Math.Clamp(attempt, 1, MaxRetries);
        return TimeSpan.FromMilliseconds(RetryBaseDelay.TotalMilliseconds * Math.Pow(2, normalizedAttempt - 1));
    }

    public bool ShouldDeadLetter(int attempt, Exception _) => DeadLetterEnabled && attempt >= MaxRetries;
}

public sealed record ReconciliationRolloutTarget(
    string Client,
    string Team,
    string Counterparty);

public sealed class ReconciliationFeatureFlagPolicy
{
    private readonly HashSet<string> _enabledClients = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _enabledTeams = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _enabledCounterparties = new(StringComparer.OrdinalIgnoreCase);

    public void EnableClient(string client) => _enabledClients.Add(client);
    public void EnableTeam(string team) => _enabledTeams.Add(team);
    public void EnableCounterparty(string counterparty) => _enabledCounterparties.Add(counterparty);

    public bool IsEnabled(ReconciliationRolloutTarget target)
        => _enabledClients.Contains(target.Client)
           || _enabledTeams.Contains(target.Team)
           || _enabledCounterparties.Contains(target.Counterparty);
}
