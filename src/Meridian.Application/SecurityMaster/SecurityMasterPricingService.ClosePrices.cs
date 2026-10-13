using Meridian.Contracts.SecurityMaster;

namespace Meridian.Application.SecurityMaster;

public sealed partial class SecurityMasterPricingService
{
    public async Task<SecurityClosePriceSetDto> CreateClosePriceSetAsync(
        CreateSecurityClosePriceSetRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.AccountId) || string.IsNullOrWhiteSpace(request.BookId)
            || string.IsNullOrWhiteSpace(request.CreatedBy) || request.ValuationDate == default
            || request.EvaluatedAsOf == default || DateOnly.FromDateTime(request.EvaluatedAsOf.UtcDateTime) != request.ValuationDate
            || request.MaxSourceDispersionPercent < 0 || request.Securities.Count == 0
            || request.Securities.Any(s => s.SecurityId == Guid.Empty)
            || request.Securities.Select(s => s.SecurityId).Distinct().Count() != request.Securities.Count)
            throw new ArgumentException("Close prices require an explicit account, book, date, cutoff, preparer, dispersion limit and distinct securities.");
        var account = request.AccountId.Trim();
        var book = request.BookId.Trim();
        SecurityClosePriceSnapshotDto? prior = null;
        if (request.CorrectsSnapshotId is { } priorId)
        {
            prior = (await _store.GetClosePriceSetAsync(account, book, request.ValuationDate, priorId, ct).ConfigureAwait(false))?.Snapshot;
            if (prior is null || string.IsNullOrWhiteSpace(request.CorrectionRationale))
                throw new InvalidOperationException("A correction requires a retained predecessor in the exact account/book/date scope and a rationale.");
            if (!prior.Lines.Select(l => l.SecurityId).Order().SequenceEqual(request.Securities.Select(s => s.SecurityId).Order()))
                throw new InvalidOperationException("A close-price correction must retain the predecessor's security coverage.");
        }

        var knownAt = request.KnowledgeAsOf ?? DateTimeOffset.UtcNow;
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
        var snapshot = new SecurityClosePriceSnapshotDto(Guid.NewGuid(), account, book, request.ValuationDate,
            (prior?.Version ?? 0) + 1, request.CorrectsSnapshotId, request.CorrectionRationale?.Trim(),
            request.EvaluatedAsOf, knownAt, request.MaxSourceDispersionPercent, request.CreatedBy.Trim(),
            DateTimeOffset.UtcNow, lines, SecurityClosePriceEvidence.Assess(lines, request.MaxSourceDispersionPercent));
        SecurityClosePriceEvidence.Validate(snapshot);
        await _store.RetainClosePriceSnapshotAsync(snapshot, ct).ConfigureAwait(false);
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
        await _store.RetainClosePriceApprovalAsync(retained.Snapshot.AccountId, retained.Snapshot.BookId,
            retained.Snapshot.ValuationDate, approval, ct).ConfigureAwait(false);
        return new(retained.Snapshot, approval);
    }

    public Task<SecurityClosePriceSetDto?> GetClosePriceSetAsync(
        string accountId, string bookId, DateOnly valuationDate, Guid snapshotId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(accountId) || string.IsNullOrWhiteSpace(bookId) || valuationDate == default || snapshotId == Guid.Empty)
            throw new ArgumentException("An exact account/book/date scope and snapshot identity are required.");
        return _store.GetClosePriceSetAsync(accountId.Trim(), bookId.Trim(), valuationDate, snapshotId, ct);
    }
}
