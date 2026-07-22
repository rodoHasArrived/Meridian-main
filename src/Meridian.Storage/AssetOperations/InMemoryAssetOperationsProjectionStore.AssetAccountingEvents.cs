using Meridian.Contracts.AssetOperations;

namespace Meridian.Storage.AssetOperations;

public sealed partial class InMemoryAssetOperationsProjectionStore
{
    private Dictionary<(Guid EventId, long EventVersion, long SpineVersion), AssetAccountingEventProjectionRecord>
        _assetAccountingEvents = [];

    public Task<AssetAccountingEventProjectionRecord?> GetAsync(
        Guid eventId,
        long eventVersion,
        long spineVersion,
        CancellationToken ct = default)
    {
        ValidateReadIdentity(eventId, eventVersion, spineVersion);
        ct.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var key = (eventId, eventVersion, spineVersion);
            return Task.FromResult(
                _assetAccountingEvents.TryGetValue(key, out var record)
                    ? AssetAccountingEventProjectionRules.Clone(record)
                    : null);
        }
    }

    public Task<AssetAccountingEventProjectionRecord?> GetLatestAsync(
        Guid eventId,
        long eventVersion,
        CancellationToken ct = default)
    {
        ValidateStreamIdentity(eventId, eventVersion);
        ct.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var record = _assetAccountingEvents.Values
                .Where(candidate =>
                    candidate.Projection.EventId == eventId &&
                    candidate.Projection.EventVersion == eventVersion)
                .OrderByDescending(static candidate => candidate.Projection.SpineVersion)
                .FirstOrDefault();
            return Task.FromResult(
                record is null
                    ? null
                    : AssetAccountingEventProjectionRules.Clone(record));
        }
    }

    public async Task<AssetAccountingEventAppendResult> AppendAsync(
        AssetAccountingEventSpineDto projection,
        long expectedSpineVersion,
        long expectedBookPositionVersion,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ct.ThrowIfCancellationRequested();
        projection = AssetAccountingEventProjectionRules.Clone(projection);
        await AssetAccountingEventProjectionRules.ValidateDurablePostedImpactAsync(
                projection,
                _assetAccountingJournalStore,
                ct)
            .ConfigureAwait(false);
        var fingerprint = AssetAccountingEventProjectionRules.Fingerprint(projection);
        var key = (projection.EventId, projection.EventVersion, projection.SpineVersion);

        lock (_gate)
        {
            if (_assetAccountingEvents.TryGetValue(key, out var exactIdentity))
            {
                if (!string.Equals(
                        exactIdentity.CanonicalFingerprint,
                        fingerprint,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Asset accounting event '{projection.EventId:D}' source version " +
                        $"'{projection.EventVersion}' spine version '{projection.SpineVersion}' " +
                        "already exists with a different canonical fingerprint.");
                }

                return new AssetAccountingEventAppendResult(
                    AssetAccountingEventProjectionRules.Clone(exactIdentity.Projection),
                    exactIdentity.CanonicalFingerprint,
                    WasReplay: true);
            }

            AssetAccountingEventProjectionRules.ValidateAppend(
                projection,
                expectedSpineVersion,
                expectedBookPositionVersion);

            if (!_bookPositions.TryGetValue(projection.Scope.BookPositionId, out var position))
            {
                throw new InvalidOperationException(
                    $"Book position '{projection.Scope.BookPositionId:D}' was not found.");
            }

            if (position.Version != expectedBookPositionVersion ||
                position.SecurityId != projection.Scope.SecurityId ||
                position.BookContext.LedgerBookId != projection.Scope.LedgerBookId)
            {
                throw new InvalidOperationException(
                    "Asset accounting event append rejected a stale or mismatched book-position assertion.");
            }

            var stream = _assetAccountingEvents.Values
                .Where(record =>
                    record.Projection.EventId == projection.EventId &&
                    record.Projection.EventVersion == projection.EventVersion)
                .OrderByDescending(static record => record.Projection.SpineVersion)
                .ToArray();
            var latest = stream.FirstOrDefault();
            var currentSpineVersion = latest?.Projection.SpineVersion ?? 0;
            if (currentSpineVersion != expectedSpineVersion)
            {
                throw new InvalidOperationException(
                    $"Asset accounting event '{projection.EventId:D}' expected spine version " +
                    $"'{expectedSpineVersion}', but current version is '{currentSpineVersion}'.");
            }

            if (latest is not null)
            {
                AssetAccountingEventProjectionRules.ValidateStreamContinuity(
                    latest.Projection,
                    projection);
            }

            var retained = new AssetAccountingEventProjectionRecord(projection, fingerprint);
            var events = new Dictionary<
                (Guid EventId, long EventVersion, long SpineVersion),
                AssetAccountingEventProjectionRecord>(_assetAccountingEvents)
            {
                [key] = retained
            };
            _assetAccountingEvents = events;

            return new AssetAccountingEventAppendResult(
                AssetAccountingEventProjectionRules.Clone(projection),
                fingerprint,
                WasReplay: false);
        }
    }

    private static void ValidateReadIdentity(Guid eventId, long eventVersion, long spineVersion)
    {
        ValidateStreamIdentity(eventId, eventVersion);

        if (spineVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(spineVersion),
                "An asset accounting spine version must be positive.");
        }
    }

    private static void ValidateStreamIdentity(Guid eventId, long eventVersion)
    {
        if (eventId == Guid.Empty)
        {
            throw new ArgumentException("An asset accounting event identity is required.", nameof(eventId));
        }

        if (eventVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(eventVersion),
                "An asset accounting source-event version must be positive.");
        }
    }
}
