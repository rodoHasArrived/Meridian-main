using System.Globalization;
using System.Text;
using System.Text.Json;
using Meridian.Application.SecurityMaster;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Api;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Reporting;
using Meridian.ReferenceData.SecurityMaster;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Http;
using static Meridian.Contracts.Text.TextPrimitives;

namespace Meridian.Ui.Shared.Services;

// The schedule summary and schedule book: economic schedule arrays, factor history and schedule provenance.
public sealed partial class SecurityMasterWorkbenchQueryService
{
    private static SecurityMasterScheduleSummaryDto BuildScheduleSummary(
        SecurityDetailDto detail,
        SecurityEconomicDefinitionRecord? economic,
        IReadOnlyList<CorporateActionDto> corporateActions,
        WinningSourceInfo? winningSource,
        Meridian.Contracts.FixedIncome.BondExerciseNoticeWatchResult? declaredExercise)
    {
        var assetCapability = GetAssetCapability(detail.AssetClass);
        var economicTerms = economic?.EconomicTerms;
        var supportsFactorHistory = economicTerms.HasValue && TryGetPropertyCaseInsensitive(economicTerms.Value, "structuredProduct", out _);
        var supportsCashflowSchedule =
            supportsFactorHistory
            || economicTerms.HasValue && (
                TryGetPropertyCaseInsensitive(economicTerms.Value, "coupon", out _)
                || TryGetPropertyCaseInsensitive(economicTerms.Value, "payment", out _)
                || TryGetPropertyCaseInsensitive(economicTerms.Value, "redemption", out _)
                || TryGetPropertyCaseInsensitive(economicTerms.Value, "call", out _))
            || assetCapability.SupportsCashflowScheduleByDefault;

        var currentFactor = TryGetNestedJsonDecimal(economicTerms, "structuredProduct", "factor");
        var currentFactorDate = TryGetNestedJsonDateOnly(economicTerms, "structuredProduct", "factorDate");
        var maturityDate = TryGetNestedJsonDateOnly(economicTerms, "maturity", "maturityDate");
        var firstCallDate = TryGetNestedJsonDateOnly(economicTerms, "call", "firstCallDate");
        var nextCorporateAction = corporateActions
            .Where(static action => action.ExDate >= DateOnly.FromDateTime(DateTime.UtcNow))
            .OrderBy(static action => action.ExDate)
            .Select(static action => (DateOnly?)action.ExDate)
            .FirstOrDefault();
        var nextLifecycleDate = (declaredExercise is null
                ? new[] { nextCorporateAction, firstCallDate, maturityDate }
                : DeclaredLifecycleCandidates(declaredExercise, nextCorporateAction, maturityDate))
            .Where(static value => value.HasValue)
            .Select(static value => value!.Value)
            .OrderBy(static value => value)
            .Cast<DateOnly?>()
            .FirstOrDefault();

        var sourceSummary = winningSource is null
            ? "Schedule source metadata unavailable."
            : string.IsNullOrWhiteSpace(winningSource.SourceRecordId)
                ? $"Schedules follow {winningSource.SourceSystem} as of {winningSource.AsOf?.UtcDateTime:yyyy-MM-dd HH:mm 'UTC'}."
                : $"Schedules follow {winningSource.SourceSystem} record {winningSource.SourceRecordId} as of {winningSource.AsOf?.UtcDateTime:yyyy-MM-dd HH:mm 'UTC'}.";

        string summary;
        if (!supportsCashflowSchedule && !supportsFactorHistory)
        {
            summary = "Selected security does not expose cash-flow or factor schedule terms in the current projection.";
        }
        else if (supportsFactorHistory && currentFactor.HasValue)
        {
            summary = currentFactorDate.HasValue
                ? $"Factor-aware cash-flow support is available. Current factor {currentFactor.Value:0.########} as of {currentFactorDate.Value:yyyy-MM-dd}."
                : $"Factor-aware cash-flow support is available. Current factor {currentFactor.Value:0.########}.";
        }
        else if (supportsCashflowSchedule)
        {
            summary = nextLifecycleDate.HasValue
                ? $"Cash-flow schedule support is available. Next lifecycle date {nextLifecycleDate.Value:yyyy-MM-dd}."
                : "Cash-flow schedule support is available from the current economic terms.";
        }
        else
        {
            summary = "Factor history is available without a projected cash-flow schedule.";
        }

        (summary, sourceSummary) = WithDeclaredExerciseOutcome(summary, sourceSummary, declaredExercise);

        return new SecurityMasterScheduleSummaryDto(
            SupportsCashflowSchedule: supportsCashflowSchedule,
            SupportsFactorHistory: supportsFactorHistory,
            HasEconomicScheduleTerms: economicTerms.HasValue && HasJsonContent(economicTerms.Value),
            CurrentFactor: currentFactor,
            CurrentFactorDate: currentFactorDate,
            NextLifecycleDate: nextLifecycleDate,
            SourceSummary: sourceSummary,
            Summary: summary,
            DeclaredExercise: ToDeclaredExerciseOutcome(declaredExercise));
    }

    private static SecurityMasterScheduleBookDto BuildScheduleBook(
        SecurityDetailDto detail,
        SecurityEconomicDefinitionRecord? economic,
        IReadOnlyList<CorporateActionDto> corporateActions,
        IReadOnlyList<SecurityMasterEventEnvelope> history,
        WinningSourceInfo? winningSource,
        SecurityMasterScheduleSummaryDto summary,
        Meridian.Contracts.FixedIncome.BondExerciseNoticeWatchResult? declaredExercise)
    {
        var economicTerms = economic?.EconomicTerms;
        var currency = economic?.Currency ?? detail.Currency;
        var events = new List<SecurityMasterScheduleEventDto>();

        AppendScheduleEventsFromArray(events, economicTerms, "cashflowSchedule", currency, winningSource);
        AppendScheduleEventsFromArray(events, economicTerms, "cashflows", currency, winningSource);
        AppendScheduleEventsFromArray(events, economicTerms, "paymentSchedule", currency, winningSource);

        var factorHistory = BuildFactorHistory(economicTerms, winningSource, summary.CurrentFactor, summary.CurrentFactorDate);
        foreach (var point in factorHistory)
        {
            if (events.Any(existing => string.Equals(existing.EventType, "FactorUpdate", StringComparison.OrdinalIgnoreCase)
                && existing.EffectiveDate == point.EffectiveDate
                && existing.FactorEnd == point.Factor))
            {
                continue;
            }

            events.Add(new SecurityMasterScheduleEventDto(
                EventId: $"factor-{point.EffectiveDate:yyyyMMdd}",
                EventType: "FactorUpdate",
                EffectiveDate: point.EffectiveDate,
                PayDate: null,
                AccrualStartDate: null,
                AccrualEndDate: null,
                ExpectedAmount: null,
                ActualAmount: null,
                VarianceAmount: null,
                FactorStart: point.PreviousFactor,
                FactorEnd: point.Factor,
                Currency: currency,
                PostingStatus: point.IsCurrentFactor ? "Posted" : "Reference",
                SourceSystem: point.SourceSystem,
                SourceRecordId: point.SourceRecordId,
                SourceAsOfUtc: point.SourceAsOfUtc,
                SourceUpdatedBy: point.SourceUpdatedBy,
                SourceReason: point.SourceReason,
                IsDerivedFromEconomicTerms: true,
                IsCurrentProjection: point.IsCurrentFactor));
        }

        AppendCallAndPutEvents(events, declaredExercise, economicTerms, currency, winningSource);
        AppendLifecycleEvent(events, "Maturity", TryGetNestedJsonDateOnly(economicTerms, "maturity", "maturityDate"), currency, winningSource);

        foreach (var action in corporateActions)
        {
            var postingStatus = action.PayDate.HasValue && action.PayDate.Value < DateOnly.FromDateTime(DateTime.UtcNow)
                ? "Posted"
                : action.ExDate < DateOnly.FromDateTime(DateTime.UtcNow)
                    ? "Pending"
                    : "Forecast";

            events.Add(new SecurityMasterScheduleEventDto(
                EventId: $"corp-{action.CorpActId:N}",
                EventType: string.IsNullOrWhiteSpace(action.EventType) ? "CorporateAction" : action.EventType.Trim(),
                EffectiveDate: action.ExDate,
                PayDate: action.PayDate,
                AccrualStartDate: null,
                AccrualEndDate: null,
                ExpectedAmount: action.DividendPerShare,
                ActualAmount: null,
                VarianceAmount: null,
                FactorStart: null,
                FactorEnd: action.SplitRatio ?? action.DistributionRatio ?? action.ExchangeRatio,
                Currency: string.IsNullOrWhiteSpace(action.Currency) ? currency : action.Currency.Trim(),
                PostingStatus: postingStatus,
                SourceSystem: "corporate-action-stream",
                SourceRecordId: action.CorpActId.ToString("N"),
                SourceAsOfUtc: null,
                SourceUpdatedBy: null,
                SourceReason: "Corporate action event attached to the security history.",
                IsDerivedFromEconomicTerms: false,
                IsCurrentProjection: action.ExDate >= DateOnly.FromDateTime(DateTime.UtcNow)));
        }

        var orderedEvents = events
            .OrderBy(static item => item.EffectiveDate)
            .ThenBy(static item => item.EventType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.EventId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var provenanceHistory = BuildScheduleProvenanceHistory(orderedEvents, factorHistory, history, winningSource);

        return new SecurityMasterScheduleBookDto(
            SupportsCashflowSchedule: summary.SupportsCashflowSchedule,
            SupportsFactorHistory: summary.SupportsFactorHistory,
            HasEconomicScheduleTerms: summary.HasEconomicScheduleTerms,
            Currency: currency,
            CurrentFactor: summary.CurrentFactor,
            CurrentFactorDate: summary.CurrentFactorDate,
            NextLifecycleDate: summary.NextLifecycleDate,
            SourceSummary: summary.SourceSummary,
            Summary: summary.Summary,
            Events: orderedEvents,
            FactorHistory: factorHistory,
            ProvenanceHistory: provenanceHistory,
            DeclaredExercise: summary.DeclaredExercise);
    }

    private static void AppendScheduleEventsFromArray(
        ICollection<SecurityMasterScheduleEventDto> destination,
        JsonElement? economicTerms,
        string propertyName,
        string defaultCurrency,
        WinningSourceInfo? winningSource)
    {
        if (!economicTerms.HasValue
            || !TryGetPropertyCaseInsensitive(economicTerms.Value, propertyName, out var scheduleArray)
            || scheduleArray.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var index = 0;
        foreach (var entry in scheduleArray.EnumerateArray())
        {
            index++;
            if (entry.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var effectiveDate =
                TryGetJsonDateOnly(entry, "effectiveDate")
                ?? TryGetJsonDateOnly(entry, "paymentDate")
                ?? TryGetJsonDateOnly(entry, "payDate")
                ?? TryGetJsonDateOnly(entry, "date");

            if (!effectiveDate.HasValue)
            {
                continue;
            }

            var expectedAmount = TryGetJsonDecimal(entry, "expectedAmount")
                ?? TryGetJsonDecimal(entry, "amount")
                ?? TryGetJsonDecimal(entry, "principalAmount")
                ?? TryGetJsonDecimal(entry, "interestAmount");
            var actualAmount = TryGetJsonDecimal(entry, "actualAmount");
            decimal? varianceAmount = expectedAmount.HasValue && actualAmount.HasValue
                ? actualAmount.Value - expectedAmount.Value
                : (decimal?)null;

            destination.Add(new SecurityMasterScheduleEventDto(
                EventId: TryGetJsonString(entry, "eventId") ?? $"{propertyName}-{effectiveDate.Value:yyyyMMdd}-{index}",
                EventType: TryGetJsonString(entry, "eventType") ?? InferScheduleEventType(propertyName),
                EffectiveDate: effectiveDate.Value,
                PayDate: TryGetJsonDateOnly(entry, "paymentDate") ?? TryGetJsonDateOnly(entry, "payDate"),
                AccrualStartDate: TryGetJsonDateOnly(entry, "accrualStartDate"),
                AccrualEndDate: TryGetJsonDateOnly(entry, "accrualEndDate"),
                ExpectedAmount: expectedAmount,
                ActualAmount: actualAmount,
                VarianceAmount: varianceAmount,
                FactorStart: TryGetJsonDecimal(entry, "factorStart"),
                FactorEnd: TryGetJsonDecimal(entry, "factorEnd") ?? TryGetJsonDecimal(entry, "factor"),
                Currency: TryGetJsonString(entry, "currency") ?? defaultCurrency,
                PostingStatus: TryGetJsonString(entry, "postingStatus") ?? "Projected",
                SourceSystem: TryGetJsonString(entry, "sourceSystem") ?? winningSource?.SourceSystem ?? "economic-terms",
                SourceRecordId: TryGetJsonString(entry, "sourceRecordId") ?? winningSource?.SourceRecordId,
                SourceAsOfUtc: TryGetJsonDateTimeOffset(entry, "asOf") ?? winningSource?.AsOf,
                SourceUpdatedBy: TryGetJsonString(entry, "updatedBy") ?? winningSource?.UpdatedBy,
                SourceReason: TryGetJsonString(entry, "reason") ?? winningSource?.Reason,
                IsDerivedFromEconomicTerms: true,
                IsCurrentProjection: true));
        }
    }

    private static IReadOnlyList<SecurityMasterFactorPointDto> BuildFactorHistory(
        JsonElement? economicTerms,
        WinningSourceInfo? winningSource,
        decimal? currentFactor,
        DateOnly? currentFactorDate)
    {
        var points = new List<SecurityMasterFactorPointDto>();
        var factorHistory = TryGetNestedJsonElement(economicTerms, "structuredProduct", "factorHistory");

        if (factorHistory.HasValue && factorHistory.Value.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var entry in factorHistory.Value.EnumerateArray())
            {
                index++;
                if (entry.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var effectiveDate =
                    TryGetJsonDateOnly(entry, "effectiveDate")
                    ?? TryGetJsonDateOnly(entry, "factorDate")
                    ?? TryGetJsonDateOnly(entry, "date");
                var factor = TryGetJsonDecimal(entry, "factor");
                if (!effectiveDate.HasValue || !factor.HasValue)
                {
                    continue;
                }

                points.Add(new SecurityMasterFactorPointDto(
                    PointId: TryGetJsonString(entry, "pointId") ?? $"factor-{effectiveDate.Value:yyyyMMdd}-{index}",
                    EffectiveDate: effectiveDate.Value,
                    Factor: factor.Value,
                    PreviousFactor: TryGetJsonDecimal(entry, "previousFactor"),
                    SourceSystem: TryGetJsonString(entry, "sourceSystem") ?? winningSource?.SourceSystem ?? "economic-terms",
                    SourceRecordId: TryGetJsonString(entry, "sourceRecordId") ?? winningSource?.SourceRecordId,
                    SourceAsOfUtc: TryGetJsonDateTimeOffset(entry, "asOf") ?? winningSource?.AsOf,
                    SourceUpdatedBy: TryGetJsonString(entry, "updatedBy") ?? winningSource?.UpdatedBy,
                    SourceReason: TryGetJsonString(entry, "reason") ?? winningSource?.Reason,
                    IsCurrentFactor: false));
            }
        }

        if (points.Count == 0 && currentFactor.HasValue && currentFactorDate.HasValue)
        {
            points.Add(new SecurityMasterFactorPointDto(
                PointId: $"factor-{currentFactorDate.Value:yyyyMMdd}",
                EffectiveDate: currentFactorDate.Value,
                Factor: currentFactor.Value,
                PreviousFactor: null,
                SourceSystem: winningSource?.SourceSystem ?? "economic-terms",
                SourceRecordId: winningSource?.SourceRecordId,
                SourceAsOfUtc: winningSource?.AsOf,
                SourceUpdatedBy: winningSource?.UpdatedBy,
                SourceReason: winningSource?.Reason,
                IsCurrentFactor: true));
        }

        var ordered = points
            .OrderBy(static point => point.EffectiveDate)
            .ThenBy(static point => point.PointId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        for (var index = 0; index < ordered.Length; index++)
        {
            var previousFactor = ordered[index].PreviousFactor ?? (index > 0 ? ordered[index - 1].Factor : null);
            var isCurrentFactor = currentFactorDate.HasValue
                ? ordered[index].EffectiveDate == currentFactorDate.Value
                : index == ordered.Length - 1;

            ordered[index] = ordered[index] with
            {
                PreviousFactor = previousFactor,
                IsCurrentFactor = isCurrentFactor
            };
        }

        return ordered;
    }

    private static IReadOnlyList<SecurityMasterScheduleProvenanceDto> BuildScheduleProvenanceHistory(
        IReadOnlyList<SecurityMasterScheduleEventDto> events,
        IReadOnlyList<SecurityMasterFactorPointDto> factorHistory,
        IReadOnlyList<SecurityMasterEventEnvelope> history,
        WinningSourceInfo? winningSource)
    {
        var provenance = new List<SecurityMasterScheduleProvenanceDto>();

        if (winningSource is not null)
        {
            provenance.Add(new SecurityMasterScheduleProvenanceDto(
                ProvenanceId: "winning-source",
                Category: "WinningSource",
                Summary: $"Golden-copy schedule source {winningSource.SourceSystem}.",
                EffectiveDate: null,
                SourceSystem: winningSource.SourceSystem,
                SourceRecordId: winningSource.SourceRecordId,
                SourceAsOfUtc: winningSource.AsOf,
                SourceUpdatedBy: winningSource.UpdatedBy,
                SourceReason: winningSource.Reason,
                StreamVersion: null,
                EventType: null));
        }

        provenance.AddRange(events.Select(eventItem => new SecurityMasterScheduleProvenanceDto(
            ProvenanceId: $"event-{eventItem.EventId}",
            Category: "ScheduleEvent",
            Summary: $"{eventItem.EventType} effective {eventItem.EffectiveDate:yyyy-MM-dd}.",
            EffectiveDate: eventItem.EffectiveDate,
            SourceSystem: eventItem.SourceSystem,
            SourceRecordId: eventItem.SourceRecordId,
            SourceAsOfUtc: eventItem.SourceAsOfUtc,
            SourceUpdatedBy: eventItem.SourceUpdatedBy,
            SourceReason: eventItem.SourceReason,
            StreamVersion: null,
            EventType: eventItem.EventType)));

        provenance.AddRange(factorHistory.Select(point => new SecurityMasterScheduleProvenanceDto(
            ProvenanceId: $"factor-{point.PointId}",
            Category: "FactorHistory",
            Summary: $"Factor {point.Factor:0.########} effective {point.EffectiveDate:yyyy-MM-dd}.",
            EffectiveDate: point.EffectiveDate,
            SourceSystem: point.SourceSystem,
            SourceRecordId: point.SourceRecordId,
            SourceAsOfUtc: point.SourceAsOfUtc,
            SourceUpdatedBy: point.SourceUpdatedBy,
            SourceReason: point.SourceReason,
            StreamVersion: null,
            EventType: "FactorUpdate")));

        foreach (var envelope in history.Where(IsScheduleHistoryEvent))
        {
            provenance.Add(new SecurityMasterScheduleProvenanceDto(
                ProvenanceId: $"history-{envelope.StreamVersion}",
                Category: "History",
                Summary: $"{envelope.EventType} recorded at {envelope.EventTimestamp:yyyy-MM-dd HH:mm} UTC.",
                EffectiveDate: null,
                SourceSystem: TryGetJsonString(envelope.Metadata, "sourceSystem")
                    ?? TryGetJsonString(envelope.Metadata, "source")
                    ?? winningSource?.SourceSystem
                    ?? "security-history",
                SourceRecordId: TryGetJsonString(envelope.Metadata, "sourceRecordId"),
                SourceAsOfUtc: envelope.EventTimestamp,
                SourceUpdatedBy: envelope.Actor,
                SourceReason: TryGetJsonString(envelope.Metadata, "reason"),
                StreamVersion: envelope.StreamVersion,
                EventType: envelope.EventType));
        }

        return provenance
            .OrderByDescending(static item => item.SourceAsOfUtc ?? DateTimeOffset.MinValue)
            .ThenByDescending(static item => item.EffectiveDate ?? DateOnly.MinValue)
            .ThenBy(static item => item.ProvenanceId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void AppendLifecycleEvent(
        ICollection<SecurityMasterScheduleEventDto> destination,
        string eventType,
        DateOnly? effectiveDate,
        string currency,
        WinningSourceInfo? winningSource)
    {
        if (!effectiveDate.HasValue
            || destination.Any(existing => string.Equals(existing.EventType, eventType, StringComparison.OrdinalIgnoreCase)
                && existing.EffectiveDate == effectiveDate.Value))
        {
            return;
        }

        destination.Add(new SecurityMasterScheduleEventDto(
            EventId: $"{eventType.ToLowerInvariant()}-{effectiveDate.Value:yyyyMMdd}",
            EventType: eventType,
            EffectiveDate: effectiveDate.Value,
            PayDate: effectiveDate,
            AccrualStartDate: null,
            AccrualEndDate: null,
            ExpectedAmount: null,
            ActualAmount: null,
            VarianceAmount: null,
            FactorStart: null,
            FactorEnd: null,
            Currency: currency,
            PostingStatus: effectiveDate.Value < DateOnly.FromDateTime(DateTime.UtcNow) ? "Reference" : "Projected",
            SourceSystem: winningSource?.SourceSystem ?? "economic-terms",
            SourceRecordId: winningSource?.SourceRecordId,
            SourceAsOfUtc: winningSource?.AsOf,
            SourceUpdatedBy: winningSource?.UpdatedBy,
            SourceReason: winningSource?.Reason,
            IsDerivedFromEconomicTerms: true,
            IsCurrentProjection: true));
    }

    private static string InferScheduleEventType(string propertyName)
        => propertyName.Equals("paymentSchedule", StringComparison.OrdinalIgnoreCase)
            ? "Payment"
            : propertyName.Equals("cashflows", StringComparison.OrdinalIgnoreCase)
                ? "CashFlow"
                : "Schedule";

    private static bool IsScheduleHistoryEvent(SecurityMasterEventEnvelope envelope)
    {
        var eventType = envelope.EventType.AsSpan();
        if (eventType.Contains("factor", StringComparison.OrdinalIgnoreCase)
            || eventType.Contains("coupon", StringComparison.OrdinalIgnoreCase)
            || eventType.Contains("payment", StringComparison.OrdinalIgnoreCase)
            || eventType.Contains("maturity", StringComparison.OrdinalIgnoreCase)
            || eventType.Contains("call", StringComparison.OrdinalIgnoreCase)
            || eventType.Contains("corp", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var payloadText = envelope.Payload.GetRawText();
        return payloadText.Contains("factor", StringComparison.OrdinalIgnoreCase)
            || payloadText.Contains("coupon", StringComparison.OrdinalIgnoreCase)
            || payloadText.Contains("payment", StringComparison.OrdinalIgnoreCase)
            || payloadText.Contains("cashflow", StringComparison.OrdinalIgnoreCase)
            || payloadText.Contains("maturity", StringComparison.OrdinalIgnoreCase);
    }
}
