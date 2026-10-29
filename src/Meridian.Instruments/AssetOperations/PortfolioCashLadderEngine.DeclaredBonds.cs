using System.Text.Json;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.SecurityMaster.Schedules;

namespace Meridian.Instruments.AssetOperations;

public static partial class PortfolioCashLadderEngine
{
    private static readonly JsonSerializerOptions EvidenceJson = new() { PropertyNameCaseInsensitive = true };

    private static bool HasDeclaredTerms(AssetOperationsDetailDto operations)
    {
        if (!operations.Subject.AssetClass.Equals("Bond", StringComparison.OrdinalIgnoreCase))
            return false;
        var terms = SelectTermsForRun(operations, SelectProjectionRun(operations));
        return (terms is not null ? new[] { terms } : operations.TermsHistory)
            .Any(row => row.ExtensionPayload is { } payload && EnumerateTermSources(payload)
                .Any(source => source.EnumerateObject().Any(member =>
                    member.Name.Equals("scheduleConventions", StringComparison.OrdinalIgnoreCase)
                    && member.Value.ValueKind == JsonValueKind.Object && member.Value.EnumerateObject().Any())));
    }

    private static SecurityDetailDto RetainedDeclaredSecurity(PortfolioCashLadderPositionDto position)
    {
        var operations = position.Operations;
        var run = SelectProjectionRun(operations);
        var terms = SelectTermsForRun(operations, run);
        if (run is null || !run.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase)
            || terms?.ExtensionPayload is not { } payload || run.ExtensionPayload is not { } retained
            || !retained.TryGetProperty("termsVersionId", out var version) || !version.TryGetGuid(out var id)
            || id != terms.TermsVersionId || !retained.TryGetProperty("termsHash", out var hash)
            || hash.GetString() != terms.TermsHash || terms.SecurityId != operations.Subject.SecurityId
            || !retained.TryGetProperty("termsContentSha256", out var content)
            || content.GetString() != Meridian.Contracts.Integrity.Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(payload)))
            throw new InvalidOperationException("Declared bond scenario requires the exact retained terms bound to the selected projection run.");
        var common = payload.TryGetProperty("CommonTerms", out var commonTerms) ? commonTerms : JsonSerializer.SerializeToElement(new { });
        var asset = payload.TryGetProperty("AssetSpecificTerms", out var assetTerms) ? assetTerms : payload;
        var currencies = LatestRunFlows(operations).Select(flow => flow.Currency).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (currencies.Length != 1 || string.IsNullOrWhiteSpace(currencies[0]))
            throw new InvalidOperationException("Declared bond scenario requires one retained cash-flow currency.");
        return new(operations.Subject.SecurityId, "Bond", default, operations.Subject.DisplayName,
            currencies[0], common, asset, [], [], terms.VersionNumber,
            new DateTimeOffset(terms.EffectiveDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), null);
    }

    private static BondRedemptionScenario? ResolveDeclaredRedemption(PortfolioCashLadderPositionDto position,
        PortfolioCashLadderInputs inputs, string scenarioId, DateOnly start, DateOnly end)
    {
        var security = RetainedDeclaredSecurity(position);
        var terms = StructuredCashFlowTermsResolver.Resolve(security);
        var run = SelectProjectionRun(position.Operations)!;
        if (run.ExtensionPayload is { } retained && retained.TryGetProperty("inputManifest", out var manifest)
            && manifest.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in manifest.EnumerateArray())
            {
                var input = item.Deserialize<BondCalculationInput>(EvidenceJson);
                if (input?.Kind != "BusinessDayCalendar")
                    continue;
                if (inputs.Calendars is null || !(inputs.Calendars.TryGet(input.Key, out var calendar)
                        || inputs.Calendars.TryResolve(input.Key.Split('+'), out calendar))
                    || calendar.ContentHash != input.ContentSha256)
                    throw new InvalidOperationException("The retained projection calendar is unavailable or changed; declared exercise is blocked.");
            }
        }
        var declared = ScheduleSpecFactory.Resolve(security, terms, inputs.Calendars);
        if (!declared.IsDeclared)
            throw new InvalidOperationException(declared.Refusal?.Message ?? "Declared schedule is unavailable.");
        var schedule = ScheduleGenerator.Generate(declared.Spec!);
        if (!schedule.Succeeded)
            throw new InvalidOperationException(schedule.Failure.Message);
        // Fixed/zero whole redemption scenarios are supported; other structures require their own model.
        if (terms.CouponKind is not (SecurityCouponKind.Fixed or SecurityCouponKind.ZeroCoupon)
            || terms.PrincipalFace is not > 0m || terms.HasStepCouponSchedule || terms.InflationIndex is not null)
            throw new InvalidOperationException("Declared redemption needs retained fixed/zero coupons and positive principal face.");
        var rate = declared.Conventions.CouponRateUnit switch
        {
            "Percent" => terms.CouponRate / 100m,
            "Fraction" => terms.CouponRate,
            _ => null
        };
        if (rate is null or < 0m)
            throw new InvalidOperationException("Declared redemption needs explicit coupon units and a supported coupon.");
        if (!BondContractualEconomics.TryBuild(security.SecurityId, terms, declared, schedule, start,
                rate.Value, terms.CouponKind == SecurityCouponKind.ZeroCoupon, BondYieldConvention.AnnualEffective,
                out var flows, out _, out var reason)
            || !BondExerciseEconomics.TryResolveScenarios(security, terms, declared, schedule, start,
                rate.Value, BondYieldConvention.AnnualEffective, declared.Spec!.Tenor.PeriodsPerYear ?? 1,
                flows, inputs.Calendars, out var scenarios, out reason))
            throw new InvalidOperationException(reason);
        var kind = scenarioId == HolderPutScenarioId ? "Put" : "Call";
        var eligible = scenarios.Where(item => item.Kind == kind && item.ExerciseDate >= start && item.ExerciseDate < end)
            .OrderBy(item => item.ExerciseDate).ToArray();
        if (eligible.Any(item => item.LatestNoticeDate < start))
            throw new InvalidOperationException("An elapsed exercise notice deadline requires retained notice evidence before a cash scenario can assume exercise.");
        var selected = eligible.FirstOrDefault();
        if (selected is not null && selected.AccruedPer100 != 0m && terms.HasPrincipalSchedule)
        {
            var period = schedule.Periods.FirstOrDefault(item => item.AccrualStart < selected.ExerciseDate && selected.ExerciseDate < item.AccrualEnd);
            if (period is null || terms.PrincipalSchedule!.Any(payment => payment.PaymentDate > period.AccrualStart
                    && payment.PaymentDate < selected.ExerciseDate))
                throw new InvalidOperationException("Off-coupon redemption with intra-period principal changes requires retained coupon-entitlement cash.");
        }
        return selected;
    }

    private static void ValidateDeclaredScenario(PortfolioCashLadderInputs inputs, string scenarioId, DateOnly start, DateOnly end)
    {
        if (scenarioId is not (EarlyCallScenarioId or HolderPutScenarioId or RatesUpScenarioId or RatesDownScenarioId))
            return;
        if (scenarioId is EarlyCallScenarioId or HolderPutScenarioId)
            foreach (var group in inputs.Positions.Where(position => position.Quantity != 0m && HasDeclaredTerms(position.Operations))
                         .GroupBy(position => position.Operations.Subject.SecurityId))
                if (group.Select(position => SelectProjectionRun(position.Operations)?.ProjectionRunId).Distinct().Count() > 1)
                    throw new InvalidOperationException("Declared redemption cannot combine different retained projection runs for one security.");
        foreach (var position in inputs.Positions.Where(position => position.Quantity != 0m && HasDeclaredTerms(position.Operations)))
        {
            if (scenarioId is EarlyCallScenarioId or HolderPutScenarioId)
            { _ = ResolveDeclaredRedemption(position, inputs, scenarioId, start, end); continue; }
            var security = RetainedDeclaredSecurity(position);
            if (StructuredCashFlowTermsResolver.Resolve(security).CouponKind != SecurityCouponKind.Floating)
                continue;
            foreach (var flow in SelectPositionFlows(position, start, end).Where(flow => ResolveInstrumentLane(flow.FlowType) == CouponLane))
                _ = ShiftedDeclaredCoupon(flow, inputs.AsOfDate, scenarioId == RatesUpScenarioId ? RateShiftPerHundredBps : -RateShiftPerHundredBps);
        }
    }

    private static decimal ShiftedDeclaredCoupon(AssetProjectedCashFlowDto flow, DateOnly asOf, decimal shift)
    {
        if (flow.ExtensionPayload is not { } payload || !payload.TryGetProperty("rateEvidence", out var json)
            || json.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("Declared floating scenario lacks retained per-reset rate evidence.");
        BondFloatingRateEvidence evidence;
        try
        { evidence = json.Deserialize<BondFloatingRateEvidence>(EvidenceJson) ?? throw new JsonException(); }
        catch (JsonException) { throw new InvalidOperationException("Declared floating reset evidence is unreadable."); }
        if (flow.AccrualStartDate != evidence.ResetDate || evidence.IsObserved != (evidence.ResetDate <= asOf)
            || !evidence.TryCoupon(0m, out var original, out var reason) || RoundCash(original) != flow.Amount)
            throw new InvalidOperationException("Declared floating cash does not reconcile to its retained reset evidence.");
        if (!evidence.TryCoupon(shift, out var shocked, out reason))
            throw new InvalidOperationException(reason);
        return RoundCash(shocked);
    }

    private static List<PortfolioCashLadderContributionDto> ApplyDeclaredRateShift(
        List<PortfolioCashLadderContributionDto> rows, PortfolioCashLadderInputs inputs, decimal shift)
    {
        var result = new List<PortfolioCashLadderContributionDto>();
        var index = 0;
        foreach (var position in inputs.Positions)
        {
            if (position.Quantity == 0m)
                continue;
            var declaredFloating = position.Quantity != 0m && HasDeclaredTerms(position.Operations)
                && StructuredCashFlowTermsResolver.Resolve(RetainedDeclaredSecurity(position)).CouponKind == SecurityCouponKind.Floating;
            foreach (var flow in SelectPositionFlows(position, inputs.AsOfDate, inputs.AsOfDate.AddDays(Math.Max(1, inputs.HorizonDays))))
            {
                var row = rows[index++];
                if (declaredFloating && ResolveInstrumentLane(flow.FlowType) == CouponLane)
                {
                    var cash = ShiftedDeclaredCoupon(flow, inputs.AsOfDate, shift);
                    row = row with
                    {
                        Amount = RoundCash(cash * position.Quantity * ResolveInstrumentDirection(flow.FlowType)),
                        ScenarioAdjustment = $"Retained reset economics; forecast index shift {shift:P2}, observed coupons unchanged."
                    };
                }
                result.Add(row);
            }
        }
        result.AddRange(rows.Skip(index));
        return result;
    }

    private static List<PortfolioCashLadderContributionDto> ApplyDeclaredRedemptions(
        List<PortfolioCashLadderContributionDto> rows, PortfolioCashLadderInputs inputs, string scenarioId, DateOnly start, DateOnly end)
    {
        var result = rows;
        foreach (var group in inputs.Positions.Where(position => position.Quantity != 0m && HasDeclaredTerms(position.Operations))
                     .GroupBy(position => position.Operations.Subject.SecurityId))
        {
            var position = group.First();
            var exercise = ResolveDeclaredRedemption(position, inputs, scenarioId, start, end);
            if (exercise is null)
                continue;
            var operations = position.Operations;
            var remaining = LatestRunFlows(operations).Where(flow => flow.DueDate >= exercise.ExerciseDate
                && ResolveInstrumentLane(flow.FlowType) == PrincipalLane).ToArray();
            var face = remaining.Sum(flow => flow.Amount);
            if (face <= 0m)
                throw new InvalidOperationException("Declared exercise lacks retained outstanding principal cash.");
            var amount = BondExerciseEconomics.RedemptionCash(face, exercise.RedemptionPrice, face * exercise.AccruedPer100 / 100m);
            result = result.Where(row => row.SecurityId != group.Key || row.DueDate < exercise.ExerciseDate
                || row.DueDate == exercise.ExerciseDate && row.SourceLane == CouponLane).ToList();
            var redemption = BuildCallRedemptionRow(operations, exercise.ExerciseDate, RoundCash(amount * group.Sum(item => item.Quantity)), remaining[0].Currency);
            result.Add(redemption with
            {
                FlowType = exercise.Kind == "Call" ? "CallRedemption" : "PutRedemption",
                ScenarioAdjustment = $"Declared {exercise.Kind} at {exercise.RedemptionPrice} percent of outstanding face; contractual accrued cash included."
            });
        }
        return result;
    }
}
