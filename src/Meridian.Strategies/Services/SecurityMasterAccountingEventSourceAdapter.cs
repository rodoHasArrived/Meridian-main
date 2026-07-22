using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;

namespace Meridian.Strategies.Services;

public sealed class SecurityMasterAccountingEventSourceAdapter : ISecurityMasterAccountingEventSourceAdapter
{
    private readonly ISecurityMasterQueryService? _securityMasterQueryService;
    private readonly IAssetOperationsQueryService? _assetOperationsQueryService;

    public SecurityMasterAccountingEventSourceAdapter(
        ISecurityMasterQueryService? securityMasterQueryService = null,
        IAssetOperationsQueryService? assetOperationsQueryService = null)
    {
        _securityMasterQueryService = securityMasterQueryService;
        _assetOperationsQueryService = assetOperationsQueryService;
    }

    public async Task<SecurityMasterAccountingEventRequest?> BuildRequestAsync(
        StrategyRunDetail detail,
        ReconciliationRunRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(detail);
        ArgumentNullException.ThrowIfNull(request);

        if (_securityMasterQueryService is null)
        {
            return null;
        }

        var positions = CollectPositions(detail);
        if (positions.Count == 0)
        {
            return null;
        }

        var (periodStart, periodEnd) = ResolvePeriod(detail);
        var definitions = new Dictionary<Guid, SecurityEconomicDefinitionRecord>();
        foreach (var securityId in positions.Select(static position => position.SecurityId!.Value).Distinct())
        {
            ct.ThrowIfCancellationRequested();
            var definition = await _securityMasterQueryService
                .GetEconomicDefinitionByIdAsync(securityId, ct)
                .ConfigureAwait(false);
            if (definition is not null)
            {
                definitions[securityId] = definition;
            }
        }

        if (definitions.Count == 0)
        {
            return null;
        }

        var securities = definitions.Values
            .Select(ToAccountingSecurity)
            .ToArray();
        if (securities.Length == 0)
        {
            return null;
        }

        var factorSchedule = BuildFactorSchedule(definitions.Values, periodStart, periodEnd);
        if (factorSchedule.Count > 0)
        {
            positions = await ResolveDurablePositionsAsync(
                    positions,
                    periodEnd,
                    factorSchedule.Select(static factor => factor.SecurityId).ToHashSet(),
                    ct)
                .ConfigureAwait(false);
        }

        var resolvedPositions = positions
            .Where(position => position.SecurityId is Guid securityId && definitions.ContainsKey(securityId))
            .GroupBy(
                static position => $"{position.SecurityId!.Value:N}|{position.AccountId}|{position.Symbol}|{position.PositionId:N}",
                StringComparer.OrdinalIgnoreCase)
            .Select(static group =>
            {
                var first = group.First();
                return new SecurityMasterAccountingPosition(
                    first.Symbol,
                     first.SecurityId,
                     first.AccountId,
                     group.Sum(static position => position.ParAmount),
                     PositionId: first.PositionId,
                     PositionVersion: first.PositionVersion);
            })
            .Where(static position => position.ParAmount != 0m)
            .ToArray();

        if (resolvedPositions.Length == 0)
        {
            return null;
        }

        return new SecurityMasterAccountingEventRequest(
            request.RunId,
            periodStart,
            periodEnd,
            securities,
            resolvedPositions,
            FactorSchedule: factorSchedule.Count > 0 ? factorSchedule : null,
            AmountTolerance: request.AmountTolerance);
    }

    private async Task<List<SecurityMasterAccountingPosition>> ResolveDurablePositionsAsync(
        IReadOnlyList<SecurityMasterAccountingPosition> positions,
        DateOnly asOfDate,
        IReadOnlySet<Guid> factorSecurityIds,
        CancellationToken ct)
    {
        if (_assetOperationsQueryService is null)
        {
            return positions.ToList();
        }

        var details = new Dictionary<Guid, AssetOperationsDetailDto?>();
        foreach (var securityId in positions
                     .Where(static position => position.SecurityId.HasValue)
                     .Select(static position => position.SecurityId!.Value)
                     .Where(factorSecurityIds.Contains)
                     .Distinct())
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                details[securityId] = await _assetOperationsQueryService
                    .GetOperationsAsync(securityId, ct)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                details[securityId] = null;
            }
        }

        return positions.Select(position =>
        {
            if (position.SecurityId is not Guid securityId ||
                !details.TryGetValue(securityId, out var detail) ||
                detail is null)
            {
                return position;
            }

            var candidates = detail.BookPositions
                .Where(candidate => candidate.SecurityId == securityId)
                .Where(candidate => position.PositionId is Guid suppliedPositionId
                    ? candidate.PositionId == suppliedPositionId
                    : string.Equals(candidate.PrimaryAccountId?.Trim(), position.AccountId.Trim(), StringComparison.OrdinalIgnoreCase))
                .Where(candidate => candidate.EffectiveFrom <= asOfDate &&
                    (candidate.EffectiveTo is null || candidate.EffectiveTo >= asOfDate))
                .Where(candidate => string.Equals(candidate.Status?.Trim(), "Active", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            return candidates.Length == 1
                ? position with
                {
                    PositionId = candidates[0].PositionId,
                    PositionVersion = candidates[0].Version
                }
                : position;
        }).ToList();
    }

    private static List<SecurityMasterAccountingPosition> CollectPositions(StrategyRunDetail detail)
    {
        var positions = new List<SecurityMasterAccountingPosition>();
        if (detail.Portfolio is not null)
        {
            foreach (var position in detail.Portfolio.Positions)
            {
                if (position.Security is null || string.IsNullOrWhiteSpace(position.Symbol) || position.Quantity == 0)
                {
                    continue;
                }

                positions.Add(new SecurityMasterAccountingPosition(
                    position.Symbol.Trim().ToUpperInvariant(),
                    position.Security.SecurityId,
                    ResolveAccountId(position.AccountScopeId, detail.Portfolio.AccountScopeId),
                    Math.Abs((decimal)position.Quantity)));
            }
        }

        if (positions.Count > 0)
        {
            return positions;
        }

        if (detail.Ledger is null)
        {
            return positions;
        }

        foreach (var line in detail.Ledger.TrialBalance)
        {
            if (line.Security is null || string.IsNullOrWhiteSpace(line.Symbol) || line.Balance == 0m)
            {
                continue;
            }

            positions.Add(new SecurityMasterAccountingPosition(
                line.Symbol.Trim().ToUpperInvariant(),
                line.Security.SecurityId,
                ResolveAccountId(line.AccountScopeId ?? line.FinancialAccountId, detail.Ledger.AccountScopeId),
                Math.Abs(line.Balance),
                PositionId: line.Dimensions?.PositionId));
        }

        return positions;
    }

    private static SecurityMasterAccountingSecurity ToAccountingSecurity(SecurityEconomicDefinitionRecord definition)
    {
        var economicTerms = definition.EconomicTerms;
        var coupon = GetObject(economicTerms, "coupon");
        var accrual = GetObject(economicTerms, "accrual");
        var maturity = GetObject(economicTerms, "maturity");
        var payment = GetObject(economicTerms, "payment");
        var structuredProduct = GetObject(economicTerms, "structuredProduct");
        var redemption = GetObject(economicTerms, "redemption");

        var couponType = ReadString(coupon, "couponType");
        var currentFactor = ReadDecimal(structuredProduct, "factor");
        var originalFace = ReadDecimal(structuredProduct, "notionalBalance");
        decimal? currentFace = originalFace is decimal face && currentFactor is decimal factor
            ? decimal.Round(face * factor, 6, MidpointRounding.AwayFromZero)
            : null;

        return new SecurityMasterAccountingSecurity(
            definition.SecurityId,
            ResolveSymbol(definition),
            ResolveAccountingAssetClass(definition),
            definition.Currency,
            new SecurityFixedIncomeTerms(
                CouponRate: ReadDecimal(coupon, "couponRate") ?? ReadDecimal(structuredProduct, "weightedAvgCoupon"),
                CouponType: NormalizeCouponType(couponType),
                DayCountConvention: ReadString(coupon, "dayCount") ?? ReadString(accrual, "dayCount"),
                PaymentFrequencyPerYear: ResolvePaymentFrequency(
                    ReadString(coupon, "paymentFrequency") ?? ReadString(payment, "paymentFrequency")),
                IssueDate: ReadDate(maturity, "issueDate"),
                DatedDate: ReadDate(maturity, "effectiveDate"),
                MaturityDate: ReadDate(maturity, "maturityDate"),
                AccrualStartDate: ReadDate(accrual, "accrualStartDate") ?? ReadDate(maturity, "effectiveDate"),
                CurrentFactor: currentFactor,
                OriginalFace: originalFace,
                CurrentFace: currentFace,
                RequiresFactorSchedule: currentFactor is < 1m || ReadBool(redemption, "isAmortizing") == true),
            ToAccountingRule(definition));
    }

    private static SecurityAccountingRule? ToAccountingRule(SecurityEconomicDefinitionRecord definition)
    {
        var classification = ReadString(definition.CommonTerms, "accountingClassification") ??
            ReadString(definition.LegacyAssetSpecificTerms, "accountingClassification") ??
            ReadString(definition.Classification, "accountingClassification");
        if (string.IsNullOrWhiteSpace(classification))
        {
            return null;
        }

        return new SecurityAccountingRule(classification.Trim(), "GAAP");
    }

    private static IReadOnlyList<SecurityFactorScheduleEntry> BuildFactorSchedule(
        IEnumerable<SecurityEconomicDefinitionRecord> definitions,
        DateOnly periodStart,
        DateOnly periodEnd)
    {
        var entries = new List<SecurityFactorScheduleEntry>();
        foreach (var definition in definitions)
        {
            foreach (var schedule in EnumerateFactorScheduleArrays(definition))
            {
                foreach (var item in schedule.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var asOfDate = ReadDate(item, "asOfDate") ??
                        ReadDate(item, "factorDate") ??
                        ReadDate(item, "date");
                    var priorFactor = ReadDecimal(item, "priorFactor") ??
                        ReadDecimal(item, "previousFactor");
                    var currentFactor = ReadDecimal(item, "currentFactor") ??
                        ReadDecimal(item, "factor");
                    if (asOfDate is null ||
                        asOfDate < periodStart ||
                        asOfDate > periodEnd ||
                        priorFactor is null ||
                        currentFactor is null)
                    {
                        continue;
                    }

                    entries.Add(new SecurityFactorScheduleEntry(
                        definition.SecurityId,
                        asOfDate.Value,
                        priorFactor.Value,
                        currentFactor.Value,
                        ReadString(item, "source") ?? ReadString(definition.Provenance, "source") ?? "security-master",
                        ReadString(item, "evidenceLink") ?? ReadString(item, "evidenceId") ?? ReadString(item, "evidenceRoute"),
                        ReadString(item, "sourceContentHash") ??
                        ReadString(item, "contentHash") ??
                        ReadString(item, "sourceHash") ??
                        ReadString(definition.Provenance, "sourceContentHash") ??
                        HashFactorRow(item)));
                }
            }
        }

        return entries
            .OrderBy(static entry => entry.SecurityId)
            .ThenBy(static entry => entry.AsOfDate)
            .ToArray();
    }

    private static string HashFactorRow(JsonElement item)
        => $"sha256:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(item.GetRawText()))).ToLowerInvariant()}";

    private static IEnumerable<JsonElement> EnumerateFactorScheduleArrays(SecurityEconomicDefinitionRecord definition)
    {
        if (TryGetArray(definition.EconomicTerms, "factorSchedule", out var rootSchedule))
        {
            yield return rootSchedule;
        }

        var structuredProduct = GetObject(definition.EconomicTerms, "structuredProduct");
        if (TryGetArray(structuredProduct, "factorSchedule", out var structuredSchedule))
        {
            yield return structuredSchedule;
        }
    }

    private static (DateOnly PeriodStart, DateOnly PeriodEnd) ResolvePeriod(StrategyRunDetail detail)
    {
        var asOf = detail.Portfolio?.AsOf ?? detail.Ledger?.AsOf ?? detail.Summary.CompletedAt ?? detail.Summary.StartedAt;
        var date = DateOnly.FromDateTime(asOf.UtcDateTime);
        var start = new DateOnly(date.Year, date.Month, 1);
        var end = start.AddMonths(1);
        return (start, end);
    }

    private static string ResolveSymbol(SecurityEconomicDefinitionRecord definition)
    {
        var symbol = definition.Identifiers.FirstOrDefault(static identifier =>
            identifier.Kind == SecurityIdentifierKind.Ticker)?.Value;
        return string.IsNullOrWhiteSpace(symbol)
            ? definition.DisplayName.Trim().ToUpperInvariant()
            : symbol.Trim().ToUpperInvariant();
    }

    private static string ResolveAccountingAssetClass(SecurityEconomicDefinitionRecord definition)
    {
        if (IsMortgageBacked(definition.AssetClass) ||
            IsMortgageBacked(definition.AssetFamily) ||
            IsMortgageBacked(definition.SubType) ||
            IsMortgageBacked(definition.TypeName))
        {
            return "MortgageBackedSecurity";
        }

        if (IsAssetBacked(definition.AssetClass) ||
            IsAssetBacked(definition.AssetFamily) ||
            IsAssetBacked(definition.SubType) ||
            IsAssetBacked(definition.TypeName))
        {
            return "AssetBackedSecurity";
        }

        if (IsAmortizingLoan(definition.AssetClass) ||
            IsAmortizingLoan(definition.AssetFamily) ||
            IsAmortizingLoan(definition.SubType) ||
            IsAmortizingLoan(definition.TypeName))
        {
            return "AmortizingLoan";
        }

        if (IsFixedIncome(definition.AssetClass) ||
            IsFixedIncome(definition.AssetFamily) ||
            ContainsToken(definition.SubType, "Bond") ||
            ContainsToken(definition.TypeName, "Bond"))
        {
            return "Bond";
        }

        return definition.SubType ?? definition.AssetClass;
    }

    private static bool IsFixedIncome(string? value) =>
        string.Equals(value, "FixedIncome", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "Bond", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "CertificateOfDeposit", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "CommercialPaper", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "TreasuryBill", StringComparison.OrdinalIgnoreCase) ||
        IsMortgageBacked(value) ||
        IsAssetBacked(value) ||
        IsAmortizingLoan(value);

    private static bool IsMortgageBacked(string? value) =>
        string.Equals(value, "MortgageBacked", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "MortgageBackedSecurity", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "Mbs", StringComparison.OrdinalIgnoreCase) ||
        ContainsToken(value, "MortgageBacked") ||
        ContainsToken(value, "Mortgage Backed");

    private static bool IsAssetBacked(string? value) =>
        string.Equals(value, "AssetBacked", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "AssetBackedSecurity", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "Abs", StringComparison.OrdinalIgnoreCase) ||
        ContainsToken(value, "AssetBacked") ||
        ContainsToken(value, "Asset Backed");

    private static bool IsAmortizingLoan(string? value) =>
        string.Equals(value, "Loan", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "AmortizingLoan", StringComparison.OrdinalIgnoreCase) ||
        ContainsToken(value, "AmortizingLoan") ||
        ContainsToken(value, "Amortizing Loan");

    private static bool ContainsToken(string? value, string token) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Contains(token, StringComparison.OrdinalIgnoreCase);

    private static string? NormalizeCouponType(string? couponType)
    {
        if (string.IsNullOrWhiteSpace(couponType))
        {
            return null;
        }

        return couponType.Trim() switch
        {
            "FixedRate" => "Fixed",
            "FloatingRate" => "Floating",
            var value => value
        };
    }

    private static int? ResolvePaymentFrequency(string? frequency)
    {
        if (string.IsNullOrWhiteSpace(frequency))
        {
            return null;
        }

        return frequency.Trim() switch
        {
            "Annual" => 1,
            "SemiAnnual" => 2,
            "Semi-Annual" => 2,
            "Quarterly" => 4,
            "Monthly" => 12,
            "Weekly" => 52,
            "Daily" => 365,
            var value when int.TryParse(value, out var parsed) => parsed,
            _ => null
        };
    }

    private static string ResolveAccountId(string? primary, string? fallback)
    {
        if (!string.IsNullOrWhiteSpace(primary))
        {
            return primary.Trim();
        }

        if (!string.IsNullOrWhiteSpace(fallback))
        {
            return fallback.Trim();
        }

        return "unscoped-account";
    }

    private static JsonElement? GetObject(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var value) ||
            value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return value;
    }

    private static bool TryGetArray(JsonElement? element, string propertyName, out JsonElement value)
    {
        if (element is { ValueKind: JsonValueKind.Object } objectElement &&
            objectElement.TryGetProperty(propertyName, out value) &&
            value.ValueKind == JsonValueKind.Array)
        {
            return true;
        }

        value = default;
        return false;
    }

    private static string? ReadString(JsonElement? element, string propertyName)
    {
        if (element is not { ValueKind: JsonValueKind.Object } objectElement ||
            !objectElement.TryGetProperty(propertyName, out var value) ||
            value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return value.GetString();
    }

    private static decimal? ReadDecimal(JsonElement? element, string propertyName)
    {
        if (element is not { ValueKind: JsonValueKind.Object } objectElement ||
            !objectElement.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDecimal(out var parsed) => parsed,
            JsonValueKind.String when decimal.TryParse(value.GetString(), out var parsed) => parsed,
            _ => null
        };
    }

    private static bool? ReadBool(JsonElement? element, string propertyName)
    {
        if (element is not { ValueKind: JsonValueKind.Object } objectElement ||
            !objectElement.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static DateOnly? ReadDate(JsonElement? element, string propertyName)
    {
        var raw = ReadString(element, propertyName);
        return DateOnly.TryParse(raw, out var parsed) ? parsed : null;
    }
}
