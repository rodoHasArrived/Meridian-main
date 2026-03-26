using Meridian.Application.Logging;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.FSharp.Ledger;
using Meridian.Ledger;
using Serilog;

namespace Meridian.Application.Services;

/// <summary>
/// Orchestrates Security Master, ledger, and portfolio joins for governance reconciliation runs.
/// Resolves security identifiers, builds projected cash flows from instrument terms, and
/// classifies breaks using the F# reconciliation kernel.
/// </summary>
public sealed class ReconciliationEngineService
{
    private readonly ISecurityMasterQueryService _securityMaster;
    private readonly ILogger _log = LoggingSetup.ForContext<ReconciliationEngineService>();

    public ReconciliationEngineService(ISecurityMasterQueryService securityMaster)
    {
        _securityMaster = securityMaster ?? throw new ArgumentNullException(nameof(securityMaster));
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs a full portfolio-vs-ledger reconciliation using the Security Master as the
    /// authoritative instrument source.  Returns a <see cref="EngineReconciliationResult"/>
    /// containing all matches and breaks together with security-enrichment metadata.
    /// </summary>
    public async Task<EngineReconciliationResult> RunAsync(
        EngineReconciliationRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var runId = Guid.NewGuid();
        var startedAt = DateTimeOffset.UtcNow;

        _log.Information(
            "Starting reconciliation run {RunId} for portfolio {PortfolioId} asOf {AsOf}",
            runId, request.PortfolioId, request.AsOf);

        // 1. Resolve each security identifier against the Security Master.
        var securityDetails = await ResolveSecuritiesAsync(request.SecurityIdentifiers, ct)
            .ConfigureAwait(false);

        // 2. Build portfolio-vs-ledger check candidates from the fund ledger.
        var candidates = BuildCandidates(request, securityDetails);

        // 3. Run through the F# reconciliation engine.
        var checkDtos = candidates
            .Select(c => new PortfolioLedgerCheckDto
            {
                CheckId          = c.CandidateId.ToString("N"),
                Label            = c.Label,
                ExpectedSource   = "portfolio",
                ActualSource     = "ledger",
                ExpectedAmount   = c.ExpectedAmount,
                ActualAmount     = c.ActualAmount,
                HasExpectedAmount = true,
                HasActualAmount  = true,
                ExpectedPresent  = c.ExpectedPresent,
                ActualPresent    = c.ActualPresent,
                ExpectedAsOf     = request.AsOf,
                ActualAsOf       = request.AsOf,
                HasExpectedAsOf  = true,
                HasActualAsOf    = true,
                CategoryHint     = c.CategoryHint,
                MissingSourceHint = string.Empty,
                ActualKind       = c.ActualKind
            })
            .ToArray();

        var results = LedgerInterop.ReconcilePortfolioLedgerChecks(
            request.AmountTolerance,
            request.MaxAsOfDriftMinutes,
            checkDtos);

        var matches = results.Where(r => r.IsMatch).ToArray();
        var breaks  = results.Where(r => !r.IsMatch).ToArray();

        var completedAt = DateTimeOffset.UtcNow;

        _log.Information(
            "Reconciliation run {RunId} completed: {Matches} matches, {Breaks} breaks in {ElapsedMs}ms",
            runId, matches.Length, breaks.Length,
            (long)(completedAt - startedAt).TotalMilliseconds);

        return new EngineReconciliationResult(
            RunId:          runId,
            PortfolioId:    request.PortfolioId,
            AsOf:           request.AsOf,
            StartedAt:      startedAt,
            CompletedAt:    completedAt,
            TotalChecks:    checkDtos.Length,
            MatchCount:     matches.Length,
            BreakCount:     breaks.Length,
            Matches:        matches,
            Breaks:         breaks,
            ResolvedSecurities: securityDetails);
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    private async Task<IReadOnlyDictionary<string, SecurityDetailDto>> ResolveSecuritiesAsync(
        IEnumerable<SecurityLookupKey> keys,
        CancellationToken ct)
    {
        var results = new Dictionary<string, SecurityDetailDto>(StringComparer.OrdinalIgnoreCase);

        foreach (var key in keys)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var detail = await _securityMaster
                    .GetByIdentifierAsync(key.IdentifierKind, key.IdentifierValue, key.Provider, ct)
                    .ConfigureAwait(false);

                if (detail is not null)
                    results[key.IdentifierValue] = detail;
            }
            catch (Exception ex)
            {
                _log.Warning(ex,
                    "Failed to resolve security {Kind}={Value} from Security Master",
                    key.IdentifierKind, key.IdentifierValue);
            }
        }

        return results;
    }

    private static List<ReconciliationCandidate> BuildCandidates(
        EngineReconciliationRequest request,
        IReadOnlyDictionary<string, SecurityDetailDto> securityDetails)
    {
        var candidates = new List<ReconciliationCandidate>();

        foreach (var position in request.PortfolioPositions)
        {
            securityDetails.TryGetValue(position.Symbol, out var security);

            var ledgerAmount = request.LedgerBalances.TryGetValue(position.Symbol, out var lb) ? lb : 0m;

            candidates.Add(new ReconciliationCandidate(
                CandidateId:    Guid.NewGuid(),
                Symbol:         position.Symbol,
                Label:          security?.DisplayName ?? position.Symbol,
                ExpectedAmount: position.MarketValue,
                ActualAmount:   ledgerAmount,
                ExpectedPresent: true,
                ActualPresent:  ledgerAmount != 0m,
                CategoryHint:  security?.AssetClass ?? "Unknown",
                ActualKind:    security?.AssetClass ?? string.Empty));
        }

        // Also surface ledger entries not present in portfolio
        foreach (var (symbol, balance) in request.LedgerBalances)
        {
            if (candidates.Any(c => string.Equals(c.Symbol, symbol, StringComparison.OrdinalIgnoreCase)))
                continue;

            securityDetails.TryGetValue(symbol, out var security);

            candidates.Add(new ReconciliationCandidate(
                CandidateId:    Guid.NewGuid(),
                Symbol:         symbol,
                Label:          security?.DisplayName ?? symbol,
                ExpectedAmount: 0m,
                ActualAmount:   balance,
                ExpectedPresent: false,
                ActualPresent:  true,
                CategoryHint:  security?.AssetClass ?? "Unknown",
                ActualKind:    security?.AssetClass ?? string.Empty));
        }

        return candidates;
    }

    // ── Private model ──────────────────────────────────────────────────────────
    private sealed record ReconciliationCandidate(
        Guid   CandidateId,
        string Symbol,
        string Label,
        decimal ExpectedAmount,
        decimal ActualAmount,
        bool   ExpectedPresent,
        bool   ActualPresent,
        string CategoryHint,
        string ActualKind);
}

// ── Request / result models ────────────────────────────────────────────────────

/// <summary>Lookup key for resolving a single security from the Security Master.</summary>
public sealed record SecurityLookupKey(
    SecurityIdentifierKind IdentifierKind,
    string IdentifierValue,
    string? Provider = null);

/// <summary>A single portfolio position passed into the reconciliation engine.</summary>
public sealed record PortfolioPositionInput(string Symbol, decimal MarketValue);

/// <summary>Request payload for <see cref="ReconciliationEngineService.RunAsync"/>.</summary>
public sealed record EngineReconciliationRequest(
    string PortfolioId,
    DateTimeOffset AsOf,
    IReadOnlyList<PortfolioPositionInput> PortfolioPositions,
    IReadOnlyDictionary<string, decimal> LedgerBalances,
    IEnumerable<SecurityLookupKey> SecurityIdentifiers,
    decimal AmountTolerance = 0.01m,
    int MaxAsOfDriftMinutes = 5);

/// <summary>Result of a single reconciliation engine run.</summary>
public sealed record EngineReconciliationResult(
    Guid RunId,
    string PortfolioId,
    DateTimeOffset AsOf,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    int TotalChecks,
    int MatchCount,
    int BreakCount,
    PortfolioLedgerCheckResultDto[] Matches,
    PortfolioLedgerCheckResultDto[] Breaks,
    IReadOnlyDictionary<string, SecurityDetailDto> ResolvedSecurities);
