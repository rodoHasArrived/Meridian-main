using System.Text.Json;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Integrity;

namespace Meridian.Contracts.Accounting.Lots;

public sealed record BondIncomeTerminalCouponReceiptDto(Guid TaxLotRecordId, string ReceiptId,
    Guid JournalEntryId, string EntitlementSha256);

public sealed record BondIncomeTerminalCouponOpeningDto(Guid TaxLotRecordId, Guid AcquisitionMutationBatchId,
    Guid AcquisitionJournalEntryId, string EntitlementSha256);

/// <summary>Closing principal preserves already earned coupon rights, whether or not their cash has settled.</summary>
public static class BondIncomeTerminalCoupon
{
    public const string ModelVersion = "bond-terminal-earned-v1";

    public static bool HasCashRights(OpenLotAmortizationInstructionDto inputs)
        => inputs.BondIncome is { } income && (income.AcquisitionAccruedInterest != 0m
            || BondIncomeCouponRounding.EarnedFlows(income).Any(f => BondIncomeCouponRounding.AllocatedCash(
                inputs.ExpectedLot.TaxLotRecordId, income, f.PaymentDate, f.AccrualStart) != 0m));

    public static BondIncomeTerminalCouponOpeningDto Opening(OpenLotAmortizationInstructionDto inputs, Guid batchId, Guid journalId)
    {
        var targets = EarnedTargets(inputs);
        if (batchId == Guid.Empty || journalId == Guid.Empty || targets.RawReceivableEarned != 0m || targets.RawPayableEarned != 0m
            || targets.BookedReceivableEarned != 0m || targets.BookedPayableEarned != 0m || !HasCashRights(inputs))
            throw new ArgumentException("Terminal opening authority requires actual acquisition identities, exactly zero earned targets and retained nonzero coupon rights.");
        if (inputs.BondIncome?.PurchasedCouponTail is { } tail
            && (tail.AcquisitionMutationBatchId != batchId || tail.AcquisitionJournalEntryId != journalId))
            throw new ArgumentException("Separated purchased coupon authority must pin the exact actual acquisition receipt and journal.");
        return new(inputs.ExpectedLot.TaxLotRecordId, batchId, journalId, EntitlementHash(inputs));
    }

    public static AccountCashForecastScopeDto Scope(OpenLotFactorPaydownInstructionDto principal, OpenLotAmortizationInstructionDto inputs)
    {
        if (string.IsNullOrWhiteSpace(principal.Scope.TenantId) || string.IsNullOrWhiteSpace(principal.Scope.CompanyId)
            || inputs.BondIncome?.AccountId is not { } account || account == Guid.Empty)
            throw new ArgumentException("Terminal coupon authority requires exact authenticated tenant, company, account and book scope.");
        return new(principal.Scope.TenantId, principal.Scope.CompanyId, account.ToString("D"),
            principal.Scope.LedgerBookId.ToString("D"), principal.Currency);
    }

    public static void RequireEvidence(OpenLotFactorPaydownInstructionDto principal, IReadOnlyList<BondIncomeTerminalCouponReceiptDto> actual,
        IReadOnlyList<BondIncomeTerminalCouponOpeningDto>? openings = null)
    {
        var retained = principal.ScheduledPrincipal?.TerminalCouponReceipts ?? [];
        if (retained.Count != actual.Count || retained.Select(r => r.TaxLotRecordId).Distinct().Count() != retained.Count
            || !retained.OrderBy(r => r.TaxLotRecordId).SequenceEqual(actual.OrderBy(r => r.TaxLotRecordId)))
            throw new ArgumentException("Terminal principal coupon-earned receipt authority is absent or stale; prepare again after the final earned target is posted.");
        var expectedOpenings = principal.ScheduledPrincipal?.TerminalCouponOpenings ?? [];
        var actualOpenings = openings ?? [];
        if (expectedOpenings.Count != actualOpenings.Count
            || expectedOpenings.Select(r => r.TaxLotRecordId).Distinct().Count() != expectedOpenings.Count
            || expectedOpenings.Any(o => retained.Any(r => r.TaxLotRecordId == o.TaxLotRecordId))
            || !expectedOpenings.OrderBy(r => r.TaxLotRecordId).SequenceEqual(actualOpenings.OrderBy(r => r.TaxLotRecordId)))
            throw new ArgumentException("Terminal principal acquisition-opening coupon authority is absent or stale; reconcile the actual purchased-interest opening before closing.");
    }

    public static bool HasCouponEconomics(OpenLotAmortizationInstructionDto inputs)
        => inputs.BondIncome is { } income && (income.AcquisitionAccruedInterest != 0m
            || income.CurrentAccruedInterest != 0m || income.CurrentProjection.CashFlows.Any(f => f.Interest != 0m));

    public static string EntitlementHash(BondCouponRecognitionInstructionDto instruction) => EntitlementHash(instruction.IncomeInstruction);

    public static string EntitlementHash(OpenLotAmortizationInstructionDto inputs)
    {
        var income = inputs.BondIncome ?? throw new ArgumentException("Terminal coupon rights require adopted income inputs.");
        var targets = EarnedTargets(inputs);
        var coupons = BondIncomeCouponRounding.EarnedFlows(income)
            .GroupBy(f => (f.PaymentDate, f.AccrualStart, f.AccrualEnd)).OrderBy(g => g.Key.PaymentDate).ThenBy(g => g.Key.AccrualStart)
            .Select(g => new { g.Key.PaymentDate, g.Key.AccrualStart, g.Key.AccrualEnd,
                Cash = BondIncomeCouponRounding.AllocatedCash(inputs.ExpectedLot.TaxLotRecordId, income, g.Key.PaymentDate, g.Key.AccrualStart) }).ToArray();
        return Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(new
        {
            ModelVersion, inputs.ExpectedLot.TaxLotRecordId, income.LedgerBookId, income.AccountId,
            income.AccountingPolicyId, income.AccountingPolicyVersion, income.CurrentProjection.Currency,
            income.AcquisitionAccruedInterest, targets.RawReceivableEarned, targets.RawPayableEarned,
            targets.BookedReceivableEarned, targets.BookedPayableEarned, Coupons = coupons
        }));
    }

    public static BondIncomeTerminalCouponReceiptDto? Reconcile(OpenLotAmortizationInstructionDto inputs,
        AccountCashForecastScopeDto scope, BondCouponRecognitionReceiptDto? receipt)
    {
        var income = inputs.BondIncome ?? throw new ArgumentException("Terminal coupon rights require adopted income inputs.");
        if (income.TerminalEntitlement is null && income.CurrentProjection.CashFlows.Any(f => f.PaymentDate > inputs.AsOfDate && f.Interest != 0m))
            throw new ArgumentException("Terminal principal with future coupon rights requires retained final entitlement authority before closing; future or unfixed rights cannot be assumed zero.");
        var targets = EarnedTargets(inputs);
        if (receipt is null)
        {
            if (targets.BookedReceivableEarned == 0m && targets.BookedPayableEarned == 0m && !HasCashRights(inputs)) return null;
            throw new ArgumentException("Recognize and post the final earned coupon target before terminal principal; coupon cash may remain unpaid.");
        }
        var retained = receipt.Instruction;
        var retainedIncome = retained.IncomeInstruction.BondIncome;
        if (receipt.JournalEntryId == Guid.Empty || receipt.ReceiptId != BondCouponRecognitionCalculator.Fingerprint(retained)
            || retained.Scope != scope || retained.IncomeInstruction.ExpectedLot.TaxLotRecordId != inputs.ExpectedLot.TaxLotRecordId
            || retainedIncome is null || retainedIncome.LedgerBookId != income.LedgerBookId || retainedIncome.AccountId != income.AccountId
            || retainedIncome.AccountingPolicyId != income.AccountingPolicyId || retainedIncome.AccountingPolicyVersion != income.AccountingPolicyVersion
            || retained.BookedTargets?.PolicyVersion != BondIncomeBookedPrecision.PolicyVersion
            || retained.BookedTargets.RawReceivableEarned != targets.RawReceivableEarned
            || retained.BookedTargets.RawPayableEarned != targets.RawPayableEarned
            || retained.CumulativeReceivableEarned != targets.BookedReceivableEarned
            || retained.CumulativePayableEarned != targets.BookedPayableEarned
            || EntitlementHash(retained) != EntitlementHash(inputs))
            throw new ArgumentException("The latest posted coupon target or entitlement differs from terminal principal; reconcile the earned target before closing, without requiring cash settlement.");
        return new(inputs.ExpectedLot.TaxLotRecordId, receipt.ReceiptId, receipt.JournalEntryId, EntitlementHash(inputs));
    }

    public static BondCouponBookedTargetsDto EarnedTargets(OpenLotAmortizationInstructionDto inputs)
    {
        var income = inputs.BondIncome ?? throw new ArgumentException("Terminal coupon rights require adopted income inputs.");
        var sides = BondIncomeCouponRounding.EarnedSides(inputs.ExpectedLot.TaxLotRecordId, income,
            inputs.ExpectedLot.OpenQuantity / inputs.ExpectedLot.OriginalQuantity);
        var receivable = sides.Receivable;
        var payable = sides.Payable;
        var bookedReceivable = BondIncomeBookedPrecision.RoundTarget(receivable);
        var bookedPayable = BondIncomeBookedPrecision.RoundTarget(payable);
        return new(BondIncomeBookedPrecision.PolicyVersion, receivable, bookedReceivable, receivable - bookedReceivable,
            payable, bookedPayable, payable - bookedPayable, 0m, 0m);
    }
}
