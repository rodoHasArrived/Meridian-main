using System.Threading.Channels;
using Meridian.ProviderSdk;

namespace Meridian.Ui.Shared.Services;

/// <summary>Stable source identity for an operator-visible provider datum.</summary>
public sealed record ProviderProjectionProvenance(
    string Key,
    string ProviderFamily,
    int RequestId,
    string Capability,
    DateTimeOffset ObservedAt,
    ProviderDataProvenance? Source);

/// <summary>Connection and entitlement evidence retained alongside every projection row.</summary>
public sealed record ProviderProjectionAvailability(
    bool IsAvailable,
    string ConnectionState,
    string? Entitlement,
    string? Detail);

public sealed record ProviderNewsReadModel(ProviderProjectionProvenance Provenance, ProviderProjectionAvailability Availability, ProviderNewsItem Item);
public sealed record ProviderScannerReadModel(ProviderProjectionProvenance Provenance, ProviderProjectionAvailability Availability, ProviderScannerResult Item);
public sealed record ProviderPnlReadModel(ProviderProjectionProvenance Provenance, ProviderProjectionAvailability Availability, ProviderAccountPnl Item);
public sealed record ProviderCalendarReadModel(ProviderProjectionProvenance Provenance, ProviderProjectionAvailability Availability, ProviderCalendarEvent Item);
public sealed record ProviderMarketRuleReadModel(ProviderProjectionProvenance Provenance, ProviderProjectionAvailability Availability, ProviderMarketRuleIncrement Item);
public sealed record ProviderInstrumentDiscoveryReadModel(ProviderProjectionProvenance Provenance, ProviderProjectionAvailability Availability, ProviderInstrumentDiscoveryResult Item);

/// <summary>Shared, typed snapshot consumed unchanged by browser endpoints and WPF view models.</summary>
public sealed record ProviderDataProjectionSnapshot(
    IReadOnlyList<ProviderNewsReadModel> News,
    IReadOnlyList<ProviderScannerReadModel> ScannerResults,
    IReadOnlyList<ProviderPnlReadModel> PnlStreams,
    IReadOnlyList<ProviderCalendarReadModel> Calendars,
    IReadOnlyList<ProviderMarketRuleReadModel> MarketRules,
    IReadOnlyList<ProviderInstrumentDiscoveryReadModel> Instruments);

/// <summary>
/// Aggregates provider-neutral read interfaces. Rows are de-duplicated by the stable provenance
/// key (<see cref="ProviderDataProvenance.StableDeduplicationKey"/>), so repeated live snapshots replace
/// older evidence rather than making either workstation show duplicate vendor data.
/// </summary>
public sealed class ProviderDataReadModelService
{
    private readonly IReadOnlyList<IProviderDataReadService> _providers;
    private readonly IReadOnlyList<IProviderNewsReadService> _newsProviders;
    private readonly IReadOnlyList<IProviderCalendarReadService> _calendarProviders;
    private readonly IReadOnlyList<IProviderInstrumentDiscoveryReadService> _instrumentProviders;
    private readonly IReadOnlyList<IProviderDataAvailabilityReadService> _availabilityProviders;

    public ProviderDataReadModelService(
        IEnumerable<IProviderDataReadService> providers,
        IEnumerable<IProviderNewsReadService>? newsProviders = null,
        IEnumerable<IProviderCalendarReadService>? calendarProviders = null,
        IEnumerable<IProviderInstrumentDiscoveryReadService>? instrumentProviders = null,
        IEnumerable<IProviderDataAvailabilityReadService>? availabilityProviders = null)
    {
        _providers = providers?.ToArray() ?? throw new ArgumentNullException(nameof(providers));
        _newsProviders = newsProviders?.ToArray() ?? [];
        _calendarProviders = calendarProviders?.ToArray() ?? [];
        _instrumentProviders = instrumentProviders?.ToArray() ?? [];
        _availabilityProviders = availabilityProviders?.ToArray() ?? [];
    }

    /// <summary>
    /// Returns only providers whose contract is explicitly unscoped. Tenant/company-aware
    /// providers are excluded rather than downgraded to their compatibility read surface.
    /// </summary>
    public IReadOnlyList<ProviderDataRequestReadModel> GetRequests() => _providers
        .Where(static provider => provider is not ITenantScopedProviderDataReadService)
        .SelectMany(static provider => provider.GetRequests())
        .OrderByDescending(static request => request.UpdatedAt)
        .ThenBy(static request => request.RequestId)
        .ToArray();

    public IReadOnlyList<ProviderDataRequestReadModel> GetRequests(string tenantId, string companyId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(companyId);
        tenantId = tenantId.Trim();
        companyId = companyId.Trim();
        return _providers
            .SelectMany(provider => provider is ITenantScopedProviderDataReadService scoped
                ? scoped.GetRequests(tenantId, companyId)
                : provider.GetRequests())
            .OrderByDescending(static request => request.UpdatedAt)
            .ThenBy(static request => request.RequestId)
            .ToArray();
    }

    /// <summary>Emits the initial projection and a refreshed snapshot after any optional provider stream changes.</summary>
    public async IAsyncEnumerable<ProviderDataProjectionSnapshot> WatchAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return GetProjection();
        var updates = Channel.CreateUnbounded<bool>();
        var pumps = new List<Task>();
        pumps.AddRange(_providers
            .Where(static provider => provider is not ITenantScopedProviderDataReadService)
            .Select(provider => Pump(provider.WatchAsync(cancellationToken), updates.Writer, cancellationToken)));
        pumps.AddRange(_newsProviders.Select(provider => Pump(provider.WatchNewsAsync(cancellationToken), updates.Writer, cancellationToken)));
        pumps.AddRange(_calendarProviders.Select(provider => Pump(provider.WatchCalendarEventsAsync(cancellationToken), updates.Writer, cancellationToken)));
        pumps.AddRange(_instrumentProviders.Select(provider => Pump(provider.WatchInstrumentsAsync(cancellationToken), updates.Writer, cancellationToken)));
        _ = Task.WhenAll(pumps).ContinueWith(_ => updates.Writer.TryComplete(), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);

        await foreach (var _ in updates.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            yield return GetProjection();
    }

    /// <summary>
    /// Emits only projection updates owned by the requested tenant and company. Tenant-scoped
    /// providers are never consumed through their unscoped watch surface.
    /// </summary>
    public async IAsyncEnumerable<ProviderDataProjectionSnapshot> WatchAsync(
        string tenantId,
        string companyId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(companyId);
        tenantId = tenantId.Trim();
        companyId = companyId.Trim();

        yield return GetProjection(tenantId, companyId);
        var updates = Channel.CreateUnbounded<bool>();
        var pumps = new List<Task>();
        pumps.AddRange(_providers.Select(provider => Pump(
            provider is ITenantScopedProviderDataReadService scoped
                ? scoped.WatchAsync(tenantId, companyId, cancellationToken)
                : provider.WatchAsync(cancellationToken),
            updates.Writer,
            cancellationToken)));
        pumps.AddRange(_newsProviders.Select(provider => Pump(provider.WatchNewsAsync(cancellationToken), updates.Writer, cancellationToken)));
        pumps.AddRange(_calendarProviders.Select(provider => Pump(provider.WatchCalendarEventsAsync(cancellationToken), updates.Writer, cancellationToken)));
        pumps.AddRange(_instrumentProviders.Select(provider => Pump(provider.WatchInstrumentsAsync(cancellationToken), updates.Writer, cancellationToken)));
        _ = Task.WhenAll(pumps).ContinueWith(_ => updates.Writer.TryComplete(), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);

        await foreach (var _ in updates.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            yield return GetProjection(tenantId, companyId);
    }

    private static async Task Pump<T>(IAsyncEnumerable<T> stream, ChannelWriter<bool> updates, CancellationToken cancellationToken)
    {
        await foreach (var _ in stream.WithCancellation(cancellationToken).ConfigureAwait(false))
            await updates.WriteAsync(true, cancellationToken).ConfigureAwait(false);
    }

    public ProviderDataProjectionSnapshot GetProjection()
        => GetProjection(GetRequests());

    public ProviderDataProjectionSnapshot GetProjection(string tenantId, string companyId)
        => GetProjection(GetRequests(tenantId, companyId));

    private ProviderDataProjectionSnapshot GetProjection(
        IReadOnlyList<ProviderDataRequestReadModel> requests)
    {
        var availability = _availabilityProviders.SelectMany(static provider => provider.GetAvailability())
            .GroupBy(static item => item.ProviderFamily, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.OrderByDescending(x => x.ObservedAt).First(), StringComparer.OrdinalIgnoreCase);

        var news = _newsProviders.SelectMany(provider => provider.GetNews().Select(item => (provider.ProviderFamily, item)))
            .Select(entry => new ProviderNewsReadModel(OptionalProvenance(entry.ProviderFamily, "news", entry.item.NewsId, entry.item.PublishedAt, entry.item.Provenance), OptionalAvailability(entry.ProviderFamily, entry.item.PublishedAt, availability), entry.item));
        var calendars = _calendarProviders.SelectMany(provider => provider.GetCalendarEvents().Select(item => (provider.ProviderFamily, item)))
            .Select(entry => new ProviderCalendarReadModel(OptionalProvenance(entry.ProviderFamily, "calendar", entry.item.EventId, entry.item.StartsAt, entry.item.Provenance), OptionalAvailability(entry.ProviderFamily, entry.item.StartsAt, availability), entry.item));
        var instruments = _instrumentProviders.SelectMany(provider => provider.GetInstruments().Select(item => (provider.ProviderFamily, item)))
            .Select(entry => new ProviderInstrumentDiscoveryReadModel(OptionalProvenance(entry.ProviderFamily, "instrument-discovery", entry.item.InstrumentId, DateTimeOffset.MinValue, entry.item.Provenance), OptionalAvailability(entry.ProviderFamily, DateTimeOffset.MinValue, availability), entry.item));

        return new ProviderDataProjectionSnapshot(
            Deduplicate(news, x => x.Provenance.Key, x => x.Provenance.ObservedAt),
            Deduplicate(requests.Where(x => x.ScannerResults is not null).SelectMany(request => request.ScannerResults!.Select(item => new ProviderScannerReadModel(Provenance(request, item.Provenance, $"scanner:{item.Symbol}:{item.Rank}"), Availability(request, availability), item))), x => x.Provenance.Key, x => x.Provenance.ObservedAt),
            Deduplicate(requests.Where(x => x.Pnl is not null).Select(request => new ProviderPnlReadModel(Provenance(request, request.Pnl!.Provenance, $"pnl:{request.Pnl.AccountId}:{request.Pnl.ModelAccountId}"), Availability(request, availability), request.Pnl!)), x => x.Provenance.Key, x => x.Provenance.ObservedAt),
            Deduplicate(calendars, x => x.Provenance.Key, x => x.Provenance.ObservedAt),
            Deduplicate(requests.Where(x => x.MarketRuleIncrements is not null).SelectMany(request => request.MarketRuleIncrements!.Select(item => new ProviderMarketRuleReadModel(Provenance(request, item.Provenance, $"market-rule:{item.LowEdge}:{item.Increment}"), Availability(request, availability), item))), x => x.Provenance.Key, x => x.Provenance.ObservedAt),
            Deduplicate(instruments, x => x.Provenance.Key, x => x.Provenance.ObservedAt));
    }

    private static IReadOnlyList<T> Deduplicate<T>(IEnumerable<T> rows, Func<T, string> key, Func<T, DateTimeOffset> observedAt) => rows
        .GroupBy(key, StringComparer.Ordinal)
        .Select(group => group.OrderByDescending(observedAt).First())
        .OrderByDescending(observedAt)
        .ThenBy(key, StringComparer.Ordinal)
        .ToArray();

    private static ProviderProjectionProvenance Provenance(ProviderDataRequestReadModel request, ProviderDataProvenance source, string legacyItemKey) =>
        new(StableKey(source, $"{request.ProviderFamily}/{request.RequestId}/{request.Capability}/{legacyItemKey}"), request.ProviderFamily, request.RequestId, request.Capability, ObservedAt(source, request.UpdatedAt), source);

    private static ProviderProjectionProvenance OptionalProvenance(string providerFamily, string capability, string itemKey, DateTimeOffset observedAt, ProviderDataProvenance? source) =>
        new(StableKey(source, $"{providerFamily}/{capability}/{itemKey}"), providerFamily, 0, capability, ObservedAt(source, observedAt), source);

    private static string StableKey(ProviderDataProvenance? source, string fallback) =>
        string.IsNullOrWhiteSpace(source?.StableDeduplicationKey) ? fallback : source.StableDeduplicationKey;

    private static DateTimeOffset ObservedAt(ProviderDataProvenance? source, DateTimeOffset fallback) =>
        source is null ? fallback : source.ReceiptTimestamp > source.SourceTimestamp ? source.ReceiptTimestamp : source.SourceTimestamp;

    private static ProviderProjectionAvailability Availability(ProviderDataRequestReadModel request, IReadOnlyDictionary<string, ProviderDataAvailability> availability) =>
        availability.TryGetValue(request.ProviderFamily, out var item)
            ? new(item.IsAvailable, item.ConnectionState, item.Entitlement, item.Detail)
            : new(request.Status is ProviderDataRequestStatus.Streaming or ProviderDataRequestStatus.Completed, request.Status.ToString(), null, request.ErrorMessage);

    private static ProviderProjectionAvailability OptionalAvailability(string providerFamily, DateTimeOffset observedAt, IReadOnlyDictionary<string, ProviderDataAvailability> availability) =>
        availability.TryGetValue(providerFamily, out var item)
            ? new(item.IsAvailable, item.ConnectionState, item.Entitlement, item.Detail)
            : new(true, "Available", null, null);
}
