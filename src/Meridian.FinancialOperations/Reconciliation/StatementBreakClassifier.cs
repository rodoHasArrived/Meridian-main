using Meridian.Contracts.Workstation;
using Meridian.Domain.Reconciliation;

namespace Meridian.FinancialOperations.Reconciliation;

/// <summary>
/// Canonical statement reconciliation break types used by broker, custodian, and internal ledger workflows.
/// </summary>
public enum StatementBreakClassificationType : byte
{
    MissingInternalPosition = 0,
    MissingBrokerPosition = 1,
    PositionQuantityVariance = 2,
    CashBalanceVariance = 3,
    MissingInternalTransaction = 4,
    MissingBrokerTransaction = 5,
    TransactionTimingVariance = 6,
    SecurityMappingBreak = 7,
    AccountMappingBreak = 8,
    CurrencyMismatch = 9,
    FeeOrTaxVariance = 10
}

/// <summary>
/// Recommended operator action for a classified statement reconciliation break.
/// </summary>
public enum StatementBreakRecommendedAction : byte
{
    MapSecurity = 0,
    ReviewCashPosting = 1,
    InspectLedgerTransaction = 2,
    VerifySettlementDate = 3,
    RequestBrokerClarification = 4
}

/// <summary>
/// Materiality and workflow settings used to decide severity and whether a break should become casework.
/// </summary>
public sealed record StatementBreakWorkflowPolicy(
    decimal PositionQuantityMateriality = 0.0001m,
    decimal CashMateriality = 0.01m,
    decimal TransactionMateriality = 0.01m,
    decimal FeeOrTaxMateriality = 0.01m,
    bool CreateCasesForLowSeverityBreaks = false)
{
    public static StatementBreakWorkflowPolicy Default { get; } = new();
}

/// <summary>
/// Input evidence for a single broker statement break classification.
/// </summary>
public sealed record StatementBreakClassificationRequest(
    StatementBreakClassificationType BreakType,
    decimal? StatementAmount = null,
    decimal? InternalAmount = null,
    decimal? QuantityDelta = null,
    string? StatementCurrency = null,
    string? InternalCurrency = null,
    string Status = "Open",
    bool IsOptionalMetadata = false,
    StatementBreakWorkflowPolicy? Policy = null)
{
    public static StatementBreakClassificationRequest FromStatementRow(NormalizedStatementRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        var breakType = row.Kind switch
        {
            StatementRowKind.CashBalance => StatementBreakClassificationType.CashBalanceVariance,
            StatementRowKind.Fee => StatementBreakClassificationType.FeeOrTaxVariance,
            StatementRowKind.Transaction or StatementRowKind.Dividend => StatementBreakClassificationType.MissingInternalTransaction,
            _ when string.IsNullOrWhiteSpace(row.Symbol) => StatementBreakClassificationType.SecurityMappingBreak,
            _ => StatementBreakClassificationType.PositionQuantityVariance
        };

        return new StatementBreakClassificationRequest(
            breakType,
            StatementAmount: row.Amount,
            InternalAmount: null,
            QuantityDelta: row.Quantity,
            StatementCurrency: row.Currency,
            InternalCurrency: row.RawSnapshot.TryGetValue("internalCurrency", out var internalCurrency) ? internalCurrency : row.Currency,
            Status: row.RawSnapshot.TryGetValue("status", out var status) ? status : "Open",
            IsOptionalMetadata: row.RawSnapshot.TryGetValue("optionalMetadata", out var optionalMetadata)
                && bool.TryParse(optionalMetadata, out var parsedOptionalMetadata)
                && parsedOptionalMetadata);
    }
}

/// <summary>
/// Classification output stored on a break and consumed by case queue creation.
/// </summary>
public sealed record StatementBreakClassificationResult(
    StatementBreakClassificationType BreakType,
    ReconciliationBreakSeverity Severity,
    StatementBreakRecommendedAction RecommendedAction,
    string RecommendedActionText,
    bool IsMaterial,
    bool IsUnresolved,
    bool ShouldCreateCaseQueueItem,
    decimal AbsoluteVariance,
    string WorkflowRationale);

/// <summary>
/// Assigns canonical break type, materiality-aware severity, and operator guidance for statement reconciliation breaks.
/// </summary>
public sealed class StatementBreakClassifier
{
    public StatementBreakClassificationResult Classify(StatementBreakClassificationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var policy = request.Policy ?? StatementBreakWorkflowPolicy.Default;
        var absoluteVariance = CalculateAbsoluteVariance(request);
        var currencyMismatch = HasCurrencyMismatch(request);
        var isMaterial = IsMaterial(request, policy, absoluteVariance, currencyMismatch);
        var severity = ResolveSeverity(request, isMaterial, currencyMismatch);
        var action = ResolveRecommendedAction(request.BreakType);
        var isUnresolved = IsUnresolved(request.Status);
        var shouldCreateCase = isMaterial
            && isUnresolved
            && (severity >= ReconciliationBreakSeverity.Medium || policy.CreateCasesForLowSeverityBreaks);

        return new StatementBreakClassificationResult(
            request.BreakType,
            severity,
            action,
            ToRecommendedActionText(action),
            isMaterial,
            isUnresolved,
            shouldCreateCase,
            absoluteVariance,
            BuildRationale(request.BreakType, severity, action, isMaterial, isUnresolved));
    }

    private static decimal CalculateAbsoluteVariance(StatementBreakClassificationRequest request)
    {
        if (request.QuantityDelta.HasValue && IsPositionBreak(request.BreakType))
        {
            return Math.Abs(request.QuantityDelta.Value);
        }

        if (request.StatementAmount.HasValue && request.InternalAmount.HasValue)
        {
            return Math.Abs(request.StatementAmount.Value - request.InternalAmount.Value);
        }

        if (request.StatementAmount.HasValue)
        {
            return Math.Abs(request.StatementAmount.Value);
        }

        if (request.InternalAmount.HasValue)
        {
            return Math.Abs(request.InternalAmount.Value);
        }

        return 0m;
    }

    private static bool HasCurrencyMismatch(StatementBreakClassificationRequest request) =>
        request.BreakType == StatementBreakClassificationType.CurrencyMismatch
        || (!string.IsNullOrWhiteSpace(request.StatementCurrency)
            && !string.IsNullOrWhiteSpace(request.InternalCurrency)
            && !string.Equals(request.StatementCurrency.Trim(), request.InternalCurrency.Trim(), StringComparison.OrdinalIgnoreCase));

    private static bool IsMaterial(
        StatementBreakClassificationRequest request,
        StatementBreakWorkflowPolicy policy,
        decimal absoluteVariance,
        bool currencyMismatch) => request.BreakType switch
        {
            StatementBreakClassificationType.SecurityMappingBreak or StatementBreakClassificationType.AccountMappingBreak
                => !request.IsOptionalMetadata,
            StatementBreakClassificationType.CurrencyMismatch => currencyMismatch,
            StatementBreakClassificationType.MissingInternalPosition or StatementBreakClassificationType.MissingBrokerPosition
                => absoluteVariance >= policy.PositionQuantityMateriality,
            StatementBreakClassificationType.PositionQuantityVariance => absoluteVariance >= policy.PositionQuantityMateriality,
            StatementBreakClassificationType.CashBalanceVariance => absoluteVariance >= policy.CashMateriality,
            StatementBreakClassificationType.MissingInternalTransaction or StatementBreakClassificationType.MissingBrokerTransaction
                => absoluteVariance >= policy.TransactionMateriality,
            StatementBreakClassificationType.TransactionTimingVariance => true,
            StatementBreakClassificationType.FeeOrTaxVariance => absoluteVariance >= policy.FeeOrTaxMateriality,
            _ => false
        };

    private static ReconciliationBreakSeverity ResolveSeverity(
        StatementBreakClassificationRequest request,
        bool isMaterial,
        bool currencyMismatch) => request.BreakType switch
        {
            StatementBreakClassificationType.SecurityMappingBreak or StatementBreakClassificationType.AccountMappingBreak
                when request.IsOptionalMetadata => ReconciliationBreakSeverity.Low,
            StatementBreakClassificationType.SecurityMappingBreak or StatementBreakClassificationType.AccountMappingBreak
                => ReconciliationBreakSeverity.High,
            StatementBreakClassificationType.CurrencyMismatch when currencyMismatch => ReconciliationBreakSeverity.High,
            StatementBreakClassificationType.MissingInternalPosition or StatementBreakClassificationType.MissingBrokerPosition or StatementBreakClassificationType.PositionQuantityVariance
                => isMaterial ? ReconciliationBreakSeverity.High : ReconciliationBreakSeverity.Low,
            StatementBreakClassificationType.CashBalanceVariance
                => isMaterial ? ReconciliationBreakSeverity.High : ReconciliationBreakSeverity.Low,
            StatementBreakClassificationType.TransactionTimingVariance or StatementBreakClassificationType.FeeOrTaxVariance
                => isMaterial ? ReconciliationBreakSeverity.Medium : ReconciliationBreakSeverity.Low,
            StatementBreakClassificationType.MissingInternalTransaction or StatementBreakClassificationType.MissingBrokerTransaction
                => isMaterial ? ReconciliationBreakSeverity.Medium : ReconciliationBreakSeverity.Low,
            _ => ReconciliationBreakSeverity.Low
        };

    private static StatementBreakRecommendedAction ResolveRecommendedAction(StatementBreakClassificationType breakType) => breakType switch
    {
        StatementBreakClassificationType.SecurityMappingBreak => StatementBreakRecommendedAction.MapSecurity,
        StatementBreakClassificationType.AccountMappingBreak => StatementBreakRecommendedAction.RequestBrokerClarification,
        StatementBreakClassificationType.CashBalanceVariance or StatementBreakClassificationType.CurrencyMismatch => StatementBreakRecommendedAction.ReviewCashPosting,
        StatementBreakClassificationType.TransactionTimingVariance => StatementBreakRecommendedAction.VerifySettlementDate,
        StatementBreakClassificationType.MissingInternalTransaction or StatementBreakClassificationType.MissingBrokerTransaction or StatementBreakClassificationType.FeeOrTaxVariance
            => StatementBreakRecommendedAction.InspectLedgerTransaction,
        _ => StatementBreakRecommendedAction.RequestBrokerClarification
    };

    public static string ToRecommendedActionText(StatementBreakRecommendedAction action) => action switch
    {
        StatementBreakRecommendedAction.MapSecurity => "map security",
        StatementBreakRecommendedAction.ReviewCashPosting => "review cash posting",
        StatementBreakRecommendedAction.InspectLedgerTransaction => "inspect ledger transaction",
        StatementBreakRecommendedAction.VerifySettlementDate => "verify settlement date",
        StatementBreakRecommendedAction.RequestBrokerClarification => "request broker clarification",
        _ => "request broker clarification"
    };

    public static bool IsUnresolved(string? status) => status switch
    {
        null => true,
        _ when status.Equals("Open", StringComparison.OrdinalIgnoreCase) => true,
        _ when status.Equals("Investigating", StringComparison.OrdinalIgnoreCase) => true,
        _ when status.Equals("InReview", StringComparison.OrdinalIgnoreCase) => true,
        _ when status.Equals("PartialMatch", StringComparison.OrdinalIgnoreCase) => true,
        _ => false
    };

    private static bool IsPositionBreak(StatementBreakClassificationType breakType) => breakType is
        StatementBreakClassificationType.MissingInternalPosition or
        StatementBreakClassificationType.MissingBrokerPosition or
        StatementBreakClassificationType.PositionQuantityVariance;

    private static string BuildRationale(
        StatementBreakClassificationType breakType,
        ReconciliationBreakSeverity severity,
        StatementBreakRecommendedAction action,
        bool isMaterial,
        bool isUnresolved) =>
        $"{breakType} classified as {severity}; material={isMaterial}; unresolved={isUnresolved}; recommended action={action}.";
}
