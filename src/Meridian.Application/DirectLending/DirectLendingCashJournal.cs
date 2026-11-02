using System.Globalization;
using System.Text.Json;
using Meridian.Contracts.DirectLending;
using Meridian.Ledger;

namespace Meridian.Application.DirectLending;

/// <summary>
/// Decodes retained cash decisions for both journal projections and event replay.
/// Historical receipts keep their original clearing treatment; new decisions must
/// account for every unit of cash without recognizing income on receipt.
/// </summary>
internal static class DirectLendingCashJournal
{
    internal const string CashAccountingVersion = DirectLendingPaymentResolver.CashAccountingVersion;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal sealed record Receipt(
        decimal Amount,
        MixedPaymentResolutionDto? Resolution,
        int? TermsVersion,
        string? TermsHash,
        CurrencyCode? Currency,
        DateOnly? EffectiveDate,
        Guid? CashTransactionId,
        decimal AutomaticPrepaymentPenaltyAmount = 0m,
        Guid? PrepaymentPenaltyEventId = null)
    {
        internal bool IsVersioned => Resolution?.CashAccountingVersion is not null;
    }

    internal sealed record Line(string AccountCode, LedgerAccountType AccountType, decimal Debit, decimal Credit);

    internal sealed record AccrualRelease(
        decimal AppliedAmount,
        IReadOnlyList<PrepaidInterestReleaseDto> Releases,
        CurrencyCode? Currency,
        DateOnly? AccrualDate);

    internal sealed record AutomaticPenalty(
        decimal Amount, decimal ExcessPrincipal, decimal Rate, int TermsVersion, string TermsHash,
        CurrencyCode Currency, DateOnly EffectiveDate, Guid CashTransactionId, Guid ReceiptEventId);

    internal static Receipt Read(JsonElement payload)
    {
        RequireUniqueProperties(payload);
        var amount = RequiredDecimal(payload, "amount", "Amount");
        if (amount <= 0m)
            throw Invalid("receipt amount must be positive");

        var hasVersionedEnvelope =
            TryProperty(payload, "termsVersion", "TermsVersion", out _) ||
            TryProperty(payload, "termsHash", "TermsHash", out _) ||
            TryProperty(payload, "currency", "Currency", out _) ||
            TryProperty(payload, "cashTransactionId", "CashTransactionId", out _) ||
            TryProperty(payload, "cashAccountingVersion", "CashAccountingVersion", out _);
        if (!TryProperty(payload, "resolution", "Resolution", out var resolutionElement))
        {
            if (hasVersionedEnvelope)
                throw Invalid("versioned cash evidence requires its resolution");
            return new Receipt(amount, null, null, null, null, null, null);
        }

        if (resolutionElement.ValueKind != JsonValueKind.Object)
            throw Invalid("resolution must be an object");
        RequireUniqueProperties(resolutionElement);

        var resolution = resolutionElement.Deserialize<MixedPaymentResolutionDto>(JsonOptions)
            ?? throw Invalid("resolution is missing");
        if (resolution.Breakdown is null)
            throw Invalid("payment breakdown is missing");

        if (resolution.CashAccountingVersion is null)
        {
            if (resolution.ExcessToPrepaidInterest != 0m || resolution.ExcessToPrincipal != 0m ||
                resolution.ExcessRuleApplied is not null || hasVersionedEnvelope)
                throw Invalid("versioned cash evidence requires an explicit cash accounting version");
            return new Receipt(amount, resolution, null, null, null, null, null);
        }

        if (!string.Equals(resolution.CashAccountingVersion, CashAccountingVersion, StringComparison.Ordinal))
            throw Invalid($"unsupported cash accounting version '{resolution.CashAccountingVersion}'");
        if (!string.Equals(resolution.RuleVersion, CashAccountingVersion, StringComparison.Ordinal))
            throw Invalid("allocation rule version does not match the cash accounting version");
        if (TryProperty(payload, "cashAccountingVersion", "CashAccountingVersion", out var envelopeVersion) &&
            (envelopeVersion.ValueKind != JsonValueKind.String || envelopeVersion.GetString() != CashAccountingVersion))
            throw Invalid("envelope cash accounting version does not match its retained resolution");

        // Required numeric properties must not silently deserialize to zero in financial evidence.
        // The additive prepaid amount is omitted when zero by its versioned DTO contract.
        if (!TryProperty(resolutionElement, "breakdown", "Breakdown", out var breakdownElement) ||
            breakdownElement.ValueKind != JsonValueKind.Object)
            throw Invalid("payment breakdown is missing");
        RequireUniqueProperties(breakdownElement);
        var amounts = new[]
        {
            RequiredDecimal(breakdownElement, "toInterest", "ToInterest"),
            RequiredDecimal(breakdownElement, "toCommitmentFee", "ToCommitmentFee"),
            RequiredDecimal(breakdownElement, "toFees", "ToFees"),
            RequiredDecimal(breakdownElement, "toPenalty", "ToPenalty"),
            RequiredDecimal(breakdownElement, "toPrincipal", "ToPrincipal"),
            RequiredDecimal(resolutionElement, "unappliedAmount", "UnappliedAmount"),
            TryProperty(resolutionElement, "excessToPrincipal", "ExcessToPrincipal", out _)
                ? RequiredDecimal(resolutionElement, "excessToPrincipal", "ExcessToPrincipal")
                : 0m,
            TryProperty(resolutionElement, "excessToPrepaidInterest", "ExcessToPrepaidInterest", out _)
                ? RequiredDecimal(resolutionElement, "excessToPrepaidInterest", "ExcessToPrepaidInterest")
                : 0m
        };
        if (!DirectLendingPaymentResolver.IsLedgerAmount(amount) ||
            amounts.Any(static value => !DirectLendingPaymentResolver.IsLedgerAmount(value)))
            throw Invalid("cash amounts exceed the authoritative ledger precision");
        if (amounts.Any(static value => value < 0m) || amounts.Sum() != amount)
            throw Invalid("allocations must be nonnegative and reconcile exactly to the cash receipt");

        if (!TryProperty(payload, "termsVersion", "TermsVersion", out var termsVersionElement) ||
            !termsVersionElement.TryGetInt32(out var termsVersion) || termsVersion <= 0)
            throw Invalid("terms version is missing or invalid");
        if (!TryProperty(payload, "termsHash", "TermsHash", out var termsHashElement) ||
            termsHashElement.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(termsHashElement.GetString()))
            throw Invalid("terms hash is missing");
        if (!TryProperty(payload, "cashTransactionId", "CashTransactionId", out var cashIdElement) ||
            cashIdElement.ValueKind != JsonValueKind.String || !cashIdElement.TryGetGuid(out var cashId) || cashId == Guid.Empty)
            throw Invalid("cash transaction identity is missing or invalid");
        if (!TryProperty(payload, "currency", "Currency", out var currencyElement))
            throw Invalid("receipt currency is missing");
        CurrencyCode currency;
        if (currencyElement.ValueKind == JsonValueKind.Number && currencyElement.TryGetByte(out var currencyNumber))
            currency = (CurrencyCode)currencyNumber;
        else if (currencyElement.ValueKind == JsonValueKind.String &&
                 Enum.TryParse(currencyElement.GetString(), ignoreCase: false, out CurrencyCode parsedCurrency))
            currency = parsedCurrency;
        else
            throw Invalid("receipt currency is invalid");
        if (!Enum.IsDefined(currency) || currency == CurrencyCode.Other)
            throw Invalid("receipt currency must identify a supported denomination");
        if (!TryProperty(payload, "effectiveDate", "EffectiveDate", out var effectiveDateElement) ||
            effectiveDateElement.ValueKind != JsonValueKind.String ||
            !DateOnly.TryParseExact(effectiveDateElement.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var effectiveDate))
            throw Invalid("payment effective date is missing or invalid");

        var penaltyAmount = RequiredDecimal(payload, "automaticPrepaymentPenaltyAmount", "AutomaticPrepaymentPenaltyAmount");
        Guid? penaltyEventId = null;
        if (!TryProperty(payload, "prepaymentPenaltyEventId", "PrepaymentPenaltyEventId", out var penaltyIdElement))
            throw Invalid("automatic penalty event identity evidence is missing");
        if (penaltyIdElement.ValueKind != JsonValueKind.Null)
        {
            if (penaltyIdElement.ValueKind != JsonValueKind.String ||
                !penaltyIdElement.TryGetGuid(out var retainedPenaltyId) || retainedPenaltyId == Guid.Empty)
                throw Invalid("automatic penalty event identity is invalid");
            penaltyEventId = retainedPenaltyId;
        }
        if (penaltyAmount < 0m || !DirectLendingPaymentResolver.IsLedgerAmount(penaltyAmount) ||
            (penaltyAmount > 0m) != penaltyEventId.HasValue)
            throw Invalid("automatic penalty amount and companion event identity are inconsistent");

        return new Receipt(amount, resolution, termsVersion, termsHashElement.GetString(), currency, effectiveDate,
            cashId, penaltyAmount, penaltyEventId);
    }

    internal static AccrualRelease ReadAccrualRelease(JsonElement payload, decimal cashInterest)
    {
        RequireUniqueProperties(payload);
        var applied = TryProperty(payload, "prepaidInterestConsumed", "PrepaidInterestConsumed", out _)
            ? RequiredDecimal(payload, "prepaidInterestConsumed", "PrepaidInterestConsumed")
            : 0m;
        var releases = new List<PrepaidInterestReleaseDto>();
        if (TryProperty(payload, "prepaidInterestReleases", "PrepaidInterestReleases", out var releaseElements) &&
            releaseElements.ValueKind != JsonValueKind.Null)
        {
            if (releaseElements.ValueKind != JsonValueKind.Array)
                throw Invalid("prepaid-interest release evidence must be an array");
            foreach (var element in releaseElements.EnumerateArray())
            {
                RequireUniqueProperties(element);
                if (!TryProperty(element, "cashTransactionId", "CashTransactionId", out var cashIdElement) ||
                    cashIdElement.ValueKind != JsonValueKind.String || !cashIdElement.TryGetGuid(out var cashId) || cashId == Guid.Empty)
                    throw Invalid("prepaid-interest release receipt identity is missing or invalid");
                var amount = RequiredDecimal(element, "amount", "Amount");
                if (amount <= 0m || !DirectLendingPaymentResolver.IsLedgerAmount(amount))
                    throw Invalid("prepaid-interest release amounts must be positive and exactly representable by the ledger");
                releases.Add(new PrepaidInterestReleaseDto(cashId, amount));
            }
        }

        var hasVersion = TryProperty(payload, "cashAccountingVersion", "CashAccountingVersion", out var version) &&
            version.ValueKind != JsonValueKind.Null;
        if (hasVersion && (version.ValueKind != JsonValueKind.String || version.GetString() != CashAccountingVersion))
            throw Invalid("unsupported prepaid-interest release accounting version");
        if (applied == 0m && releases.Count == 0)
            return new AccrualRelease(0m, [], null, null);
        if (!hasVersion || applied <= 0m || applied > cashInterest || !DirectLendingPaymentResolver.IsLedgerAmount(applied) ||
            !DirectLendingPaymentResolver.IsLedgerAmount(cashInterest) || releases.Sum(item => item.Amount) != applied ||
            releases.Select(item => item.CashTransactionId).Distinct().Count() != releases.Count)
            throw Invalid("prepaid-interest release must have known version, distinct receipts and exact support from earned cash interest");
        if (!TryProperty(payload, "currency", "Currency", out var currencyElement))
            throw Invalid("prepaid-interest release currency is missing");
        CurrencyCode currency;
        if (currencyElement.ValueKind == JsonValueKind.Number && currencyElement.TryGetByte(out var currencyNumber))
            currency = (CurrencyCode)currencyNumber;
        else if (currencyElement.ValueKind == JsonValueKind.String &&
                 Enum.TryParse(currencyElement.GetString(), ignoreCase: false, out CurrencyCode parsedCurrency))
            currency = parsedCurrency;
        else
            throw Invalid("prepaid-interest release currency is invalid");
        if (!Enum.IsDefined(currency) || currency == CurrencyCode.Other)
            throw Invalid("prepaid-interest release currency must identify a supported denomination");
        if (!TryProperty(payload, "accrualDate", "AccrualDate", out var dateElement) ||
            dateElement.ValueKind != JsonValueKind.String ||
            !DateOnly.TryParseExact(dateElement.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var accrualDate))
            throw Invalid("prepaid-interest release accrual date is missing or invalid");

        return new AccrualRelease(applied, releases, currency, accrualDate);
    }

    internal static void ValidateAccrualDate(AccrualRelease release, DateOnly accrualDate)
    {
        if (release.AppliedAmount > 0m && release.AccrualDate != accrualDate)
            throw Invalid("prepaid-interest release date does not match its retained event date");
    }

    internal static void ValidateTerms(Receipt receipt, LoanContractDetailDto contract)
    {
        if (!receipt.IsVersioned)
            return;

        var terms = contract.TermsVersions.SingleOrDefault(item => item.VersionNumber == receipt.TermsVersion);
        if (terms is null || !string.Equals(terms.TermsHash, receipt.TermsHash, StringComparison.Ordinal) ||
            terms.Terms.BaseCurrency != receipt.Currency)
            throw Invalid("terms version, hash or currency does not match the retained contract");

        var retainedTerms = terms.Terms;
        if (DirectLendingPaymentResolver.ValidatePolicy(retainedTerms) is { } policyError)
            throw Invalid(policyError);
        var resolution = receipt.Resolution!;
        if (resolution.ExcessRuleApplied != retainedTerms.ExcessPaymentApplication)
            throw Invalid("recorded excess routing rule does not match the retained contractual terms");
        if (resolution.ExcessToPrepaidInterest > 0m &&
            retainedTerms.EffectiveExcessPaymentApplication != ExcessPaymentApplication.PrepaidInterest)
            throw Invalid("prepaid interest requires the retained contractual routing rule");
        if (retainedTerms.EffectiveExcessPaymentApplication == ExcessPaymentApplication.PrepaidInterest &&
            resolution.UnappliedAmount != 0m)
            throw Invalid("the retained uncapped prepaid-interest rule cannot leave routed excess unapplied");
        if (resolution.ExcessToPrincipal > 0m &&
            (retainedTerms.EffectiveExcessPaymentApplication != ExcessPaymentApplication.PrincipalPrepayment ||
             !retainedTerms.PrepaymentAllowed))
            throw Invalid("recorded excess principal prepayment is not permitted by the retained contractual instructions");
        if (receipt.AutomaticPrepaymentPenaltyAmount !=
            DirectLendingPaymentResolver.CalculateExcessPrepaymentPenalty(retainedTerms, resolution))
            throw Invalid("automatic penalty amount does not match the retained excess principal and terms");
    }

    internal static AutomaticPenalty? ReadAutomaticPenalty(
        JsonElement payload, LoanContractDetailDto contract, DateOnly effectiveDate)
    {
        RequireUniqueProperties(payload);
        if (!TryProperty(payload, "assessmentKind", "AssessmentKind", out var kind))
        {
            if (TryProperty(payload, "receiptEventId", "ReceiptEventId", out _) ||
                TryProperty(payload, "excessToPrincipal", "ExcessToPrincipal", out _))
                throw Invalid("automatic penalty assessment kind is missing");
            return null;
        }
        if (kind.ValueKind != JsonValueKind.String || kind.GetString() != "ExcessPrincipalPrepayment" ||
            !IsVersionedAssessment(payload))
            throw Invalid("automatic penalty assessment kind or version is invalid");

        var amount = RequiredDecimal(payload, "penaltyAmount", "PenaltyAmount");
        var principal = RequiredDecimal(payload, "excessToPrincipal", "ExcessToPrincipal");
        var rate = RequiredDecimal(payload, "prepaymentPenaltyRate", "PrepaymentPenaltyRate");
        if (amount <= 0m || principal <= 0m || rate <= 0m ||
            !DirectLendingPaymentResolver.IsLedgerAmount(amount) || !DirectLendingPaymentResolver.IsLedgerAmount(principal))
            throw Invalid("automatic penalty amount, rate or excess principal basis is invalid");
        var loanId = RequiredGuid(payload, "loanId", "LoanId");
        var cashId = RequiredGuid(payload, "cashTransactionId", "CashTransactionId");
        var receiptEventId = RequiredGuid(payload, "receiptEventId", "ReceiptEventId");
        if (loanId != contract.LoanId ||
            !TryProperty(payload, "termsVersion", "TermsVersion", out var versionElement) ||
            !versionElement.TryGetInt32(out var termsVersion) || termsVersion <= 0 ||
            !TryProperty(payload, "termsHash", "TermsHash", out var hashElement) ||
            hashElement.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(hashElement.GetString()))
            throw Invalid("automatic penalty loan or retained terms identity is invalid");
        var termsHash = hashElement.GetString()!;
        var terms = contract.TermsVersions.SingleOrDefault(item => item.VersionNumber == termsVersion);
        if (terms is null || terms.TermsHash != termsHash || !terms.Terms.PrepaymentAllowed ||
            terms.Terms.EffectiveExcessPaymentApplication != ExcessPaymentApplication.PrincipalPrepayment ||
            terms.Terms.PrepaymentPenaltyRate != rate ||
            decimal.Round(principal * rate, 2, MidpointRounding.AwayFromZero) != amount)
            throw Invalid("automatic penalty is unsupported by its retained terms and excess principal basis");
        if (!TryProperty(payload, "effectiveDate", "EffectiveDate", out var dateElement) ||
            dateElement.ValueKind != JsonValueKind.String ||
            !DateOnly.TryParseExact(dateElement.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var recordedDate) || recordedDate != effectiveDate)
            throw Invalid("automatic penalty date does not match its retained event date");
        var currency = RequiredCurrency(payload);
        if (currency != terms.Terms.BaseCurrency)
            throw Invalid("automatic penalty currency does not match its retained terms");

        return new AutomaticPenalty(amount, principal, rate, termsVersion, termsHash, currency,
            recordedDate, cashId, receiptEventId);
    }

    internal static void ValidatePenaltyReceipt(
        AutomaticPenalty penalty, Receipt receipt, Guid receiptEventId, Guid penaltyEventId)
    {
        if (!receipt.IsVersioned || penalty.ReceiptEventId != receiptEventId ||
            receipt.PrepaymentPenaltyEventId != penaltyEventId ||
            receipt.AutomaticPrepaymentPenaltyAmount != penalty.Amount ||
            receipt.Resolution!.ExcessToPrincipal != penalty.ExcessPrincipal ||
            receipt.CashTransactionId != penalty.CashTransactionId || receipt.EffectiveDate != penalty.EffectiveDate ||
            receipt.Currency != penalty.Currency || receipt.TermsVersion != penalty.TermsVersion ||
            receipt.TermsHash != penalty.TermsHash)
            throw Invalid("automatic penalty does not match its retained receipt and companion identity");
    }

    internal static void ValidateEffectiveDate(Receipt receipt, DateOnly effectiveDate)
    {
        if (receipt.IsVersioned && receipt.EffectiveDate != effectiveDate)
            throw Invalid("payment effective date does not match its retained event date");
    }

    internal static bool IsVersionedAssessment(JsonElement payload)
    {
        RequireUniqueProperties(payload);
        if (!TryProperty(payload, "cashAccountingVersion", "CashAccountingVersion", out var version))
            return false;
        if (version.ValueKind != JsonValueKind.String || version.GetString() != CashAccountingVersion)
            throw Invalid("unsupported fee or penalty assessment accounting version");
        return true;
    }

    /// <summary>
    /// Legacy fee/penalty assessments created operational drafts without an authoritative
    /// receivable debit. Require explicit remediation before a new receipt clears them.
    /// Cash may still be held while that evidence is reviewed.
    /// </summary>
    internal static string? ValidateAssessmentEvidence(
        IReadOnlyList<LoanEventLineageDto> history, MixedPaymentResolutionDto resolution)
    {
        if (resolution.Breakdown.ToFees <= 0m && resolution.Breakdown.ToPenalty <= 0m)
            return null;
        foreach (var entry in history)
        {
            var isFee = entry.EventType == "loan.fee-assessed" && resolution.Breakdown.ToFees > 0m;
            var isPenalty = entry.EventType == "loan.prepayment-penalty-charged" && resolution.Breakdown.ToPenalty > 0m;
            if (!isFee && !isPenalty)
                continue;
            try
            {
                using var payload = JsonDocument.Parse(entry.PayloadJson);
                RequireUniqueProperties(payload.RootElement);
                var amount = isFee
                    ? RequiredDecimal(payload.RootElement, "amount", "Amount")
                    : RequiredDecimal(payload.RootElement, "penaltyAmount", "PenaltyAmount");
                if (amount == 0m)
                    continue;
                if (amount < 0m ||
                    !TryProperty(payload.RootElement, "cashAccountingVersion", "CashAccountingVersion", out var version) ||
                    version.ValueKind != JsonValueKind.String || version.GetString() != CashAccountingVersion)
                    return $"Retained legacy {(isFee ? "fee" : "prepayment-penalty")} assessments require reviewed receivable reconciliation before collection; hold the cash unapplied pending remediation.";
            }
            catch (Exception error) when (error is JsonException or InvalidOperationException)
            {
                return "Retained fee or penalty assessment evidence is unreadable; reconcile the receivable before collection.";
            }
        }
        return null;
    }

    internal static IReadOnlyList<Line> BuildLines(Receipt receipt)
    {
        var lines = new List<Line> { new("Cash", LedgerAccountType.Asset, receipt.Amount, 0m) };
        if (!receipt.IsVersioned)
        {
            lines.Add(new("LoanAndAccrualsClearing", LedgerAccountType.Asset, 0m, receipt.Amount));
            return lines;
        }

        var resolution = receipt.Resolution!;
        Add("AccruedInterestReceivable", LedgerAccountType.Asset, resolution.Breakdown.ToInterest);
        Add("CommitmentFeeReceivable", LedgerAccountType.Asset, resolution.Breakdown.ToCommitmentFee);
        Add("FeeReceivable", LedgerAccountType.Asset, resolution.Breakdown.ToFees);
        Add("PenaltyReceivable", LedgerAccountType.Asset, resolution.Breakdown.ToPenalty);
        Add("LoanPrincipal", LedgerAccountType.Asset, resolution.Breakdown.ToPrincipal + resolution.ExcessToPrincipal);
        Add("UnappliedCashLiability", LedgerAccountType.Liability, resolution.UnappliedAmount);
        Add("PrepaidInterestLiability", LedgerAccountType.Liability, resolution.ExcessToPrepaidInterest);
        return lines;

        void Add(string account, LedgerAccountType type, decimal credit)
        {
            if (credit > 0m)
                lines.Add(new Line(account, type, 0m, credit));
        }
    }

    private static decimal RequiredDecimal(JsonElement root, string camelCase, string pascalCase)
    {
        if (TryProperty(root, camelCase, pascalCase, out var value) && value.ValueKind == JsonValueKind.Number &&
            value.TryGetDecimal(out var amount))
            return amount;
        throw Invalid($"numeric '{camelCase}' is missing or invalid");
    }

    internal static decimal ReadAssessmentAmount(JsonElement payload, bool penalty)
    {
        var amount = penalty
            ? RequiredDecimal(payload, "penaltyAmount", "PenaltyAmount")
            : RequiredDecimal(payload, "amount", "Amount");
        if (amount < 0m || !DirectLendingPaymentResolver.IsLedgerAmount(amount))
            throw Invalid("assessment amount must be nonnegative and exactly representable by the ledger");
        return amount;
    }

    private static Guid RequiredGuid(JsonElement payload, string camelCase, string pascalCase)
    {
        if (TryProperty(payload, camelCase, pascalCase, out var element) &&
            element.ValueKind == JsonValueKind.String && element.TryGetGuid(out var value) && value != Guid.Empty)
            return value;
        throw Invalid($"identity '{camelCase}' is missing or invalid");
    }

    private static CurrencyCode RequiredCurrency(JsonElement payload)
    {
        if (!TryProperty(payload, "currency", "Currency", out var element))
            throw Invalid("currency is missing");
        CurrencyCode currency;
        if (element.ValueKind == JsonValueKind.Number && element.TryGetByte(out var number))
            currency = (CurrencyCode)number;
        else if (element.ValueKind == JsonValueKind.String &&
                 Enum.TryParse(element.GetString(), ignoreCase: false, out CurrencyCode parsed))
            currency = parsed;
        else
            throw Invalid("currency is invalid");
        if (!Enum.IsDefined(currency) || currency == CurrencyCode.Other)
            throw Invalid("currency must identify a supported denomination");
        return currency;
    }

    private static bool TryProperty(JsonElement root, string camelCase, string pascalCase, out JsonElement value)
        => root.TryGetProperty(camelCase, out value) || root.TryGetProperty(pascalCase, out value);

    private static void RequireUniqueProperties(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw Invalid("cash evidence must be an object");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (value.EnumerateObject().Any(property => !names.Add(property.Name)))
            throw Invalid("duplicate property names are not supported");
    }

    private static InvalidOperationException Invalid(string reason)
        => new($"Retained direct-lending cash evidence is invalid: {reason}.");
}
