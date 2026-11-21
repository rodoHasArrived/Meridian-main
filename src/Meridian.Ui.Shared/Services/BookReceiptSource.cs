using Meridian.Contracts.Api;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Tenancy;
using Meridian.Storage.AssetOperations;
using Meridian.Storage.Ledger;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Reads approved retained holdings and accepted schedules. The instrument query service's
/// illustrative Security Master projection is never an expectation source for this queue.
/// </summary>
public sealed partial class BookReceiptSource(
    IInstrumentPositionProjectionStore positions,
    IAssetOperationsProjectionStore projections,
    IBookReceiptEvidenceSource? evidenceSource = null,
    ILedgerJournalStore? journals = null,
    IFundProfileTenancyRegistry? ownership = null) : IBookReceiptSource
{
    public async Task<BookReceiptSourceSnapshotDto> LoadAsync(BookReceiptQueryDto query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Scope.LedgerBookId == Guid.Empty || query.AsOfDate == default ||
            string.IsNullOrWhiteSpace(query.Scope.TenantId) || string.IsNullOrWhiteSpace(query.Scope.CompanyId))
            throw new ArgumentException("An exact tenant, company, book and observation date are required.", nameof(query));
        if (journals is null || ownership is null)
            return Unavailable("Authoritative book ownership is unavailable.");
        var book = await journals.GetLedgerBookAsync(query.Scope.LedgerBookId, ct).ConfigureAwait(false);
        var owner = book is null ? null : await ownership.ResolveAsync(book.FundProfileId, ct).ConfigureAwait(false);
        if (book is null || owner is null)
            return Unavailable("The book does not have retained ownership evidence.");
        if (book.LedgerBookId != query.Scope.LedgerBookId || owner.FundProfileId != book.FundProfileId ||
            !owner.IsHeldBy(query.Scope.TenantId) ||
            !string.Equals(owner.CompanyId, query.Scope.CompanyId, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("The book is outside the receipt observation scope.");

        var holdingsIssues = new List<string>();
        var projectionIssues = new List<string>();
        var expectations = new List<BookReceiptExpectationDto>();
        var snapshots = await positions.GetBookAsync(query.Scope.LedgerBookId, query.AsOfDate, ct).ConfigureAwait(false);
        if (snapshots.Count == 0)
            holdingsIssues.Add("No authoritative book holdings are available for this observation date.");
        foreach (var snapshot in snapshots.OrderBy(static row => row.SecurityId))
        {
            if (snapshot.LedgerBookId != query.Scope.LedgerBookId || snapshot.AsOfDate != query.AsOfDate)
            {
                holdingsIssues.Add("A holdings snapshot did not match the requested book and date.");
                continue;
            }
            var validPositions = snapshot.BookPositions.Where(position =>
                position.BookContext.LedgerBookId == book.LedgerBookId && position.SecurityId == snapshot.SecurityId &&
                position.BookContext.FundProfileId == book.FundProfileId &&
                position.BookContext.FundStructureNodeId == book.FundStructureNodeId &&
                position.BookContext.FundStructureNodeKind == book.FundStructureNodeKind).ToArray();
            if (validPositions.Length != snapshot.BookPositions.Count)
                holdingsIssues.Add("A holding did not match the canonical book's retained owner and entity.");
            if (validPositions.Length == 0)
                continue;
            var detail = await projections.GetAsync(snapshot.SecurityId, ct).ConfigureAwait(false);
            if (detail is null || detail.Subject.SecurityId != snapshot.SecurityId)
            {
                projectionIssues.Add($"{snapshot.SecurityId:D}: no published projection is available.");
                continue;
            }
            foreach (var position in validPositions.OrderBy(static row => row.PositionId))
            {
                var role = snapshot.InstrumentRoles.SingleOrDefault(row => row.RoleId == position.RoleId);
                if (role is null || !ReceivesCash(role, position))
                    continue;
                if (position.Status is not ("Active" or "Open") && position.EffectiveTo is null)
                {
                    holdingsIssues.Add($"{position.PositionId:D}: a closed holding lacks its effective closure date.");
                    continue;
                }
                var candidates = new List<(AssetCashFlowProjectionRunDto Run, BookReceiptExpectationDto Expectation)>();
                foreach (var flow in detail.ProjectedCashFlows.OrderBy(static row => row.ProjectedCashFlowId))
                {
                    var flowType = NormalizeFlowType(flow.FlowType);
                    if (flowType is null || flow.Amount <= 0 || flow.SecurityId != snapshot.SecurityId ||
                        flow.DueDate == default || flow.DueDate < position.EffectiveFrom || flow.DueDate > position.EffectiveTo ||
                        !AcceptedFlowStatus(flow.Status))
                        continue;
                    var run = detail.CashFlowProjectionRuns.SingleOrDefault(row =>
                        row.ProjectionRunId == flow.ProjectionRunId && row.SecurityId == snapshot.SecurityId);
                    if (run is null || run.ProjectionAsOf > query.AsOfDate ||
                        !KnownBy(run.GeneratedAt, query.AsOfDate) || !AcceptedRunStatus(run.Status))
                        continue;
                    var projectionScope = ResolveProjectionScope(detail, snapshot, run, flow, position.PositionId, query.AsOfDate);
                    if (!projectionScope.Applies)
                    {
                        if (projectionScope.Issue is not null)
                            projectionIssues.Add(projectionScope.Issue);
                        continue;
                    }
                    var evidence = run.RetainedEvidence.Concat(flow.RetainedEvidence)
                        .Where(item => AcceptedProjectionEvidence(item, run, flow, query.AsOfDate))
                        .DistinctBy(static item => item.EvidenceId, StringComparer.Ordinal)
                        .OrderBy(static item => item.EvidenceId, StringComparer.Ordinal).ToArray();
                    if (evidence.Length == 0)
                    {
                        projectionIssues.Add($"{snapshot.SecurityId:D}: projection {run.ProjectionRunId:D} lacks accepted schedule evidence.");
                        continue;
                    }
                    var terms = ResolveTerms(detail, snapshot, run, flow, position.PositionId, query.AsOfDate);
                    if (terms is null)
                    {
                        projectionIssues.Add($"{snapshot.SecurityId:D}: accepted projection terms cannot be identified unambiguously.");
                        continue;
                    }
                    if (flow.DueDate < terms.EffectiveDate)
                        continue;
                    // A redemption recorded on payment day must not erase principal still due.
                    var holdingDate = flow.DueDate <= query.AsOfDate ? flow.DueDate.AddDays(-1) : query.AsOfDate;
                    var state = snapshot.PositionEconomicStates
                        .Where(row => row.PositionId == position.PositionId && row.AsOfDate <= holdingDate)
                        .Where(row => row.SourceEvent is null || KnownBy(row.SourceEvent.OccurredAtUtc, query.AsOfDate))
                        .Where(row => row.ProjectionLineage is null || KnownBy(row.ProjectionLineage.GeneratedAtUtc, query.AsOfDate))
                        .OrderByDescending(static row => row.AsOfDate).ThenByDescending(static row => row.Version)
                        .ThenByDescending(static row => row.EconomicStateId).FirstOrDefault();
                    if (state is null || !HasHoldingProvenance(position, state))
                    {
                        holdingsIssues.Add($"{position.PositionId:D}: no approved economic state covers {holdingDate:yyyy-MM-dd}.");
                        continue;
                    }
                    if (!string.Equals(state.Currency, flow.Currency, StringComparison.OrdinalIgnoreCase))
                    {
                        projectionIssues.Add($"{position.PositionId:D}: holding and schedule currencies differ.");
                        continue;
                    }
                    var principal = state.CurrentFaceAmount ?? state.ParAmount ?? state.NotionalAmount;
                    var positionBound = projectionScope.PositionSized;
                    // Future amortization changes projected principal, not the owner's share of
                    // the schedule. Anchor scaling at the first still-future principal basis.
                    var sizingBasis = flow.DueDate <= query.AsOfDate ? flow.PrincipalBasis
                        : detail.ProjectedCashFlows.Where(row => row.ProjectionRunId == run.ProjectionRunId &&
                                row.SecurityId == snapshot.SecurityId && row.DueDate > query.AsOfDate &&
                                NormalizeFlowType(row.FlowType) is not null && AcceptedFlowStatus(row.Status) && row.Amount > 0)
                            .OrderBy(static row => row.DueDate).ThenBy(static row => row.SequenceNumber)
                            .Select(row => run.RetainedEvidence.Concat(row.RetainedEvidence)
                                .Any(item => AcceptedProjectionEvidence(item, run, row, query.AsOfDate))
                                    ? row.PrincipalBasis : null).FirstOrDefault();
                    if (principal is null || principal < 0 || (!positionBound && sizingBasis is not > 0))
                    {
                        projectionIssues.Add($"{position.PositionId:D}: a holding principal and schedule sizing basis are required.");
                        continue;
                    }
                    if (principal == 0)
                        continue;
                    var amount = positionBound ? flow.Amount : flow.Amount * principal.Value / sizingBasis!.Value;
                    var links = evidence.Select(static item => item.EvidenceUri).Concat(position.EvidenceLinks)
                        .Concat(state.EvidenceLinks).Append(UiApiRoutes.WorkstationAssetOperations.Replace(
                            "{securityId:guid}", snapshot.SecurityId.ToString("D"), StringComparison.Ordinal))
                        .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
                    candidates.Add((run, new BookReceiptExpectationDto(
                        ObligationKey(position.PositionId, flowType, flow.DueDate, flow.AccrualStartDate, flow.AccrualEndDate,
                            ReadText(flow.ExtensionPayload, "obligationId") ?? ReadText(flow.ExtensionPayload, "schedulePeriodId"),
                            flow.FlowType.Trim().ToUpperInvariant() is "MATURITY" or "REDEMPTION"),
                        position.PositionId, snapshot.SecurityId, detail.Subject.DisplayName, flowType, flow.DueDate,
                        flow.Currency.ToUpperInvariant(), state.Quantity ?? principal.Value, amount, terms.TermsVersionId,
                        terms.TermsHash, run.ProjectionRunId, flow.ProjectedCashFlowId, links, evidence)));
                }
                foreach (var group in candidates.GroupBy(static row => row.Expectation.ObligationKey, StringComparer.Ordinal))
                {
                    var chosen = group.OrderByDescending(row => row.Run.ProjectionAsOf).ThenByDescending(row => row.Run.GeneratedAt)
                        .ThenByDescending(row => row.Run.ProjectionRunId).First();
                    if (group.Count(row => row.Run.ProjectionRunId == chosen.Run.ProjectionRunId) != 1)
                    {
                        projectionIssues.Add($"{position.PositionId:D}: duplicate schedule obligations need a retained source identity.");
                        continue;
                    }
                    expectations.Add(chosen.Expectation);
                }
                if (candidates.Count == 0)
                    projectionIssues.Add($"{position.PositionId:D}: no accepted receipt schedule is available.");
            }
        }
        var ordered = expectations.OrderBy(static row => row.ObligationKey, StringComparer.Ordinal).ToArray();
        var evidenceSnapshot = evidenceSource is null
            ? new BookReceiptSourceSnapshotDto([], [], [],
                [new("Receipts", false, "No provider receipt source is configured."), new("Postings", false, "No posted receipt source is configured.")])
            : await evidenceSource.LoadAsync(query, ordered, ct).ConfigureAwait(false);
        return new BookReceiptSourceSnapshotDto(ordered, evidenceSnapshot.Receipts, evidenceSnapshot.Postings,
        [
            new("Holdings", holdingsIssues.Count == 0, JoinIssues(holdingsIssues)),
            new("Projections", projectionIssues.Count == 0 && snapshots.Count > 0,
                snapshots.Count == 0 ? "Projection coverage cannot be established without book holdings." : JoinIssues(projectionIssues)),
            .. evidenceSnapshot.Coverage
        ]);
    }

    private static BookReceiptSourceSnapshotDto Unavailable(string reason)
        => new([], [], [], [new("Holdings", false, reason), new("Projections", false, reason),
            new("Receipts", false, reason), new("Postings", false, reason)]);
}
