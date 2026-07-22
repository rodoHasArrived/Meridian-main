using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Meridian.Contracts.Operations;
using Meridian.Contracts.Workstation;

namespace Meridian.Strategies.Services;

public interface IShadowBookValuationService
{
    Task<ShadowBookValuationResult> RunAsync(ShadowBookValuationRequest request, CancellationToken ct = default);
}

public sealed record ShadowBookValuationRequest(
    string AccountId,
    string StrategyProfile,
    DateOnly PeriodEnd,
    IReadOnlyList<ShadowBookPositionInput> Positions,
    IReadOnlyDictionary<string, decimal> PricesBySymbol,
    IReadOnlyDictionary<string, decimal> FxRatesByCurrency,
    decimal Fees,
    decimal Accruals,
    decimal ExternalReferenceNav,
    bool BlockCloseOnBreach,
    string OperatorId,
    Guid? LedgerBookId = null,
    string? BaseCurrency = null,
    Guid? AccountingPeriodId = null);

public sealed record ShadowBookPositionInput(string Symbol, decimal Quantity, string PriceCurrency);
public sealed record ShadowBookVarianceThresholds(decimal AbsoluteMateriality, decimal BpsMateriality);
public sealed record ShadowBookRunSnapshot(string RunId, string VersionHash, DateTimeOffset CreatedAt, string AccountId, DateOnly PeriodEnd, decimal ShadowNav, decimal ExternalNav, decimal VarianceAmount, decimal VarianceBps, bool Breached);
public sealed record ShadowBookValuationResult(
    ShadowBookRunSnapshot? Snapshot,
    bool CloseBlocked,
    ReconciliationBreakQueueItem? EmittedBreak,
    VerifiedOperationOutcome Outcome);

public sealed class ShadowBookValuationService : IShadowBookValuationService
{
    private const string OperationKind = "strategy.shadow-book-valuation";

    private readonly IReconciliationBreakQueueRepository _breakQueueRepository;
    private readonly Dictionary<string, ShadowBookVarianceThresholds> _thresholds;
    private readonly List<ShadowBookRunSnapshot> _snapshots = [];
    private readonly object _snapshotGate = new();

    public ShadowBookValuationService(IReconciliationBreakQueueRepository breakQueueRepository, IReadOnlyDictionary<string, ShadowBookVarianceThresholds>? thresholds = null)
    {
        _breakQueueRepository = breakQueueRepository;
        _thresholds = new(StringComparer.OrdinalIgnoreCase)
        {
            ["default"] = new ShadowBookVarianceThresholds(100m, 25m)
        };

        if (thresholds is not null)
        {
            foreach (var pair in thresholds)
            {
                _thresholds[pair.Key] = pair.Value;
            }
        }
    }

    public async Task<ShadowBookValuationResult> RunAsync(ShadowBookValuationRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var startedAtUtc = DateTimeOffset.UtcNow;
        var threshold = ResolveThreshold(request.StrategyProfile);
        var inputHash = ComputeInputHash(request, threshold);
        var operationId = $"shadow-book-valuation:{inputHash[..16]}";

        if (!TryValidateAndNormalize(request, threshold, out var input, out var prerequisiteFailure))
        {
            var completedAtUtc = DateTimeOffset.UtcNow;
            var outcome = CreateUnsuccessfulOutcome(
                operationId,
                request,
                inputHash,
                startedAtUtc,
                completedAtUtc,
                OperationTerminalState.Blocked,
                prerequisiteFailure!.Code,
                prerequisiteFailure.Message,
                prerequisiteFailure.RecoveryGuidance,
                exceptionType: null);
            return new ShadowBookValuationResult(
                Snapshot: null,
                CloseBlocked: true,
                EmittedBreak: null,
                Outcome: outcome);
        }

        decimal shadowNav;
        decimal varianceAmount;
        decimal varianceBps;
        bool breached;
        try
        {
            decimal gross = 0m;
            foreach (var position in input!.Positions)
            {
                var price = input.PricesBySymbol[position.Symbol];
                var fx = string.Equals(position.PriceCurrency, input.BaseCurrency, StringComparison.Ordinal)
                    ? 1m
                    : input.FxRatesByCurrency[position.PriceCurrency];
                gross += position.Quantity * price * fx;
            }

            shadowNav = gross - request.Fees + request.Accruals;
            varianceAmount = shadowNav - request.ExternalReferenceNav;
            varianceBps = request.ExternalReferenceNav == 0m
                ? 0m
                : varianceAmount / request.ExternalReferenceNav * 10_000m;
            breached = Math.Abs(varianceAmount) >= threshold.AbsoluteMateriality
                || Math.Abs(varianceBps) >= threshold.BpsMateriality;
        }
        catch (Exception ex) when (ex is OverflowException or DivideByZeroException)
        {
            var completedAtUtc = DateTimeOffset.UtcNow;
            var outcome = CreateUnsuccessfulOutcome(
                operationId,
                request,
                inputHash,
                startedAtUtc,
                completedAtUtc,
                OperationTerminalState.Failed,
                "shadow-valuation-calculation-failed",
                "The shadow-book valuation could not be computed from the admitted numeric input.",
                "Review position quantities, prices, FX rates, fees, accruals, and external NAV for invalid scale, then retry.",
                ex.GetType().FullName);
            return new ShadowBookValuationResult(
                Snapshot: null,
                CloseBlocked: true,
                EmittedBreak: null,
                Outcome: outcome);
        }

        var calculatedAtUtc = DateTimeOffset.UtcNow;
        var runId = $"shadow-nav-{request.PeriodEnd:yyyyMMdd}-{inputHash[..16]}";
        var snapshot = new ShadowBookRunSnapshot(
            runId,
            inputHash,
            calculatedAtUtc,
            input.AccountId,
            request.PeriodEnd,
            shadowNav,
            request.ExternalReferenceNav,
            varianceAmount,
            varianceBps,
            breached);

        ReconciliationBreakQueueItem? retainedBreak = null;
        if (breached)
        {
            var emittedBreak = CreateBreak(request, input, threshold, snapshot);
            try
            {
                await _breakQueueRepository.CreateIfMissingAsync(emittedBreak, ct).ConfigureAwait(false);
                retainedBreak = await _breakQueueRepository.GetByIdAsync(emittedBreak.BreakId, ct).ConfigureAwait(false);
                if (!IsExpectedRetainedBreak(retainedBreak, emittedBreak))
                {
                    throw new InvalidOperationException(
                        $"Reconciliation break '{emittedBreak.BreakId}' was not durably read back with the expected source and accounting scope.");
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                var completedAtUtc = DateTimeOffset.UtcNow;
                var conflict = ex is InvalidOperationException
                    && ex.Message.Contains("different source or scope input", StringComparison.OrdinalIgnoreCase);
                var outcome = CreateUnsuccessfulOutcome(
                    operationId,
                    request,
                    inputHash,
                    startedAtUtc,
                    completedAtUtc,
                    OperationTerminalState.Failed,
                    conflict ? "shadow-break-persistence-conflict" : "shadow-break-persistence-failed",
                    conflict
                        ? "The reconciliation case identity is already bound to different valuation input or accounting scope."
                        : "The breached valuation could not be verified in the durable reconciliation queue.",
                    conflict
                        ? "Inspect the retained case and source fingerprint, correct the conflicting identity or scope, then retry the exact valuation request."
                        : "Repair reconciliation queue persistence, verify storage health, then retry the exact valuation request.",
                    ex.GetType().FullName);
                return new ShadowBookValuationResult(
                    Snapshot: null,
                    CloseBlocked: true,
                    EmittedBreak: null,
                    Outcome: outcome);
            }
        }

        PublishSnapshot(snapshot);
        var succeededAtUtc = DateTimeOffset.UtcNow;
        return new ShadowBookValuationResult(
            snapshot,
            request.BlockCloseOnBreach && breached,
            retainedBreak,
            CreateSucceededOutcome(
                operationId,
                request,
                inputHash,
                startedAtUtc,
                succeededAtUtc,
                snapshot,
                retainedBreak));
    }

    private ShadowBookVarianceThresholds ResolveThreshold(string? strategyProfile)
        => !string.IsNullOrWhiteSpace(strategyProfile)
            && _thresholds.TryGetValue(strategyProfile.Trim(), out var configured)
                ? configured
                : _thresholds["default"];

    private static bool TryValidateAndNormalize(
        ShadowBookValuationRequest request,
        ShadowBookVarianceThresholds threshold,
        out ValidatedInput? input,
        out PrerequisiteFailure? failure)
    {
        input = null;
        failure = null;

        if (string.IsNullOrWhiteSpace(request.AccountId))
            return Fail("shadow-account-scope-missing", "Shadow-book valuation requires an account identity.", "Select the account scope and retry.", out failure);
        if (string.IsNullOrWhiteSpace(request.StrategyProfile))
            return Fail("shadow-strategy-profile-missing", "Shadow-book valuation requires a strategy profile.", "Select a governed strategy profile and retry.", out failure);
        if (request.LedgerBookId is null || request.LedgerBookId == Guid.Empty)
            return Fail("shadow-ledger-book-scope-missing", "Shadow-book valuation requires the exact ledger book identity.", "Resolve one authoritative ledger book for the account and retry.", out failure);
        if (request.AccountingPeriodId is null || request.AccountingPeriodId == Guid.Empty)
            return Fail("shadow-accounting-period-scope-missing", "Shadow-book valuation requires the exact accounting period identity.", "Resolve the accounting period that contains the valuation date and retry.", out failure);
        if (string.IsNullOrWhiteSpace(request.OperatorId))
            return Fail("shadow-operator-missing", "Shadow-book valuation requires an operator identity for emitted case assignment.", "Authenticate an operator and retry.", out failure);
        if (threshold.AbsoluteMateriality < 0m || threshold.BpsMateriality < 0m)
            return Fail("shadow-threshold-invalid", "Shadow-book variance thresholds cannot be negative.", "Repair the strategy-profile threshold configuration and retry.", out failure);

        var baseCurrency = NormalizeCurrency(request.BaseCurrency);
        if (baseCurrency is null)
            return Fail("shadow-base-currency-invalid", "Shadow-book valuation requires a three-letter base currency.", "Select the authoritative ledger-book base currency and retry.", out failure);
        if (request.Positions is null)
            return Fail("shadow-positions-missing", "Shadow-book valuation requires a position collection.", "Load the authoritative position set and retry.", out failure);
        if (!TryNormalizeMap(request.PricesBySymbol, "price", out var prices, out failure))
            return false;
        if (!TryNormalizeMap(request.FxRatesByCurrency, "FX rate", out var fxRates, out failure))
            return false;

        var positions = new List<NormalizedPosition>(request.Positions.Count);
        foreach (var position in request.Positions)
        {
            if (position is null || string.IsNullOrWhiteSpace(position.Symbol))
                return Fail("shadow-position-symbol-missing", "Every shadow-book position requires a symbol.", "Repair the position identity and retry.", out failure);

            var symbol = position.Symbol.Trim().ToUpperInvariant();
            var priceCurrency = NormalizeCurrency(position.PriceCurrency);
            if (priceCurrency is null)
                return Fail("shadow-position-currency-invalid", $"Position '{symbol}' requires a three-letter price currency.", "Repair the position currency and retry.", out failure);
            if (!prices!.TryGetValue(symbol, out var price))
                return Fail("shadow-price-missing", $"No price was supplied for position '{symbol}'.", "Load and retain an authoritative price for every position, then retry.", out failure);
            if (price <= 0m)
                return Fail("shadow-price-invalid", $"Price for position '{symbol}' must be positive.", "Repair the invalid price evidence and retry.", out failure);

            if (string.Equals(priceCurrency, baseCurrency, StringComparison.Ordinal))
            {
                if (fxRates!.TryGetValue(priceCurrency, out var baseFx) && baseFx != 1m)
                    return Fail("shadow-base-fx-invalid", $"Base-currency FX rate for '{baseCurrency}' must equal 1.", "Repair the base-currency FX evidence and retry.", out failure);
            }
            else if (!fxRates!.TryGetValue(priceCurrency, out var fx))
            {
                return Fail("shadow-fx-rate-missing", $"No {priceCurrency}/{baseCurrency} FX rate was supplied for position '{symbol}'.", "Load and retain an authoritative non-base FX rate, then retry.", out failure);
            }
            else if (fx <= 0m)
            {
                return Fail("shadow-fx-rate-invalid", $"FX rate for '{priceCurrency}' must be positive.", "Repair the invalid FX evidence and retry.", out failure);
            }

            positions.Add(new NormalizedPosition(symbol, position.Quantity, priceCurrency));
        }

        input = new ValidatedInput(
            request.AccountId.Trim(),
            request.StrategyProfile.Trim(),
            request.OperatorId.Trim(),
            baseCurrency,
            positions
                .OrderBy(static position => position.Symbol, StringComparer.Ordinal)
                .ThenBy(static position => position.PriceCurrency, StringComparer.Ordinal)
                .ThenBy(static position => position.Quantity)
                .ToArray(),
            prices!,
            fxRates!);
        return true;
    }

    private static bool TryNormalizeMap(
        IReadOnlyDictionary<string, decimal>? source,
        string valueLabel,
        out Dictionary<string, decimal>? normalized,
        out PrerequisiteFailure? failure)
    {
        normalized = null;
        failure = null;
        if (source is null)
            return Fail($"shadow-{valueLabel.Replace(' ', '-').ToLowerInvariant()}-map-missing", $"Shadow-book valuation requires a {valueLabel} map.", $"Load the authoritative {valueLabel} evidence and retry.", out failure);

        var values = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var pair in source)
        {
            var key = pair.Key?.Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(key))
                return Fail($"shadow-{valueLabel.Replace(' ', '-').ToLowerInvariant()}-key-missing", $"The {valueLabel} map contains an empty key.", $"Repair the {valueLabel} evidence and retry.", out failure);
            if (!values.TryAdd(key, pair.Value))
                return Fail($"shadow-{valueLabel.Replace(' ', '-').ToLowerInvariant()}-key-duplicate", $"The {valueLabel} map contains duplicate canonical key '{key}'.", $"Remove the ambiguous {valueLabel} entry and retry.", out failure);
        }

        normalized = values;
        return true;
    }

    private static bool Fail(
        string code,
        string message,
        string recoveryGuidance,
        out PrerequisiteFailure? failure)
    {
        failure = new PrerequisiteFailure(code, message, recoveryGuidance);
        return false;
    }

    private static string? NormalizeCurrency(string? currency)
    {
        if (string.IsNullOrWhiteSpace(currency))
            return null;
        var normalized = currency.Trim().ToUpperInvariant();
        return normalized.Length == 3 && normalized.All(static value => value is >= 'A' and <= 'Z')
            ? normalized
            : null;
    }

    private static ReconciliationBreakQueueItem CreateBreak(
        ShadowBookValuationRequest request,
        ValidatedInput input,
        ShadowBookVarianceThresholds threshold,
        ShadowBookRunSnapshot snapshot)
    {
        var evidenceLink = $"urn:sha256:{snapshot.VersionHash}";
        var blockedOutputs = request.BlockCloseOnBreach
            ? (IReadOnlyList<string>)["accounting-close", "certified-reporting"]
            : ["certified-reporting"];
        return new ReconciliationBreakQueueItem(
            BreakId: $"shadow-nav:{input.AccountId}:{request.PeriodEnd:yyyyMMdd}:{snapshot.VersionHash[..12]}",
            RunId: snapshot.RunId,
            StrategyName: input.StrategyProfile,
            Category: ReconciliationBreakCategory.AmountMismatch,
            Status: ReconciliationBreakQueueStatus.Open,
            Variance: snapshot.VarianceAmount,
            Reason: $"Shadow NAV variance breached ({snapshot.VarianceAmount.ToString("F2", CultureInfo.InvariantCulture)} / {snapshot.VarianceBps.ToString("F1", CultureInfo.InvariantCulture)} bps)",
            DetectedAt: snapshot.CreatedAt,
            LastUpdatedAt: snapshot.CreatedAt,
            Severity: Math.Abs(snapshot.VarianceBps) >= threshold.BpsMateriality * 2
                ? ReconciliationBreakSeverity.Critical
                : ReconciliationBreakSeverity.High,
            ExceptionRoute: "accounting/reconciliation/shadow-nav",
            ToleranceBand: threshold.AbsoluteMateriality,
            RequiredSignoffRole: "Fund accounting",
            SignoffStatus: "pending-signoff",
            FundAccountId: input.AccountId,
            ExplainabilitySummary: $"version={snapshot.VersionHash[..12]}, shadowNav={snapshot.ShadowNav.ToString("F2", CultureInfo.InvariantCulture)}, externalNav={request.ExternalReferenceNav.ToString("F2", CultureInfo.InvariantCulture)}",
            CustodianId: null,
            UpstreamSyncCursor: snapshot.VersionHash,
            AssignedTo: input.OperatorId,
            ReviewedBy: null,
            ReviewedAt: null,
            ResolvedBy: null,
            ResolvedAt: null,
            ResolutionNote: $"Policy threshold breached for strategy profile '{input.StrategyProfile}'.",
            LifecycleState: ReconciliationCaseLifecycleState.Open,
            LifecycleRationale: "Auto-generated from shadow-book valuation variance.",
            StateTransitions: [],
            EvidenceLinks: [evidenceLink],
            SourceType: "shadow-book-valuation",
            SourceSystem: "Meridian.Strategies",
            SourceReference: snapshot.RunId,
            SourceFingerprint: snapshot.VersionHash,
            LedgerBookId: request.LedgerBookId,
            Measures:
            [
                new ReconciliationBreakMeasureDto(
                    ReconciliationBreakMeasureKindDto.Value,
                    Expected: request.ExternalReferenceNav,
                    Actual: snapshot.ShadowNav,
                    Variance: snapshot.VarianceAmount,
                    Tolerance: threshold.AbsoluteMateriality,
                    Unit: input.BaseCurrency),
                new ReconciliationBreakMeasureDto(
                    ReconciliationBreakMeasureKindDto.Quantity,
                    Expected: null,
                    Actual: null,
                    Variance: null,
                    Tolerance: null,
                    Unit: "units",
                    UnavailableReason: "The external NAV reference does not include item-level position quantities."),
                new ReconciliationBreakMeasureDto(
                    ReconciliationBreakMeasureKindDto.CostBasis,
                    Expected: null,
                    Actual: null,
                    Variance: null,
                    Tolerance: null,
                    Unit: input.BaseCurrency,
                    UnavailableReason: "The shadow-book request does not include an external cost-basis reference.")
            ],
            BlockedOutputs: blockedOutputs,
            AccountingPeriodId: request.AccountingPeriodId!.Value.ToString("D"),
            AsOfDate: request.PeriodEnd);
    }

    private static bool IsExpectedRetainedBreak(
        ReconciliationBreakQueueItem? retained,
        ReconciliationBreakQueueItem expected)
        => retained is not null
            && string.Equals(retained.BreakId, expected.BreakId, StringComparison.Ordinal)
            && string.Equals(retained.SourceFingerprint, expected.SourceFingerprint, StringComparison.Ordinal)
            && retained.LedgerBookId == expected.LedgerBookId
            && string.Equals(retained.AccountingPeriodId, expected.AccountingPeriodId, StringComparison.Ordinal)
            && retained.AsOfDate == expected.AsOfDate;

    private void PublishSnapshot(ShadowBookRunSnapshot snapshot)
    {
        lock (_snapshotGate)
        {
            if (_snapshots.All(existing => !string.Equals(existing.VersionHash, snapshot.VersionHash, StringComparison.Ordinal)))
                _snapshots.Add(snapshot);
        }
    }

    private static VerifiedOperationOutcome CreateSucceededOutcome(
        string operationId,
        ShadowBookValuationRequest request,
        string inputHash,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc,
        ShadowBookRunSnapshot snapshot,
        ReconciliationBreakQueueItem? retainedBreak)
    {
        var inputEvidenceId = "shadow-valuation-input";
        var snapshotEvidenceId = "shadow-valuation-snapshot";
        var evidence = new List<OperationEvidenceReference>
        {
            CreateInputEvidence(inputEvidenceId, inputHash, completedAtUtc),
            new(
                snapshotEvidenceId,
                "valuation-snapshot",
                "Canonical shadow-book valuation snapshot returned to the caller.",
                Uri: $"urn:sha256:{snapshot.VersionHash}",
                ContentHashSha256: snapshot.VersionHash,
                CapturedAtUtc: snapshot.CreatedAt)
        };
        var postconditions = new List<OperationPostcondition>
        {
            new(
                "valuation-inputs-resolved",
                "Ledger book, accounting period, prices, and required FX rates were resolved.",
                OperationPostconditionState.Satisfied,
                Required: true,
                EvidenceIds: [inputEvidenceId]),
            new(
                "shadow-valuation-computed",
                "The shadow NAV and variance were computed from the canonical input.",
                OperationPostconditionState.Satisfied,
                Required: true,
                EvidenceIds: [snapshotEvidenceId])
        };

        if (snapshot.Breached)
        {
            var breakEvidenceId = "shadow-reconciliation-case";
            evidence.Add(new OperationEvidenceReference(
                breakEvidenceId,
                "reconciliation-case",
                $"Durably read back reconciliation case '{retainedBreak!.BreakId}' for the breached valuation.",
                Uri: $"urn:meridian:reconciliation-break:{Uri.EscapeDataString(retainedBreak.BreakId)}",
                ContentHashSha256: inputHash,
                CapturedAtUtc: completedAtUtc));
            postconditions.Add(new OperationPostcondition(
                "breach-case-retained",
                "A breached valuation was retained in the reconciliation case queue before publication.",
                OperationPostconditionState.Satisfied,
                Required: true,
                EvidenceIds: [breakEvidenceId]));
        }

        return VerifiedOperationOutcomeValidator.ValidateAndThrow(new VerifiedOperationOutcome(
            operationId,
            OperationKind,
            OperationTerminalState.Succeeded,
            startedAtUtc,
            completedAtUtc,
            AttemptNumber: 1,
            CorrelationId: BuildCorrelationId(request),
            InputHashSha256: inputHash,
            Postconditions: postconditions,
            Evidence: evidence,
            Artifacts: [],
            Issues: [],
            Recovery: []));
    }

    private static VerifiedOperationOutcome CreateUnsuccessfulOutcome(
        string operationId,
        ShadowBookValuationRequest request,
        string inputHash,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc,
        OperationTerminalState terminalState,
        string issueCode,
        string message,
        string recoveryGuidance,
        string? exceptionType)
    {
        var evidenceId = "shadow-valuation-input";
        var failed = terminalState == OperationTerminalState.Failed;
        var persistenceFailure = failed && issueCode.StartsWith("shadow-break-persistence-", StringComparison.Ordinal);
        return VerifiedOperationOutcomeValidator.ValidateAndThrow(new VerifiedOperationOutcome(
            operationId,
            OperationKind,
            terminalState,
            startedAtUtc,
            completedAtUtc,
            AttemptNumber: 1,
            CorrelationId: BuildCorrelationId(request),
            InputHashSha256: inputHash,
            Postconditions:
            [
                new OperationPostcondition(
                    persistenceFailure
                        ? "breach-case-retained"
                        : failed
                            ? "shadow-valuation-computed"
                            : "valuation-prerequisites-resolved",
                    persistenceFailure
                        ? "Any breached valuation must be verified in the durable reconciliation queue before publication."
                        : failed
                            ? "The shadow NAV and variance must be computed from the canonical input."
                            : "All accounting scope and market-data prerequisites must be resolved before valuation.",
                    OperationPostconditionState.NotSatisfied,
                    Required: true,
                    EvidenceIds: [evidenceId])
            ],
            Evidence: [CreateInputEvidence(evidenceId, inputHash, completedAtUtc)],
            Artifacts: [],
            Issues:
            [
                new OperationIssue(
                    issueCode,
                    message,
                    OperationIssueSeverity.Error,
                    exceptionType,
                    evidenceId)
                {
                    IsBlocking = !failed
                }
            ],
            Recovery:
            [
                new OperationRecoveryAction(
                    "repair-and-retry-shadow-valuation",
                    "Repair and retry",
                    recoveryGuidance,
                    Retryable: true,
                    RequiresHumanAction: true,
                    Route: "/accounting/reconciliation")
                {
                    EvidenceIds = [evidenceId]
                }
            ]));
    }

    private static OperationEvidenceReference CreateInputEvidence(
        string evidenceId,
        string inputHash,
        DateTimeOffset capturedAtUtc)
        => new(
            evidenceId,
            "canonical-input-receipt",
            "Canonical hash of the shadow-book request, accounting scope, variance policy, and resolved thresholds.",
            Uri: $"urn:sha256:{inputHash}",
            ContentHashSha256: inputHash,
            CapturedAtUtc: capturedAtUtc);

    private static string BuildCorrelationId(ShadowBookValuationRequest request)
    {
        var accountId = string.IsNullOrWhiteSpace(request.AccountId) ? "unresolved-account" : request.AccountId.Trim();
        return $"shadow-book:{accountId}:{request.PeriodEnd:yyyy-MM-dd}";
    }

    private static string ComputeInputHash(
        ShadowBookValuationRequest request,
        ShadowBookVarianceThresholds threshold)
    {
        var builder = new StringBuilder("meridian.shadow-book-valuation.v2\n");
        Append(builder, "account", request.AccountId?.Trim());
        Append(builder, "strategy", request.StrategyProfile?.Trim().ToUpperInvariant());
        Append(builder, "periodEnd", request.PeriodEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Append(builder, "ledgerBook", request.LedgerBookId?.ToString("D"));
        Append(builder, "accountingPeriod", request.AccountingPeriodId?.ToString("D"));
        Append(builder, "baseCurrency", request.BaseCurrency?.Trim().ToUpperInvariant());
        Append(builder, "blockCloseOnBreach", request.BlockCloseOnBreach ? "true" : "false");
        Append(builder, "absoluteMateriality", FormatDecimal(threshold.AbsoluteMateriality));
        Append(builder, "bpsMateriality", FormatDecimal(threshold.BpsMateriality));

        foreach (var position in (request.Positions ?? [])
                     .OrderBy(static item => item?.Symbol?.Trim().ToUpperInvariant(), StringComparer.Ordinal)
                     .ThenBy(static item => item?.PriceCurrency?.Trim().ToUpperInvariant(), StringComparer.Ordinal)
                     .ThenBy(static item => item?.Quantity ?? 0m))
        {
            builder.Append("position|")
                .Append(position?.Symbol?.Trim().ToUpperInvariant()).Append('|')
                .Append(position is null ? string.Empty : FormatDecimal(position.Quantity)).Append('|')
                .Append(position?.PriceCurrency?.Trim().ToUpperInvariant()).Append('\n');
        }

        foreach (var pair in (request.PricesBySymbol ?? new Dictionary<string, decimal>())
                     .OrderBy(static item => item.Key?.Trim().ToUpperInvariant(), StringComparer.Ordinal)
                     .ThenBy(static item => item.Value))
        {
            builder.Append("price|")
                .Append(pair.Key?.Trim().ToUpperInvariant()).Append('|')
                .Append(FormatDecimal(pair.Value)).Append('\n');
        }

        foreach (var pair in (request.FxRatesByCurrency ?? new Dictionary<string, decimal>())
                     .OrderBy(static item => item.Key?.Trim().ToUpperInvariant(), StringComparer.Ordinal)
                     .ThenBy(static item => item.Value))
        {
            builder.Append("fx|")
                .Append(pair.Key?.Trim().ToUpperInvariant()).Append('|')
                .Append(FormatDecimal(pair.Value)).Append('\n');
        }

        Append(builder, "fees", FormatDecimal(request.Fees));
        Append(builder, "accruals", FormatDecimal(request.Accruals));
        Append(builder, "externalReferenceNav", FormatDecimal(request.ExternalReferenceNav));

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
    }

    private static void Append(StringBuilder builder, string key, string? value)
        => builder.Append(key).Append('|').Append(value).Append('\n');

    private static string FormatDecimal(decimal value)
        => value.ToString("G29", CultureInfo.InvariantCulture);

    private sealed record ValidatedInput(
        string AccountId,
        string StrategyProfile,
        string OperatorId,
        string BaseCurrency,
        IReadOnlyList<NormalizedPosition> Positions,
        IReadOnlyDictionary<string, decimal> PricesBySymbol,
        IReadOnlyDictionary<string, decimal> FxRatesByCurrency);

    private sealed record NormalizedPosition(string Symbol, decimal Quantity, string PriceCurrency);

    private sealed record PrerequisiteFailure(string Code, string Message, string RecoveryGuidance);
}
