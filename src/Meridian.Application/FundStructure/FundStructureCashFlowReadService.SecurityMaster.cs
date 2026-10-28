using System.Text.Json;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.SecurityMaster.Schedules;
using static Meridian.Contracts.Text.TextPrimitives;
using static Meridian.Application.FundStructure.FundStructureProjector;

namespace Meridian.Application.FundStructure;

internal sealed partial class FundStructureCashFlowReadService
{
    private async Task<SecurityMasterProjectionResult> BuildSecurityMasterProjectedEntriesAsync(
        AccountSummaryDto account,
        StructureScope scoped,
        AccountBalanceSnapshotDto? latestSnapshot,
        string currency,
        DateTimeOffset projectionWindowStart,
        DateTimeOffset projectionWindowEnd,
        IReadOnlyList<GovernanceCashFlowEntryDto> existingEntries,
        CancellationToken ct)
    {
        if (_securityMasterQueryService is null)
        {
            return SecurityMasterProjectionResult.Empty;
        }

        var assignments = ResolveSecurityMasterInstrumentAssignmentsForAccount(account, scoped);
        if (assignments.Count == 0)
        {
            return SecurityMasterProjectionResult.Empty;
        }

        var projections = await Task.WhenAll(assignments.Select(assignment =>
            ProjectSecurityMasterAssignmentAsync(
                assignment,
                account,
                latestSnapshot,
                currency,
                projectionWindowStart,
                projectionWindowEnd,
                existingEntries,
                ct))).ConfigureAwait(false);

        return new SecurityMasterProjectionResult(
            projections
                .SelectMany(static projection => projection.Entries)
                .OrderBy(static entry => entry.EventDate)
                .ThenBy(static entry => entry.EventKind)
                .ToList(),
            projections.Any(static projection => projection.ConsumedAccruedInterest),
            projections.Any(static projection => projection.ConsumedPendingSettlement));
    }

    private IReadOnlyList<SecurityMasterInstrumentAssignment> ResolveSecurityMasterInstrumentAssignmentsForAccount(
        AccountSummaryDto account,
        StructureScope scoped)
    {
        var nodeIds = ResolveSecurityAssignmentNodeIdsForAccount(account, scoped);
        if (nodeIds.Count == 0)
        {
            return [];
        }

        var priorityByNodeId = nodeIds
            .Select((nodeId, index) => new { nodeId, index })
            .ToDictionary(static item => item.nodeId, static item => item.index);

        return scoped.Assignments
            .Where(assignment => priorityByNodeId.ContainsKey(assignment.NodeId))
            .Where(assignment => IsSecurityInstrumentAssignment(assignment.AssignmentType))
            .Select(assignment => TryParseSecurityMasterInstrumentAssignment(assignment, priorityByNodeId[assignment.NodeId]))
            .Where(static assignment => assignment is not null)
            .Cast<SecurityMasterInstrumentAssignment>()
            .OrderByDescending(static assignment => assignment.IsPrimary)
            .ThenBy(static assignment => assignment.Priority)
            .ThenBy(static assignment => assignment.EffectiveFrom)
            .GroupBy(static assignment => assignment.SecurityId)
            .Select(static group => group.First())
            .ToList();
    }

    private static IReadOnlyList<Guid> ResolveSecurityAssignmentNodeIdsForAccount(
        AccountSummaryDto account,
        StructureScope scoped)
    {
        IReadOnlyList<Guid> nodeIds = [account.AccountId];

        if (TryParseGuid(account.PortfolioId, out var portfolioId))
        {
            nodeIds = AppendUnique(nodeIds, portfolioId);
            var portfolio = scoped.InvestmentPortfolios.FirstOrDefault(candidate => candidate.InvestmentPortfolioId == portfolioId);
            if (portfolio is not null)
            {
                if (portfolio.ClientId.HasValue)
                {
                    nodeIds = AppendUnique(nodeIds, portfolio.ClientId.Value);
                }

                nodeIds = AppendUnique(nodeIds, portfolio.BusinessId);
                var business = scoped.Businesses.FirstOrDefault(candidate => candidate.BusinessId == portfolio.BusinessId);
                if (business is not null)
                {
                    nodeIds = AppendUnique(nodeIds, business.OrganizationId);
                }
            }
        }

        if (account.EntityId.HasValue)
        {
            nodeIds = AppendUnique(nodeIds, account.EntityId.Value);
        }

        if (account.VehicleId.HasValue)
        {
            nodeIds = AppendUnique(nodeIds, account.VehicleId.Value);
            var vehicle = scoped.Vehicles.FirstOrDefault(candidate => candidate.VehicleId == account.VehicleId.Value);
            if (vehicle is not null)
            {
                nodeIds = AppendUnique(nodeIds, vehicle.FundId);
                nodeIds = AppendUnique(nodeIds, vehicle.LegalEntityId);
                var fund = scoped.Funds.FirstOrDefault(candidate => candidate.FundId == vehicle.FundId);
                if (fund?.BusinessId is Guid businessId)
                {
                    nodeIds = AppendUnique(nodeIds, businessId);
                    var business = scoped.Businesses.FirstOrDefault(candidate => candidate.BusinessId == businessId);
                    if (business is not null)
                    {
                        nodeIds = AppendUnique(nodeIds, business.OrganizationId);
                    }
                }
            }
        }

        if (account.SleeveId.HasValue)
        {
            nodeIds = AppendUnique(nodeIds, account.SleeveId.Value);
            var sleeve = scoped.Sleeves.FirstOrDefault(candidate => candidate.SleeveId == account.SleeveId.Value);
            if (sleeve is not null)
            {
                nodeIds = AppendUnique(nodeIds, sleeve.FundId);
                var fund = scoped.Funds.FirstOrDefault(candidate => candidate.FundId == sleeve.FundId);
                if (fund?.BusinessId is Guid businessId)
                {
                    nodeIds = AppendUnique(nodeIds, businessId);
                    var business = scoped.Businesses.FirstOrDefault(candidate => candidate.BusinessId == businessId);
                    if (business is not null)
                    {
                        nodeIds = AppendUnique(nodeIds, business.OrganizationId);
                    }
                }
            }
        }

        if (account.FundId.HasValue)
        {
            nodeIds = AppendUnique(nodeIds, account.FundId.Value);
            var fund = scoped.Funds.FirstOrDefault(candidate => candidate.FundId == account.FundId.Value);
            if (fund?.BusinessId is Guid businessId)
            {
                nodeIds = AppendUnique(nodeIds, businessId);
                var business = scoped.Businesses.FirstOrDefault(candidate => candidate.BusinessId == businessId);
                if (business is not null)
                {
                    nodeIds = AppendUnique(nodeIds, business.OrganizationId);
                }
            }
        }

        return nodeIds;
    }

    private static bool IsSecurityInstrumentAssignment(string assignmentType) =>
        AssignmentComparer.Equals(assignmentType, SecurityMasterInstrumentAssignmentType)
        || AssignmentComparer.Equals(assignmentType, SecurityInstrumentAssignmentType);

    private static SecurityMasterInstrumentAssignment? TryParseSecurityMasterInstrumentAssignment(
        FundStructureAssignmentDto assignment,
        int priority)
    {
        if (TryParseGuid(assignment.AssignmentReference, out var securityId))
        {
            return new SecurityMasterInstrumentAssignment(
                assignment.AssignmentId,
                assignment.NodeId,
                securityId,
                IncomeAmount: null,
                PrincipalAmount: null,
                Units: null,
                FirstProjectedDate: null,
                Currency: null,
                Notes: null,
                priority,
                assignment.IsPrimary,
                assignment.EffectiveFrom);
        }

        try
        {
            var payload = JsonSerializer.Deserialize<SecurityMasterInstrumentAssignmentPayload>(
                assignment.AssignmentReference,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (payload is null || payload.SecurityId == Guid.Empty)
            {
                return null;
            }

            var firstProjectedDate = payload.FirstProjectedDate ?? payload.NextPaymentDate;
            return new SecurityMasterInstrumentAssignment(
                assignment.AssignmentId,
                assignment.NodeId,
                payload.SecurityId,
                payload.IncomeAmount ?? payload.ExpectedCashAmount,
                payload.PrincipalAmount,
                payload.Units,
                firstProjectedDate,
                payload.Currency,
                payload.Notes,
                priority,
                assignment.IsPrimary,
                assignment.EffectiveFrom);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<SecurityMasterProjectionResult> ProjectSecurityMasterAssignmentAsync(
        SecurityMasterInstrumentAssignment assignment,
        AccountSummaryDto account,
        AccountBalanceSnapshotDto? latestSnapshot,
        string currency,
        DateTimeOffset projectionWindowStart,
        DateTimeOffset projectionWindowEnd,
        IReadOnlyList<GovernanceCashFlowEntryDto> existingEntries,
        CancellationToken ct)
    {
        if (_securityMasterQueryService is null)
        {
            return SecurityMasterProjectionResult.Empty;
        }

        var economicTask = _securityMasterQueryService.GetEconomicDefinitionByIdAsync(assignment.SecurityId, ct);
        var corporateActionsTask = assignment.Units.HasValue && assignment.Units.Value != 0m
            ? _securityMasterQueryService.GetCorporateActionsAsync(assignment.SecurityId, ct)
            : Task.FromResult<IReadOnlyList<CorporateActionDto>>([]);

        await Task.WhenAll(economicTask, corporateActionsTask).ConfigureAwait(false);

        var economic = await economicTask.ConfigureAwait(false);
        if (economic is null)
        {
            return SecurityMasterProjectionResult.Empty;
        }

        var effectiveCurrency = ResolveSecurityMasterProjectionCurrency(assignment, economic, account);
        if (!MatchesCurrency(effectiveCurrency, currency))
        {
            return SecurityMasterProjectionResult.Empty;
        }

        var allEntries = existingEntries.ToList();
        var projectedEntries = new List<GovernanceCashFlowEntryDto>();
        var corporateActions = await corporateActionsTask.ConfigureAwait(false);
        var hasDividendActionInWindow = false;

        if (assignment.Units.HasValue && assignment.Units.Value != 0m)
        {
            foreach (var action in corporateActions
                .Where(action => AssignmentComparer.Equals(action.EventType, "Dividend"))
                .Where(action => action.DividendPerShare.HasValue))
            {
                var eventDate = ToDateTimeOffset(action.PayDate ?? action.ExDate);
                if (eventDate < projectionWindowStart || eventDate >= projectionWindowEnd)
                {
                    continue;
                }

                var amount = assignment.Units.Value * action.DividendPerShare!.Value;
                if (amount == 0m)
                {
                    continue;
                }

                var entry = new GovernanceCashFlowEntryDto(
                    eventDate,
                    amount,
                    currency,
                    "Dividend",
                    SecurityMasterCorporateActionSourceKind,
                    account.AccountId,
                    account.DisplayName,
                    account.LedgerReference,
                    $"{economic.DisplayName} dividend projected from Security Master corporate action.",
                    IsProjected: true,
                    SecurityId: assignment.SecurityId,
                    SecurityDisplayName: economic.DisplayName,
                    SecurityTypeName: economic.TypeName);
                if (TryAddSecurityMasterProjectedEntry(allEntries, projectedEntries, entry))
                {
                    hasDividendActionInWindow = true;
                }
            }
        }

        var recurringEventKind = DetermineSecurityIncomeEventKind(economic);
        var consumedAccruedInterest = false;
        if (!string.IsNullOrWhiteSpace(recurringEventKind)
            && !(hasDividendActionInWindow && recurringEventKind.Equals("Dividend", StringComparison.OrdinalIgnoreCase)))
        {
            var recurringAmount = assignment.IncomeAmount;
            if (!recurringAmount.HasValue
                && recurringEventKind.Equals("Coupon", StringComparison.OrdinalIgnoreCase)
                && latestSnapshot?.AccruedInterest is decimal accruedInterest
                && accruedInterest != 0m)
            {
                recurringAmount = accruedInterest;
                consumedAccruedInterest = true;
            }

            if (recurringAmount.HasValue && recurringAmount.Value != 0m)
            {
                foreach (var dueDate in BuildProjectedScheduleDates(assignment, economic, projectionWindowStart, projectionWindowEnd))
                {
                    var entry = new GovernanceCashFlowEntryDto(
                        dueDate,
                        recurringAmount.Value,
                        currency,
                        recurringEventKind,
                        SecurityMasterRuleSourceKind,
                        account.AccountId,
                        account.DisplayName,
                        account.LedgerReference,
                        $"{economic.DisplayName} {recurringEventKind.ToLowerInvariant()} projected from Security Master terms.",
                        IsProjected: true,
                        SecurityId: assignment.SecurityId,
                        SecurityDisplayName: economic.DisplayName,
                        SecurityTypeName: economic.TypeName);
                    TryAddSecurityMasterProjectedEntry(allEntries, projectedEntries, entry);
                }
            }
        }

        var maturityDate = ReadDateOnly(ReadObject(economic.EconomicTerms, "maturity"), "maturityDate");
        var consumedPendingSettlement = false;
        if (maturityDate.HasValue)
        {
            var maturityEventDate = ToDateTimeOffset(maturityDate.Value);
            if (maturityEventDate >= projectionWindowStart && maturityEventDate < projectionWindowEnd)
            {
                var principalAmount = assignment.PrincipalAmount;
                if (!principalAmount.HasValue
                    && latestSnapshot?.PendingSettlement is decimal pendingSettlement
                    && pendingSettlement != 0m)
                {
                    principalAmount = pendingSettlement;
                    consumedPendingSettlement = true;
                }

                if (principalAmount.HasValue && principalAmount.Value != 0m)
                {
                    var entry = new GovernanceCashFlowEntryDto(
                        maturityEventDate,
                        principalAmount.Value,
                        currency,
                        "PrincipalMaturity",
                        SecurityMasterRuleSourceKind,
                        account.AccountId,
                        account.DisplayName,
                        account.LedgerReference,
                        $"{economic.DisplayName} principal maturity projected from Security Master terms.",
                        IsProjected: true,
                        SecurityId: assignment.SecurityId,
                        SecurityDisplayName: economic.DisplayName,
                        SecurityTypeName: economic.TypeName);
                    TryAddSecurityMasterProjectedEntry(allEntries, projectedEntries, entry);
                }
            }
        }

        return new SecurityMasterProjectionResult(
            projectedEntries,
            consumedAccruedInterest && projectedEntries.Any(entry => entry.EventKind.Equals("Coupon", StringComparison.OrdinalIgnoreCase)),
            consumedPendingSettlement && projectedEntries.Any(entry => entry.EventKind.Equals("PrincipalMaturity", StringComparison.OrdinalIgnoreCase)));
    }

    private static bool TryAddSecurityMasterProjectedEntry(
        List<GovernanceCashFlowEntryDto> allEntries,
        List<GovernanceCashFlowEntryDto> projectedEntries,
        GovernanceCashFlowEntryDto candidate)
    {
        var exists = allEntries.Any(existing =>
            existing.AccountId == candidate.AccountId
            && existing.SecurityId == candidate.SecurityId
            && existing.EventDate == candidate.EventDate
            && existing.Amount == candidate.Amount
            && string.Equals(existing.EventKind, candidate.EventKind, StringComparison.OrdinalIgnoreCase)
            && MatchesCurrency(existing.Currency, candidate.Currency));

        if (exists)
        {
            return false;
        }

        allEntries.Add(candidate);
        projectedEntries.Add(candidate);
        return true;
    }

    private static IReadOnlyList<DateTimeOffset> BuildProjectedScheduleDates(
        SecurityMasterInstrumentAssignment assignment,
        SecurityEconomicDefinitionRecord economic,
        DateTimeOffset projectionWindowStart,
        DateTimeOffset projectionWindowEnd)
    {
        var payment = ReadObject(economic.EconomicTerms, "payment");
        var coupon = ReadObject(economic.EconomicTerms, "coupon");
        var accrual = ReadObject(economic.EconomicTerms, "accrual");
        var maturity = ReadObject(economic.EconomicTerms, "maturity");
        var frequency = ReadString(payment, "paymentFrequency")
            ?? ReadString(coupon, "paymentFrequency");
        var anchorDate = assignment.FirstProjectedDate
            ?? ReadDateOnly(accrual, "accrualStartDate")
            ?? ReadDateOnly(maturity, "issueDate")
            ?? ReadDateOnly(maturity, "effectiveDate");

        if (string.IsNullOrWhiteSpace(frequency))
        {
            if (!anchorDate.HasValue)
            {
                return [];
            }

            var anchored = ToDateTimeOffset(anchorDate.Value);
            return anchored >= projectionWindowStart && anchored < projectionWindowEnd
                ? [anchored]
                : [];
        }

        return RollProjectedScheduleDates(anchorDate, frequency, projectionWindowStart, projectionWindowEnd);
    }

    // The projected dates in [projectionWindowStart, projectionWindowEnd): the anchor (the window's
    // start date when there is none), then each date one kernel tenor step after the previous one,
    // for at most 256 dates. Compounding lets a month-end anchor drift (SCH-DEF-007). An unrecognised
    // spelling projects the anchor alone. The step past 9999-12-31 throws ArgumentOutOfRangeException,
    // even on the 256th date, whose successor is never used (SCH-DEF-011).
    private static IReadOnlyList<DateTimeOffset> RollProjectedScheduleDates(
        DateOnly? anchorDate,
        string? frequency,
        DateTimeOffset projectionWindowStart,
        DateTimeOffset projectionWindowEnd)
    {
        var schedule = new List<DateTimeOffset>();
        var tenor = ResolveProjectedScheduleTenor(frequency);
        var nextDate = anchorDate ?? DateOnly.FromDateTime(projectionWindowStart.UtcDateTime);
        var guard = 0;
        while (guard < 256)
        {
            guard++;
            var eventDate = ToDateTimeOffset(nextDate);
            if (eventDate >= projectionWindowStart && eventDate < projectionWindowEnd)
            {
                schedule.Add(eventDate);
            }

            if (eventDate >= projectionWindowEnd || tenor is not { } step)
            {
                break;
            }

            nextDate = step.Advance(nextDate, 1);
        }

        return schedule;
    }

    // The forecast's own frequency spellings; any other spelling (BiMonthly, AtMaturity, "3M") has no
    // tenor, so the recognised set never widens with the Security Master's vocabulary.
    private static ScheduleTenor? ResolveProjectedScheduleTenor(string? frequency)
        => frequency?.Trim().ToLowerInvariant() switch
        {
            "daily" => ScheduleTenor.Days(1),
            "weekly" => ScheduleTenor.Weeks(1),
            "biweekly" or "bi-weekly" => ScheduleTenor.Weeks(2),
            "monthly" => ScheduleTenor.Months(1),
            "quarterly" => ScheduleTenor.Months(3),
            "semiannual" or "semi-annual" or "semiannually" or "semi-annually" or "halfyearly" or "half-yearly" => ScheduleTenor.Months(6),
            "annual" or "annually" or "yearly" => ScheduleTenor.Years(1),
            _ => null
        };

    private static string? DetermineSecurityIncomeEventKind(SecurityEconomicDefinitionRecord economic)
    {
        var payment = ReadObject(economic.EconomicTerms, "payment");
        var coupon = ReadObject(economic.EconomicTerms, "coupon");
        if (ReadString(payment, "paymentFrequency") is not null
            || ReadString(coupon, "paymentFrequency") is not null
            || ReadDecimal(coupon, "couponRate").HasValue)
        {
            return "Coupon";
        }

        var equityBehavior = ReadObject(economic.EconomicTerms, "equityBehavior");
        if (ReadString(equityBehavior, "distributionType") is not null
            || SecurityAssetClassCatalog.GetOrDefault(economic.AssetClass).CashFlowBehavior is SecurityCashFlowBehavior.Dividends
            || string.Equals(economic.AssetClass, "Fund", StringComparison.OrdinalIgnoreCase))
        {
            return "Dividend";
        }

        return null;
    }

    private static string ResolveSecurityMasterProjectionCurrency(
        SecurityMasterInstrumentAssignment assignment,
        SecurityEconomicDefinitionRecord economic,
        AccountSummaryDto account)
    {
        if (!string.IsNullOrWhiteSpace(assignment.Currency))
        {
            return assignment.Currency.Trim().ToUpperInvariant();
        }

        var payment = ReadObject(economic.EconomicTerms, "payment");
        var paymentCurrency = ReadString(payment, "paymentCurrency");
        if (!string.IsNullOrWhiteSpace(paymentCurrency))
        {
            return paymentCurrency.Trim().ToUpperInvariant();
        }

        if (!string.IsNullOrWhiteSpace(economic.Currency))
        {
            return economic.Currency.Trim().ToUpperInvariant();
        }

        return string.IsNullOrWhiteSpace(account.BaseCurrency)
            ? DefaultCashFlowCurrency
            : account.BaseCurrency.Trim().ToUpperInvariant();
    }

    private static JsonElement? ReadObject(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.Object
            ? property
            : null;

    private static string? ReadString(JsonElement? element, string propertyName)
        => element.HasValue
            && element.Value.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;

    private static int? ReadInt32(JsonElement? element, string propertyName)
        => element.HasValue
            && element.Value.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.Number
                ? property.GetInt32()
                : null;

    private static decimal? ReadDecimal(JsonElement? element, string propertyName)
        => element.HasValue
            && element.Value.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.Number
                ? property.GetDecimal()
                : null;

    private static DateOnly? ReadDateOnly(JsonElement? element, string propertyName)
        => element.HasValue
            && element.Value.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
            && DateOnly.TryParse(property.GetString(), out var parsed)
                ? parsed
                : null;

}
