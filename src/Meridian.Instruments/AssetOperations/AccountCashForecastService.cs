using System.Text.Json;
using Meridian.Contracts.AssetOperations;

namespace Meridian.Instruments.AssetOperations;

/// <summary>Retains replayable account forecast versions without changing accounting or payment state.</summary>
public sealed class AccountCashForecastService(IAccountCashForecastStore store) : IAccountCashForecastService
{
    private readonly IAccountCashForecastStore _store = store ?? throw new ArgumentNullException(nameof(store));

    public Task<AccountCashForecastSnapshotDto> CaptureAsync(AccountCashForecastRequestDto request, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        // The engine records a non-UTC recording time as a blocker; the durable store refuses to retain it.
        // Reject it here as caller input so it surfaces as validation rather than unavailable evidence.
        if (request.RecordedAtUtc == default || request.RecordedAtUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Forecast recording time must be an explicit UTC instant.", nameof(request));
        return _store.AppendAsync(AccountCashForecastEngine.Build(request), ct);
    }

    public async Task<AccountCashForecastSnapshotDto?> GetAsync(AccountCashForecastScopeDto scope, string snapshotId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotId);
        var history = await GetHistoryAsync(scope, ct).ConfigureAwait(false);
        return history.SingleOrDefault(row => row.SnapshotId == snapshotId);
    }

    public async Task<IReadOnlyList<AccountCashForecastSnapshotDto>> GetHistoryAsync(AccountCashForecastScopeDto scope, CancellationToken ct = default)
    {
        var history = await _store.GetHistoryAsync(scope, ct).ConfigureAwait(false);
        foreach (var snapshot in history)
        {
            var replay = AccountCashForecastEngine.Build(snapshot.Inputs);
            var retainedJson = JsonSerializer.Serialize(snapshot, AccountCashForecastJsonContext.Default.AccountCashForecastSnapshotDto);
            var replayJson = JsonSerializer.Serialize(replay, AccountCashForecastJsonContext.Default.AccountCashForecastSnapshotDto);
            if (snapshot.Inputs.Scope != scope || retainedJson != replayJson)
                throw new InvalidDataException("Retained account cash forecast failed exact scope or deterministic replay validation.");
        }
        return history;
    }
}
