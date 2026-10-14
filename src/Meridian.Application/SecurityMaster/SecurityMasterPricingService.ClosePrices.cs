using Meridian.Contracts.Integrity;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Application.SecurityMaster;

public sealed partial class SecurityMasterPricingService
{
    public async Task<SecurityClosePriceSetDto> CreateClosePriceSetAsync(
        CreateSecurityClosePriceSetRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var capturedAt = DateTimeOffset.UtcNow;
        var knownAt = request.KnowledgeAsOf ?? capturedAt;
        if (request.Securities is null || request.Securities.Any(s => s is null))
            throw new ArgumentException("Close-price securities require a collection with no null entries.", nameof(request));
        if (request.EvaluatedAsOf > capturedAt || knownAt > capturedAt)
            throw new ArgumentException("Close-price evaluation and knowledge cutoffs cannot be later than the snapshot capture time.");
        if (string.IsNullOrWhiteSpace(request.AccountId) || string.IsNullOrWhiteSpace(request.BookId)
            || string.IsNullOrWhiteSpace(request.CreatedBy) || request.ValuationDate == default
            || request.EvaluatedAsOf == default || DateOnly.FromDateTime(request.EvaluatedAsOf.UtcDateTime) != request.ValuationDate
            || request.MaxSourceDispersionPercent < 0 || request.Securities.Count == 0 || request.RequestId == Guid.Empty
            || request.Securities.Any(s => s.SecurityId == Guid.Empty)
            || request.Securities.Select(s => s.SecurityId).Distinct().Count() != request.Securities.Count)
            throw new ArgumentException("Close prices require an explicit account, book, date, cutoff, preparer, dispersion limit and distinct securities.");
        var account = request.AccountId.Trim();
        var book = request.BookId.Trim();
        var commandDigest = request.RequestId is null ? null : SecurityClosePriceEvidence.CreationCommandDigest(request);
        if (request.RequestId is { } requestId)
        {
            var existing = await _store.GetClosePriceSetAsync(account, book, request.ValuationDate, requestId, ct).ConfigureAwait(false);
            if (existing is not null)
            {
                if (!Sha256Digest.FixedEquals(existing.Snapshot.CreationCommandDigest, commandDigest))
                    throw new InvalidOperationException("A close-price request identity cannot be reused for a different creation command.");
                return existing;
            }
        }
        SecurityClosePriceSnapshotDto? prior = null;
        if (request.CorrectsSnapshotId is { } priorId)
        {
            prior = (await _store.GetClosePriceSetAsync(account, book, request.ValuationDate, priorId, ct).ConfigureAwait(false))?.Snapshot;
            if (prior is null || string.IsNullOrWhiteSpace(request.CorrectionRationale))
                throw new InvalidOperationException("A correction requires a retained predecessor in the exact account/book/date scope and a rationale.");
            if (!prior.Lines.Select(l => l.SecurityId).Order().SequenceEqual(request.Securities.Select(s => s.SecurityId).Order()))
                throw new InvalidOperationException("A close-price correction must retain the predecessor's security coverage.");
        }

        var lines = new List<SecurityClosePriceLineDto>();
        foreach (var input in request.Securities.OrderBy(s => s.SecurityId))
        {
            var selection = await GetGoldenCopyPriceAsOfAsync(input.SecurityId, account, request.EvaluatedAsOf, ct, knownAt).ConfigureAwait(false);
            IReadOnlyList<SecurityRawPriceDto> observations;
            if (selection is null)
                observations = (await _store.GetRawPricesAsync(input.SecurityId, request.EvaluatedAsOf, ct, knownAt).ConfigureAwait(false))
                    .OrderBy(p => p.SourceId, StringComparer.Ordinal).ToArray();
            else
                observations = new[] { new SecurityRawPriceDto(selection.SelectedSource, selection.GoldenCopyPrice, selection.PriceAsOf, selection.Unit) }
                    .Concat(selection.ComparisonPrices.Select(p => new SecurityRawPriceDto(p.SourceId, p.Price, p.PriceAsOf, p.Unit)))
                    .OrderBy(p => p.SourceId, StringComparer.Ordinal).ToArray();
            lines.Add(new(input.SecurityId, selection, observations, input.Override,
                selection is null ? null : input.Override?.Price ?? selection.GoldenCopyPrice,
                selection is null ? SecurityPriceUnit.Unspecified : input.Override?.Unit ?? selection.Unit));
        }
        var snapshot = new SecurityClosePriceSnapshotDto(request.RequestId ?? Guid.NewGuid(), account, book, request.ValuationDate,
            (prior?.Version ?? 0) + 1, request.CorrectsSnapshotId, request.CorrectionRationale?.Trim(),
            request.EvaluatedAsOf, knownAt, request.MaxSourceDispersionPercent, request.CreatedBy.Trim(),
            capturedAt, lines, SecurityClosePriceEvidence.Assess(lines, request.MaxSourceDispersionPercent),
            commandDigest, request.RequestId is null ? null : request.KnowledgeAsOf?.ToUniversalTime());
        SecurityClosePriceEvidence.Validate(snapshot);
        try
        {
            await _store.RetainClosePriceSnapshotAsync(snapshot, ct).ConfigureAwait(false);
        }
        catch (InvalidOperationException) when (request.RequestId is not null)
        {
            // A competing copy of this same command may already have frozen its observations.
            // Recover that exact retained capture; never recompute or adopt another command.
            var committed = await _store.GetClosePriceSetAsync(account, book, request.ValuationDate, snapshot.SnapshotId, ct).ConfigureAwait(false);
            if (committed is null || !Sha256Digest.FixedEquals(committed.Snapshot.CreationCommandDigest, commandDigest))
                throw;
            return committed;
        }
        return new(snapshot, null);
    }

    public async Task<SecurityClosePriceSetDto> ApproveClosePriceSetAsync(
        ApproveSecurityClosePriceSetRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Reviewer) || string.IsNullOrWhiteSpace(request.Rationale))
            throw new ArgumentException("Close-price approval requires a reviewer identity and rationale.");
        var retained = await GetClosePriceSetAsync(request.AccountId, request.BookId, request.ValuationDate, request.SnapshotId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The close-price snapshot does not exist in the requested account/book/date scope.");
        if (retained.Approval is { } existing)
        {
            if (existing.Reviewer != request.Reviewer.Trim() || existing.Rationale != request.Rationale.Trim())
                throw new InvalidOperationException("An immutable close-price approval cannot be replaced.");
            return retained;
        }
        var approval = new SecurityClosePriceApprovalDto(request.SnapshotId, SecurityClosePriceEvidence.Digest(retained.Snapshot),
            request.Reviewer.Trim(), request.Rationale.Trim(), DateTimeOffset.UtcNow);
        SecurityClosePriceEvidence.ValidateApproval(retained.Snapshot, approval);
        try
        {
            await _store.RetainClosePriceApprovalAsync(retained.Snapshot.AccountId, retained.Snapshot.BookId,
                retained.Snapshot.ValuationDate, approval, ct).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            // Another identical review can commit after our initial read. Its retained timestamp
            // is authoritative; accept only that same decision over the exact snapshot digest.
            var committed = await GetClosePriceSetAsync(retained.Snapshot.AccountId, retained.Snapshot.BookId,
                retained.Snapshot.ValuationDate, approval.SnapshotId, ct).ConfigureAwait(false);
            if (committed?.Approval is not { } canonical || canonical.SnapshotId != approval.SnapshotId
                || !Sha256Digest.FixedEquals(canonical.SnapshotSha256, approval.SnapshotSha256)
                || canonical.Reviewer != approval.Reviewer || canonical.Rationale != approval.Rationale)
                throw;
            return committed;
        }
        return new(retained.Snapshot, approval);
    }

    public Task<SecurityClosePriceSetDto?> GetClosePriceSetAsync(
        string accountId, string bookId, DateOnly valuationDate, Guid snapshotId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(accountId) || string.IsNullOrWhiteSpace(bookId) || valuationDate == default || snapshotId == Guid.Empty)
            throw new ArgumentException("An exact account/book/date scope and snapshot identity are required.");
        return _store.GetClosePriceSetAsync(accountId.Trim(), bookId.Trim(), valuationDate, snapshotId, ct);
    }

    public Task<IReadOnlyList<SecurityClosePriceSetDto>> GetClosePriceSetsAsync(
        string accountId, string bookId, DateOnly valuationDate, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(accountId) || string.IsNullOrWhiteSpace(bookId) || valuationDate == default)
            throw new ArgumentException("An exact account/book/date scope is required.");
        return _store.GetClosePriceSetsAsync(accountId.Trim(), bookId.Trim(), valuationDate, ct);
    }
}
