using System.Text.Json;
using Meridian.Contracts.CashFlows;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// Bounded authoring and analytical routing over the existing fixed/floating leg contract.
/// This adapter creates the existing calculation contract and never grants posting authority.
/// </summary>
public static class SecurityVanillaSwapProfile
{
    public const string Id = "vanilla-irs-v1";

    public static ContractualCashFlowContract FromSecurity(SecurityDetailDto security)
    {
        if (!SecurityBoundedProfileCommonTerms.IsIdentityContext(security.CommonTerms))
            throw Invalid("Supplemental common-term economics require a separately supported swap profile.");
        return Build(security.AssetClass, security.AssetSpecificTerms, security.Currency, forWrite: false);
    }

    internal static bool HasLegDeclarations(string assetClass, JsonElement payload)
        => SecurityAssetClassCatalog.GetOrDefault(assetClass).CashFlowBehavior == SecurityCashFlowBehavior.SwapLegs
           && Rows(assetClass, payload).Any(leg => leg.ValueKind == JsonValueKind.Object
               && SecurityTermReader.TryGetProperty(leg, SecurityVersionedTerms.ScheduleConventionsKey, out var block)
               && SecurityAssetTermsSchema.IsPopulatedTermValue(block));

    internal static ContractualCashFlowContract Build(string assetClass, JsonElement payload, string? currency, bool forWrite)
    {
        try
        {
            return BuildCore(assetClass, payload, currency, forWrite);
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException or OverflowException)
        {
            throw Invalid(exception.Message);
        }
    }

    private static ContractualCashFlowContract BuildCore(string assetClass, JsonElement payload, string? currency, bool forWrite)
    {
        if (SecurityAssetClassCatalog.GetOrDefault(assetClass).CashFlowBehavior != SecurityCashFlowBehavior.SwapLegs)
            throw Invalid("The vanilla IRS profile requires a registered swap-leg asset class.");
        if (!forWrite)
            SecurityTermsEnvelope.EnsureReadableAsAssetSpecificTerms(assetClass, payload);
        var field = SecurityAssetTermsSchema.ElementFields(assetClass, "legs")
            .Single(static field => field.Key == SecurityVersionedTerms.ScheduleConventionsKey);
        var through = forWrite ? SecurityAssetClassCatalog.GetTermsVersion(assetClass)
            : SecurityTermsEnvelope.ResolveAssetClassTermsVersion(payload);
        if (through < field.SinceTermsVersion)
            throw Invalid("Leg conventions are undeclared at this record's terms version; explicitly review a supported replacement.");
        var permitted = SecurityAssetTermsSchema.Fields(assetClass).Select(static field => field.Key)
            .Concat(new[] { SecurityTermsEnvelope.SchemaVersionKey, SecurityTermsEnvelope.FormatKey, SecurityTermsEnvelope.AssetClassTermsVersionKey })
            .ToHashSet(StringComparer.Ordinal);
        if (payload.ValueKind != JsonValueKind.Object || payload.EnumerateObject().Any(property => !permitted.Contains(property.Name))
            || payload.TryGetProperty("cashFlowContract", out var companion) && SecurityAssetTermsSchema.IsPopulatedTermValue(companion))
            throw Invalid("Supplemental or optional payoff terms are outside the vanilla IRS profile.");
        var issue = SecurityTermReader.ReadDate([payload], "effectiveDate")
            ?? throw Invalid("An explicit effectiveDate is required.");
        var maturity = SecurityTermReader.ReadDate([payload], "maturityDate")
            ?? throw Invalid("An explicit maturityDate is required.");
        var rows = Rows(assetClass, payload);
        if (rows.Length != 2 || rows.Any(static row => row.ValueKind != JsonValueKind.Object))
            throw Invalid("Exactly two complete leg rows are required.");
        var legFields = SecurityAssetTermsSchema.ElementFields(assetClass, "legs");
        var streams = new List<ContractualCashFlowStream>(2);
        var kinds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var leg in rows)
        {
            ScheduleTermsWriteRules.ValidateLegMembers(assetClass, leg);
            string? Text(string key) => SecurityTermReader.ReadString([leg], Spellings(legFields, key));
            decimal? Number(string key) => ReadNumber(leg, Spellings(legFields, key));
            var id = Text("legId");
            var kind = Text("legType");
            var legCurrency = Text("currency");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(legCurrency))
                throw Invalid("Each leg requires an explicit stable legId and currency.");
            currency ??= legCurrency;
            if (!string.Equals(currency, legCurrency, StringComparison.OrdinalIgnoreCase))
                throw Invalid("Cross-currency legs are outside the vanilla IRS profile; no FX conversion is inferred.");
            if (kind is not ("Fixed" or "Floating") || !kinds.Add(kind))
                throw Invalid("The vanilla IRS profile requires one Fixed and one Floating leg.");
            var direction = Text("direction") switch
            {
                "Pay" => CashFlowLegDirection.Pay,
                "Receive" => CashFlowLegDirection.Receive,
                _ => throw Invalid("Each leg requires an explicit Pay or Receive direction.")
            };
            if (!SecurityTermReader.TryGetProperty(leg, "exchangesPrincipal", out var exchange)
                || exchange.ValueKind != JsonValueKind.False)
                throw Invalid("Vanilla IRS legs must explicitly set exchangesPrincipal=false.");
            if (Number("notional") is not > 0m)
                throw Invalid("Every leg requires an explicit positive notional.");
            if (SecurityTermReader.TryGetProperty(leg, "notionalSchedule", out var steps)
                && SecurityAssetTermsSchema.IsPopulatedTermValue(steps))
                throw Invalid("Amortizing or accreting notionals are outside the vanilla IRS profile.");
            if (!SecurityTermReader.TryGetProperty(leg, SecurityVersionedTerms.ScheduleConventionsKey, out var rawBlock)
                || !SecurityAssetTermsSchema.IsPopulatedTermValue(rawBlock))
                throw Invalid("Each leg requires its own declared schedule conventions and rate unit.");
            ScheduleTermsWriteRules.ValidateLegBlock(assetClass, rawBlock);
            var block = SecurityVersionedTerms.ReadScheduleConventions(leg, SecurityVersionedTerms.ScheduleConventionsKey);
            ScheduleSpecFactory.ValidateLegConventionShape(assetClass, block);
            block = block with
            {
                BusinessDayConvention = block.BusinessDayConvention is { } businessConvention
                    && SecurityTermConventions.TryParseBusinessDayConvention(businessConvention, out var parsedConvention)
                    ? parsedConvention.ToString() : block.BusinessDayConvention,
                StubPolicy = block.StubPolicy is { } stub && SecurityTermConventions.TryParseStubPolicy(stub, out var parsedStub)
                    ? parsedStub.ToString() : block.StubPolicy
            };
            var unit = block.CouponRateUnit switch
            {
                "Percent" => RateUnit.Percent,
                "Fraction" => RateUnit.Fraction,
                _ => throw Invalid("Each leg must explicitly declare Percent or Fraction in its own convention block.")
            };
            if (!SecurityTermConventions.TryParsePaymentFrequency(Text("paymentFrequency"), out var cadence)
                || cadence.MonthsPerPeriod() is not > 0)
                throw Invalid("Each leg requires an explicit supported monthly-or-longer payment frequency.");
            if (!DayCountConventions.TryParse(Text("dayCount"), out var dayCount) || dayCount == DayCountConvention.Unknown)
                throw Invalid("Each leg requires an explicit supported day count.");
            if (dayCount == DayCountConvention.Business252 && block.PaymentCalendars.Count == 0
                || dayCount == DayCountConvention.ActualActualIcma && block.AccrualAdjusted == true)
                throw Invalid("The leg's day count requires compatible calendar and accrual-adjustment evidence.");
            decimal Normalize(decimal raw, string key)
                => RateUnitNormalizer.Normalize(assetClass, key, raw, unit).TryGetFraction(out var fraction)
                    ? fraction : throw Invalid("A leg rate is outside its declared unit or supported range.");
            var fixedRate = Number("fixedRate");
            var index = Text("index");
            if (kind == "Fixed" && (fixedRate is null || index is not null)
                || kind == "Floating" && (fixedRate is not null || string.IsNullOrWhiteSpace(index)))
                throw Invalid("Fixed legs require a fixed rate; floating legs require an index and no fixed rate.");
            var spread = Number("spreadBps") ?? 0m;
            if (!RateUnitNormalizer.Normalize(assetClass, "legs.spreadBps", spread).TryGetFraction(out _))
                throw Invalid("The floating spread is outside its declared basis-point range.");
            if (kind == "Fixed" && spread != 0m)
                throw Invalid("A fixed leg cannot also assert a floating spread.");
            if (SecurityTermReader.TryGetProperty(leg, "currentIndexRate", out var undatedRate)
                && SecurityAssetTermsSchema.IsPopulatedTermValue(undatedRate))
                throw Invalid("The vanilla IRS profile requires dated reset fixings or pinned forward curves; currentIndexRate is not dated rate evidence.");
            var resets = ReadResets(assetClass, leg, unit);
            if (kind == "Fixed" && resets.Count != 0)
                throw Invalid("A fixed leg cannot also declare floating reset fixings.");
            streams.Add(new(id, Number("notional")!.Value, cadence.MonthsPerPeriod()!.Value, dayCount,
                fixedRate is { } rate ? Normalize(rate, "legs.fixedRate") : null, direction,
                ExchangesPrincipal: false, Conventions: block, IndexName: kind == "Floating" ? index : null,
                SpreadBps: spread, Resets: resets.Count == 0 ? null : resets));
        }
        var contract = new ContractualCashFlowContract(issue, maturity, currency!, streams);
        ValidateContract(contract);
        foreach (var stream in streams)
            ContractualCashFlowEngine.ValidateScheduleShape(contract, stream);
        return contract;
    }

    /// <summary>Rechecks the bounded shape on normalized retained packets during replay.</summary>
    public static void ValidateContract(ContractualCashFlowContract contract)
    {
        if (contract.Streams.Count != 2 || contract.Streams.Count(static stream => stream.IndexName is null) != 1
            || contract.Streams.Count(static stream => stream.IndexName is not null) != 1
            || contract.Streams[0].Direction == contract.Streams[1].Direction
            || contract.Streams[0].Principal <= 0m || contract.Streams[0].Principal != contract.Streams[1].Principal
            || contract.Exercises is { Count: > 0 }
            || contract.Streams.Any(static stream => stream.ExchangesPrincipal || stream.NotionalSchedule is { Count: > 0 }
                || stream.PrincipalSchedule is { Count: > 0 } || stream.PikFraction != 0m || stream.PrepaymentAllowed
                || stream.Floor is not null || stream.Cap is not null
                || stream.RateSteps is { Count: > 0 } || stream.CouponAmounts is { Count: > 0 } || stream.CouponPeriods is not null
                || stream.Amortization != CashFlowAmortization.Bullet || stream.OpeningBalance is not null
                || stream.OpeningBalanceDate is not null || stream.PrepaymentPenaltyRate != 0m
                || stream.Conventions?.CouponRateUnit is not ("Percent" or "Fraction")
                || stream.FixedRate is < -1m or > 1m || stream.SpreadBps is < -10_000m or > 10_000m
                || stream.Resets?.Any(static reset => reset.FixingRate is < -1m or > 1m) == true
                || stream.IndexName is null && stream.FixedRate is null || stream.IndexName is not null && stream.FixedRate is not null))
            throw Invalid("Vanilla IRS requires equal fixed notionals, opposite directions, one fixed and one floating leg, and no principal exchange or optional payoff.");
        ContractualCashFlowEngine.Validate(contract, new(contract.IssueDate, DateTimeOffset.UnixEpoch));
    }

    private static JsonElement[] Rows(string assetClass, JsonElement payload)
    {
        var field = SecurityAssetTermsSchema.Field(assetClass, "legs");
        if (field is null)
            return [];
        foreach (var key in new[] { field.Key }.Concat(field.Aliases))
            if (SecurityTermReader.TryGetProperty(payload, key, out var rows) && rows.ValueKind == JsonValueKind.Array)
                return rows.EnumerateArray().ToArray();
        return [];
    }

    private static IReadOnlyList<StructuredCashFlowResetFixing> ReadResets(string assetClass, JsonElement leg, RateUnit unit)
    {
        if (!SecurityTermReader.TryGetProperty(leg, "resetSchedule", out var array) || array.ValueKind == JsonValueKind.Null)
            return [];
        if (array.ValueKind != JsonValueKind.Array)
            throw Invalid("Reset fixings must be complete dated rows.");
        var fields = SecurityAssetTermsSchema.ElementFields(assetClass, "legs.resetSchedule");
        var rows = new List<StructuredCashFlowResetFixing>();
        foreach (var row in array.EnumerateArray())
        {
            ScheduleTermsWriteRules.ValidateLegResetMembers(assetClass, row);
            if (row.ValueKind != JsonValueKind.Object
                || SecurityTermReader.ReadDate([row], Spellings(fields, "resetDate")) is not { } date)
                throw Invalid("Every retained reset requires an explicit valid date.");
            var raw = ReadNumber(row, Spellings(fields, "fixingRate"));
            decimal? fixing = null;
            if (raw is { } rate)
            {
                if (!RateUnitNormalizer.Normalize(assetClass, "legs.resetSchedule.fixingRate", rate, unit).TryGetFraction(out var normalized))
                    throw Invalid("A retained reset fixing is outside its declared unit or range.");
                fixing = normalized;
            }
            rows.Add(new(date, fixing));
        }
        return rows;
    }

    private static string[] Spellings(IReadOnlyList<SecurityAssetTermField> fields, string key)
        => fields.FirstOrDefault(field => field.Key == key) is { } field ? [field.Key, .. field.Aliases] : [key];

    private static decimal? ReadNumber(JsonElement source, string[] spellings)
    {
        foreach (var key in spellings)
        {
            if (!SecurityTermReader.TryGetProperty(source, key, out var value) || value.ValueKind == JsonValueKind.Null)
                continue;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
                return number;
            throw Invalid($"'{key}' must be an explicit decimal value.");
        }
        return null;
    }

    private static InvalidOperationException Invalid(string reason) => new(reason);
}
