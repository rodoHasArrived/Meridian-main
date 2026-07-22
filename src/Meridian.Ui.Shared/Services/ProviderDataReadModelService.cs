using System.Threading.Channels;
using Meridian.ProviderSdk;

namespace Meridian.Ui.Shared.Services;

/// <summary>Stable source identity for an operator-visible provider datum.</summary>
public sealed record ProviderProjectionProvenance(
    string Key,
    string ProviderFamily,
    int RequestId,
    string Capability,
    DateTimeOffset ObservedAt);

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
/// key (<c>provider-family/request-id/capability/item-id</c>), so repeated live snapshots replace
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

    public IReadOnlyList<ProviderDataRequestReadModel> GetRequests() => _providers
        .SelectMany(static provider => provider.GetRequests())
        .OrderByDescending(static request => request.UpdatedAt)
        .ThenBy(static request => request.RequestId)
        .ToArray();

    /// <summary>Emits the initial projection and a refreshed snapshot after any optional provider stream changes.</summary>
    public async IAsyncEnumerable<ProviderDataProjectionSnapshot> WatchAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return GetProjection();
        var updates = Channel.CreateUnbounded<bool>();
        var pumps = new List<Task>();
        pumps.AddRange(_providers.Select(provider => Pump(provider.WatchAsync(cancellationToken), updates.Writer, cancellationToken)));
        pumps.AddRange(_newsProviders.Select(provider => Pump(provider.WatchNewsAsync(cancellationToken), updates.Writer, cancellationToken)));
        pumps.AddRange(_calendarProviders.Select(provider => Pump(provider.WatchCalendarEventsAsync(cancellationToken), updates.Writer, cancellationToken)));
        pumps.AddRange(_instrumentProviders.Select(provider => Pump(provider.WatchInstrumentsAsync(cancellationToken), updates.Writer, cancellationToken)));
        _ = Task.WhenAll(pumps).ContinueWith(_ => updates.Writer.TryComplete(), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);

        await foreach (var _ in updates.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            yield return GetProjection();
    }

    private static async Task Pump<T>(IAsyncEnumerable<T> stream, ChannelWriter<bool> updates, CancellationToken cancellationToken)
    {
        await foreach (var _ in stream.WithCancellation(cancellationToken).ConfigureAwait(false))
            await updates.WriteAsync(true, cancellationToken).ConfigureAwait(false);
    }

    public ProviderDataProjectionSnapshot GetProjection()
    {
        var availability = _availabilityProviders.SelectMany(static provider => provider.GetAvailability())
            .GroupBy(static item => item.ProviderFamily, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.OrderByDescending(x => x.ObservedAt).First(), StringComparer.OrdinalIgnoreCase);
        var requests = GetRequests();

        var news = _newsProviders.SelectMany(provider => provider.GetNews().Select(item => (provider.ProviderFamily, item))).Select((entry, index) =>
            new ProviderNewsReadModel(CreateOptionalProvenance(entry.ProviderFamily, "news", entry.item.NewsId, entry.item.PublishedAt, index), OptionalAvailability(entry.ProviderFamily, entry.item.PublishedAt, availability), entry.item));
        var calendars = _calendarProviders.SelectMany(provider => provider.GetCalendarEvents().Select(item => (provider.ProviderFamily, item))).Select((entry, index) =>
            new ProviderCalendarReadModel(CreateOptionalProvenance(entry.ProviderFamily, "calendar", entry.item.EventId, entry.item.StartsAt, index), OptionalAvailability(entry.ProviderFamily, entry.item.StartsAt, availability), entry.item));
        var instruments = _instrumentProviders.SelectMany(provider => provider.GetInstruments().Select(item => (provider.ProviderFamily, item))).Select((entry, index) =>
            new ProviderInstrumentDiscoveryReadModel(CreateOptionalProvenance(entry.ProviderFamily, "instrument-discovery", entry.item.InstrumentId, DateTimeOffset.MinValue, index), OptionalAvailability(entry.ProviderFamily, DateTimeOffset.MinValue, availability), entry.item));

        return new ProviderDataProjectionSnapshot(
            Deduplicate(news, x => x.Provenance.Key, x => x.Provenance.ObservedAt),
            Deduplicate(requests.Where(x => x.ScannerResults is not null).SelectMany(request => request.ScannerResults!.Select((item, index) => new ProviderScannerReadModel(Provenance(request, $"scanner:{item.Symbol}:{item.Rank}:{index}"), Availability(request, availability), item))), x => x.Provenance.Key, x => x.Provenance.ObservedAt),
            Deduplicate(requests.Where(x => x.Pnl is not null).Select(request => new ProviderPnlReadModel(Provenance(request, $"pnl:{request.Pnl!.AccountId}:{request.Pnl.ModelAccountId}"), Availability(request, availability), request.Pnl!)), x => x.Provenance.Key, x => x.Provenance.ObservedAt),
            Deduplicate(calendars, x => x.Provenance.Key, x => x.Provenance.ObservedAt),
            Deduplicate(requests.Where(x => x.MarketRuleIncrements is not null).SelectMany(request => request.MarketRuleIncrements!.Select((item, index) => new ProviderMarketRuleReadModel(Provenance(request, $"market-rule:{item.LowEdge}:{item.Increment}:{index}"), Availability(request, availability), item))), x => x.Provenance.Key, x => x.Provenance.ObservedAt),
            Deduplicate(instruments, x => x.Provenance.Key, x => x.Provenance.ObservedAt));
    }

    private static IReadOnlyList<T> Deduplicate<T>(IEnumerable<T> rows, Func<T, string> key, Func<T, DateTimeOffset> observedAt) => rows
        .GroupBy(key, StringComparer.Ordinal)
        .Select(group => group.OrderByDescending(observedAt).First())
        .OrderByDescending(observedAt)
        .ThenBy(key, StringComparer.Ordinal)
        .ToArray();

    private static ProviderProjectionProvenance Provenance(ProviderDataRequestReadModel request, string itemKey) =>
        new($"{request.ProviderFamily}/{request.RequestId}/{request.Capability}/{itemKey}", request.ProviderFamily, request.RequestId, request.Capability, request.UpdatedAt);

    private static ProviderProjectionProvenance CreateOptionalProvenance(string providerFamily, string capability, string itemKey, DateTimeOffset observedAt, int index) =>
        new($"{providerFamily}/{index}/{capability}/{itemKey}", providerFamily, index, capability, observedAt);

    private static ProviderProjectionAvailability Availability(ProviderDataRequestReadModel request, IReadOnlyDictionary<string, ProviderDataAvailability> availability) =>
        availability.TryGetValue(request.ProviderFamily, out var item)
            ? new(item.IsAvailable, item.ConnectionState, item.Entitlement, item.Detail)
            : new(request.Status is ProviderDataRequestStatus.Streaming or ProviderDataRequestStatus.Completed, request.Status.ToString(), null, request.ErrorMessage);

    private static ProviderProjectionAvailability OptionalAvailability(string providerFamily, DateTimeOffset observedAt, IReadOnlyDictionary<string, ProviderDataAvailability> availability) =>
        availability.TryGetValue(providerFamily, out var item)
            ? new(item.IsAvailable, item.ConnectionState, item.Entitlement, item.Detail)
            : new(true, "Available", null, null);
}
