using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using static Meridian.Contracts.Ledger.LedgerDimensionTags;
using static Meridian.Contracts.Text.TextPrimitives;

namespace Meridian.FinancialOperations.AccountingClose;

public sealed partial class AccountingReportPackageService
{
    private static void AddDimensionConflict(
        List<AccountingConfigurationValidationIssueDto> issues,
        string code,
        string label,
        string? requestedValue,
        string? authoritativeValue,
        string periodId)
    {
        var requested = NormalizeOptional(requestedValue);
        var authoritative = NormalizeOptional(authoritativeValue);
        if (requested is null || authoritative is null ||
            string.Equals(requested, authoritative, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        issues.Add(new AccountingConfigurationValidationIssueDto(
            code,
            AccountingConfigurationValidationSeverityDto.Critical,
            $"Accounting report package dimension scope requested {label} '{requested}', but the package context resolves to '{authoritative}'.",
            periodId,
            "Use one consistent fund, ledger book, investor, capital account, and dimension scope before report certification."));
    }

    private static bool MatchesDimensionScope(
        LedgerDimensionSetDto? dimensions,
        LedgerDimensionSetDto? filter)
    {
        if (filter is null)
        {
            return true;
        }

        var normalized = NormalizeDimensionSet(dimensions);
        if (normalized is null)
        {
            return false;
        }

        return MatchesDimensionValue(normalized.FundId, filter.FundId)
               && MatchesDimensionValue(normalized.EntityId, filter.EntityId)
               && MatchesDimensionValue(normalized.SleeveId, filter.SleeveId)
               && MatchesDimensionValue(normalized.StrategyId, filter.StrategyId)
               && MatchesDimensionValue(normalized.InvestorId, filter.InvestorId)
               && MatchesDimensionValue(normalized.CapitalAccountId, filter.CapitalAccountId)
               && MatchesDimensionGuid(normalized.InstrumentId, filter.InstrumentId)
               && MatchesDimensionGuid(normalized.PositionId, filter.PositionId)
               && MatchesDimensionValue(normalized.AccountingArrangementId, filter.AccountingArrangementId)
               && MatchesDimensionValue(normalized.AccountingArrangementRevisionId, filter.AccountingArrangementRevisionId)
               && MatchesDimensionValue(normalized.ChartAccountNodeId, filter.ChartAccountNodeId)
               && MatchesDimensionValue(normalized.AccountBindingRevisionId, filter.AccountBindingRevisionId)
               && MatchesDimensionValue(normalized.IntercompanyTransactionId, filter.IntercompanyTransactionId)
               && MatchesDimensionGuid(normalized.IntercompanyAgreementId, filter.IntercompanyAgreementId)
               && (!filter.IntercompanyAgreementVersion.HasValue || normalized.IntercompanyAgreementVersion == filter.IntercompanyAgreementVersion)
               && MatchesDimensionValue(normalized.TaxLotId, filter.TaxLotId)
               && MatchesDimensionValue(normalized.CostCenterId, filter.CostCenterId)
               && MatchesDimensionValue(normalized.CounterpartyId, filter.CounterpartyId)
               && MatchesDimensionValue(normalized.OrganizationId, filter.OrganizationId)
               && MatchesDimensionValue(normalized.PortfolioId, filter.PortfolioId)
               && MatchesDimensionValue(normalized.BookId, filter.BookId)
               && MatchesDimensionValue(normalized.AccountId, filter.AccountId)
               && MatchesDimensionValue(normalized.CustomerId, filter.CustomerId)
               && MatchesDimensionValue(normalized.VendorId, filter.VendorId)
               && MatchesDimensionValue(normalized.ProjectId, filter.ProjectId)
               && MatchesExternalGlDimensions(normalized.ExternalGlDimensions, filter.ExternalGlDimensions);
    }

    private static bool MatchesExactDimensionScope(
        LedgerDimensionSetDto? left,
        LedgerDimensionSetDto? right)
        => MatchesDimensionScope(left, right) && MatchesDimensionScope(right, left);

    private static bool MatchesDimensionValue(string? actual, string? expected)
    {
        var normalizedExpected = NormalizeOptional(expected);
        return normalizedExpected is null ||
               string.Equals(NormalizeOptional(actual), normalizedExpected, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesDimensionGuid(Guid? actual, Guid? expected)
        => !expected.HasValue || actual == expected;

    private static bool MatchesExternalGlDimensions(
        IReadOnlyDictionary<string, string> actual,
        IReadOnlyDictionary<string, string> expected)
        => expected.All(pair =>
            actual.TryGetValue(pair.Key, out var actualValue) &&
            string.Equals(NormalizeOptional(actualValue), NormalizeOptional(pair.Value), StringComparison.OrdinalIgnoreCase));

    private static LedgerDimensionSetDto? NormalizeDimensionSet(LedgerDimensionSetDto? dimensions)
    {
        if (dimensions is null)
        {
            return null;
        }

        var externalGlDimensions = dimensions.ExternalGlDimensions
            .Where(pair => NormalizeOptional(pair.Key) is not null && NormalizeOptional(pair.Value) is not null)
            .ToDictionary(
                pair => NormalizeOptional(pair.Key)!,
                pair => NormalizeOptional(pair.Value)!,
                StringComparer.OrdinalIgnoreCase);

        var normalized = new LedgerDimensionSetDto(
            FundId: NormalizeOptional(dimensions.FundId),
            EntityId: NormalizeOptional(dimensions.EntityId),
            SleeveId: NormalizeOptional(dimensions.SleeveId),
            StrategyId: NormalizeOptional(dimensions.StrategyId),
            InvestorId: NormalizeOptional(dimensions.InvestorId),
            CapitalAccountId: NormalizeOptional(dimensions.CapitalAccountId),
            InstrumentId: dimensions.InstrumentId,
            TaxLotId: NormalizeOptional(dimensions.TaxLotId),
            CostCenterId: NormalizeOptional(dimensions.CostCenterId),
            CounterpartyId: NormalizeOptional(dimensions.CounterpartyId),
            ExternalGlDimensions: externalGlDimensions,
            OrganizationId: NormalizeOptional(dimensions.OrganizationId),
            PortfolioId: NormalizeOptional(dimensions.PortfolioId),
            BookId: NormalizeOptional(dimensions.BookId),
            AccountId: NormalizeOptional(dimensions.AccountId),
            CustomerId: NormalizeOptional(dimensions.CustomerId),
            VendorId: NormalizeOptional(dimensions.VendorId),
            ProjectId: NormalizeOptional(dimensions.ProjectId))
        {
            PositionId = dimensions.PositionId,
            AccountingArrangementId = dimensions.AccountingArrangementId,
            AccountingArrangementRevisionId = dimensions.AccountingArrangementRevisionId,
            ChartAccountNodeId = dimensions.ChartAccountNodeId,
            AccountBindingRevisionId = dimensions.AccountBindingRevisionId,
            IntercompanyTransactionId = dimensions.IntercompanyTransactionId,
            IntercompanyAgreementId = dimensions.IntercompanyAgreementId,
            IntercompanyAgreementVersion = dimensions.IntercompanyAgreementVersion
        };
        return HasAnyDimension(normalized) ? normalized : null;
    }

    private static bool HasExplicitDimensionScope(
        LedgerDimensionSetDto dimensions,
        string fundProfileId,
        Guid? ledgerBookId)
    {
        var filter = new LedgerDimensionSetDto(
            FundId: fundProfileId,
            BookId: ledgerBookId?.ToString("D"));
        return !MatchesDimensionScope(filter, dimensions) || !MatchesDimensionScope(dimensions, filter);
    }

    private static IReadOnlyList<string> BuildScopedDimensionKeys(LedgerDimensionSetDto dimensions)
    {
        var keys = new List<string>();
        AddDimensionKey(keys, "fundId", dimensions.FundId);
        AddDimensionKey(keys, "entityId", dimensions.EntityId);
        AddDimensionKey(keys, "sleeveId", dimensions.SleeveId);
        AddDimensionKey(keys, "strategyId", dimensions.StrategyId);
        AddDimensionKey(keys, "investorId", dimensions.InvestorId);
        AddDimensionKey(keys, "capitalAccountId", dimensions.CapitalAccountId);
        if (dimensions.InstrumentId.HasValue)
        {
            keys.Add("instrumentId");
        }

        if (dimensions.PositionId.HasValue)
        {
            keys.Add("positionId");
        }

        AddDimensionKey(keys, "accountingArrangementId", dimensions.AccountingArrangementId);
        AddDimensionKey(keys, "accountingArrangementRevisionId", dimensions.AccountingArrangementRevisionId);
        AddDimensionKey(keys, "chartAccountNodeId", dimensions.ChartAccountNodeId);
        AddDimensionKey(keys, "accountBindingRevisionId", dimensions.AccountBindingRevisionId);
        AddDimensionKey(keys, "intercompanyTransactionId", dimensions.IntercompanyTransactionId);
        if (dimensions.IntercompanyAgreementId.HasValue)
            keys.Add("intercompanyAgreementId");
        if (dimensions.IntercompanyAgreementVersion.HasValue)
            keys.Add("intercompanyAgreementVersion");

        AddDimensionKey(keys, "taxLotId", dimensions.TaxLotId);
        AddDimensionKey(keys, "costCenterId", dimensions.CostCenterId);
        AddDimensionKey(keys, "counterpartyId", dimensions.CounterpartyId);
        AddDimensionKey(keys, "organizationId", dimensions.OrganizationId);
        AddDimensionKey(keys, "portfolioId", dimensions.PortfolioId);
        AddDimensionKey(keys, "bookId", dimensions.BookId);
        AddDimensionKey(keys, "accountId", dimensions.AccountId);
        AddDimensionKey(keys, "customerId", dimensions.CustomerId);
        AddDimensionKey(keys, "vendorId", dimensions.VendorId);
        AddDimensionKey(keys, "projectId", dimensions.ProjectId);
        keys.AddRange(dimensions.ExternalGlDimensions
            .Where(static pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
            .Select(static pair => $"externalGl.{pair.Key.Trim()}")
            .Order(StringComparer.OrdinalIgnoreCase));
        return keys
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void AddDimensionKey(List<string> keys, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            keys.Add(key);
        }
    }

    private static string BuildDimensionScopeHash(LedgerDimensionSetDto dimensions)
    {
        var payload = string.Join(
            "|",
            NormalizeOptional(dimensions.FundId) ?? string.Empty,
            NormalizeOptional(dimensions.EntityId) ?? string.Empty,
            NormalizeOptional(dimensions.SleeveId) ?? string.Empty,
            NormalizeOptional(dimensions.StrategyId) ?? string.Empty,
            NormalizeOptional(dimensions.InvestorId) ?? string.Empty,
            NormalizeOptional(dimensions.CapitalAccountId) ?? string.Empty,
            dimensions.InstrumentId?.ToString("D") ?? string.Empty,
            NormalizeOptional(dimensions.TaxLotId) ?? string.Empty,
            NormalizeOptional(dimensions.CostCenterId) ?? string.Empty,
            NormalizeOptional(dimensions.CounterpartyId) ?? string.Empty,
            NormalizeOptional(dimensions.OrganizationId) ?? string.Empty,
            NormalizeOptional(dimensions.PortfolioId) ?? string.Empty,
            NormalizeOptional(dimensions.BookId) ?? string.Empty,
            NormalizeOptional(dimensions.AccountId) ?? string.Empty,
            NormalizeOptional(dimensions.CustomerId) ?? string.Empty,
            NormalizeOptional(dimensions.VendorId) ?? string.Empty,
            NormalizeOptional(dimensions.ProjectId) ?? string.Empty,
            string.Join(";", dimensions.ExternalGlDimensions
                .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(static pair => $"{NormalizeOptional(pair.Key)}={NormalizeOptional(pair.Value)}")));
        if (dimensions.PositionId.HasValue)
        {
            payload += $"|positionId={dimensions.PositionId.Value:D}";
        }

        payload = LedgerAccountingArrangementDimensions.AppendSignature(
            LedgerAgreementDimensions.AppendSignature(payload, dimensions.IntercompanyAgreementId, dimensions.IntercompanyAgreementVersion), dimensions);
        return Sha256Digest.ComputeUtf8(payload)[..12];
    }
}
