using System.Globalization;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;

namespace Meridian.Wpf.Features.Reporting;

internal sealed record ReportingRunInput(
    string TemplateName,
    int TemplateVersion,
    string FundProfileId,
    ReportingEntityScopeKindDto EntityScopeKind,
    string EntityId,
    string PortfolioId,
    string InvestorId,
    string DimensionOverridesText,
    string PeriodId,
    string AsOfDateText,
    string LedgerBookIdText,
    string LedgerBookCode,
    ReportingAccountingBasisDto AccountingBasis,
    string PresentationCurrency,
    ReportingConsolidationLevelDto ConsolidationLevel,
    ReportingOutputFormatDto OutputFormat,
    ReportingFinalityDto Finality,
    bool IncludeSupportingSchedules,
    bool IncludeEvidenceAppendix,
    string TemplateParametersText);

/// <summary>
/// Deterministic desktop-input mapper for the shared reporting request contract. This performs
/// input-shape validation only; all business readiness and scope authority remain server-owned.
/// </summary>
internal static class ReportingRunRequestBuilder
{
    private static readonly HashSet<string> SupportedDimensionKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "FundId",
        "EntityId",
        "SleeveId",
        "StrategyId",
        "InvestorId",
        "CapitalAccountId",
        "InstrumentId",
        "PositionId",
        "TaxLotId",
        "CostCenterId",
        "CounterpartyId",
        "OrganizationId",
        "PortfolioId",
        "BookId",
        "AccountId",
        "CustomerId",
        "VendorId",
        "ProjectId"
    };

    public static bool TryBuild(
        ReportingRunInput input,
        out ReportingRunRequestDto? request,
        out string error)
    {
        request = null;
        if (string.IsNullOrWhiteSpace(input.TemplateName))
        {
            error = "Template name is required.";
            return false;
        }

        if (input.TemplateVersion <= 0)
        {
            error = "Template version must be positive.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(input.FundProfileId))
        {
            error = "A fund profile is required.";
            return false;
        }

        var requiredScopeId = input.EntityScopeKind switch
        {
            ReportingEntityScopeKindDto.Entity => input.EntityId,
            ReportingEntityScopeKindDto.Portfolio => input.PortfolioId,
            ReportingEntityScopeKindDto.Investor => input.InvestorId,
            ReportingEntityScopeKindDto.AllEntities => string.Empty,
            _ => null
        };
        if (requiredScopeId is null)
        {
            error = "Entity scope kind is invalid.";
            return false;
        }

        if (input.EntityScopeKind != ReportingEntityScopeKindDto.AllEntities
            && string.IsNullOrWhiteSpace(requiredScopeId))
        {
            error = input.EntityScopeKind switch
            {
                ReportingEntityScopeKindDto.Entity => "The scoped entity id is required.",
                ReportingEntityScopeKindDto.Portfolio => "The scoped portfolio id is required.",
                _ => "The scoped investor id is required."
            };
            return false;
        }

        if (string.IsNullOrWhiteSpace(input.PeriodId))
        {
            error = "Reporting period is required.";
            return false;
        }

        if (!DateOnly.TryParseExact(
                input.AsOfDateText.Trim(),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var asOfDate))
        {
            error = "As-of date must use yyyy-MM-dd.";
            return false;
        }

        if (!TryParseOptionalGuid(input.LedgerBookIdText, "Ledger book id", out var requestedLedgerBookId, out error)
            || !TryParseKeyValueText(input.TemplateParametersText, "Template parameters", out var templateParameters, out error)
            || !TryBuildDimensions(
                input,
                requestedLedgerBookId,
                out var dimensions,
                out var resolvedLedgerBookId,
                out error))
        {
            return false;
        }

        if (resolvedLedgerBookId is null && string.IsNullOrWhiteSpace(input.LedgerBookCode))
        {
            error = "A ledger book id or code is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(input.PresentationCurrency))
        {
            error = "Presentation currency is required.";
            return false;
        }

        var template = new VersionedReportTemplateIdDto(input.TemplateName.Trim(), input.TemplateVersion);
        var parameters = new ReportingRunParametersDto(
            new ReportingRunScopeDto(
                input.FundProfileId.Trim(),
                input.EntityScopeKind,
                TrimOrNull(input.EntityId),
                TrimOrNull(input.PortfolioId),
                TrimOrNull(input.InvestorId),
                dimensions),
            input.PeriodId.Trim(),
            asOfDate,
            new ReportingLedgerBookSelectionDto(resolvedLedgerBookId, TrimOrNull(input.LedgerBookCode)),
            input.AccountingBasis,
            input.PresentationCurrency.Trim().ToUpperInvariant(),
            input.ConsolidationLevel,
            input.OutputFormat,
            input.Finality,
            input.IncludeSupportingSchedules,
            input.IncludeEvidenceAppendix,
            templateParameters);

        request = new ReportingRunRequestDto(
            TemplateId: template.Name,
            AsOfDate: asOfDate,
            DatasetRows: null,
            DatasetSourceId: null,
            AllowRestatement: false,
            Template: template,
            Parameters: parameters);
        error = string.Empty;
        return true;
    }

    private static bool TryBuildDimensions(
        ReportingRunInput input,
        Guid? requestedLedgerBookId,
        out LedgerDimensionSetDto dimensions,
        out Guid? resolvedLedgerBookId,
        out string error)
    {
        dimensions = new LedgerDimensionSetDto();
        resolvedLedgerBookId = null;
        if (!TryParseKeyValueText(input.DimensionOverridesText, "Dimension overrides", out var values, out error)
            || !TryParseNamedGuid(values, "InstrumentId", out var instrumentId, out error)
            || !TryParseNamedGuid(values, "PositionId", out var positionId, out error)
            || !TryParseNamedGuid(values, "BookId", out var overriddenLedgerBookId, out error))
        {
            return false;
        }

        var unsupportedKeys = values.Keys
            .Where(static key =>
                !SupportedDimensionKeys.Contains(key)
                && !(key.StartsWith("gl.", StringComparison.OrdinalIgnoreCase) && key.Length > 3))
            .OrderBy(static key => key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (unsupportedKeys.Length > 0)
        {
            error = $"Unsupported dimension override{(unsupportedKeys.Length == 1 ? string.Empty : "s")}: {string.Join(", ", unsupportedKeys)}.";
            return false;
        }

        var overriddenFundId = Value(values, "FundId");
        if (overriddenFundId is not null
            && !string.Equals(overriddenFundId, input.FundProfileId.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            error = "Dimension override FundId must match the selected fund profile.";
            return false;
        }

        if (requestedLedgerBookId is not null
            && overriddenLedgerBookId is not null
            && requestedLedgerBookId != overriddenLedgerBookId)
        {
            error = "Dimension override BookId must match the explicit ledger book id.";
            return false;
        }

        resolvedLedgerBookId = requestedLedgerBookId ?? overriddenLedgerBookId;

        var external = values
            .Where(static pair => pair.Key.StartsWith("gl.", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(
                static pair => pair.Key[3..],
                static pair => pair.Value,
                StringComparer.OrdinalIgnoreCase);

        dimensions = new LedgerDimensionSetDto(
            FundId: overriddenFundId ?? input.FundProfileId.Trim(),
            EntityId: Value(values, "EntityId") ?? TrimOrNull(input.EntityId),
            SleeveId: Value(values, "SleeveId"),
            StrategyId: Value(values, "StrategyId"),
            InvestorId: Value(values, "InvestorId") ?? TrimOrNull(input.InvestorId),
            CapitalAccountId: Value(values, "CapitalAccountId"),
            InstrumentId: instrumentId,
            TaxLotId: Value(values, "TaxLotId"),
            CostCenterId: Value(values, "CostCenterId"),
            CounterpartyId: Value(values, "CounterpartyId"),
            ExternalGlDimensions: external,
            OrganizationId: Value(values, "OrganizationId"),
            PortfolioId: Value(values, "PortfolioId") ?? TrimOrNull(input.PortfolioId),
            BookId: resolvedLedgerBookId?.ToString("D"),
            AccountId: Value(values, "AccountId"),
            CustomerId: Value(values, "CustomerId"),
            VendorId: Value(values, "VendorId"),
            ProjectId: Value(values, "ProjectId"))
        {
            PositionId = positionId
        };
        error = string.Empty;
        return true;
    }

    private static bool TryParseKeyValueText(
        string text,
        string label,
        out IReadOnlyDictionary<string, string> values,
        out string error)
    {
        var parsed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var segment in (text ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = segment.IndexOf('=');
            if (separator <= 0 || separator == segment.Length - 1)
            {
                values = parsed;
                error = $"{label} must use key=value entries separated by semicolons.";
                return false;
            }

            var key = segment[..separator].Trim();
            var value = segment[(separator + 1)..].Trim();
            if (!parsed.TryAdd(key, value))
            {
                values = parsed;
                error = $"{label} contains duplicate key '{key}'.";
                return false;
            }
        }

        values = parsed;
        error = string.Empty;
        return true;
    }

    private static bool TryParseOptionalGuid(
        string value,
        string label,
        out Guid? parsed,
        out string error)
    {
        parsed = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            error = string.Empty;
            return true;
        }

        if (Guid.TryParse(value.Trim(), out var guid))
        {
            parsed = guid;
            error = string.Empty;
            return true;
        }

        error = $"{label} must be a valid GUID.";
        return false;
    }

    private static bool TryParseNamedGuid(
        IReadOnlyDictionary<string, string> values,
        string key,
        out Guid? parsed,
        out string error) =>
        TryParseOptionalGuid(Value(values, key) ?? string.Empty, key, out parsed, out error);

    private static string? Value(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value) ? TrimOrNull(value) : null;

    private static string? TrimOrNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
