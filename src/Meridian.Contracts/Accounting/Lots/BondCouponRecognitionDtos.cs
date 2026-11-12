using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Integrity;

namespace Meridian.Contracts.Accounting.Lots;

public sealed record BondCouponCashAllocationDto(string AllocationId, string PaymentVersionId,
    decimal NetAmount, decimal GrossAmount, decimal WithholdingTax = 0m, decimal Fees = 0m,
    AccountCashProvenanceDto? DeductionEvidence = null);

public sealed record BondCouponRecognitionRequest(string RunId, Guid TaxLotRecordId, Guid PeriodId,
    DateOnly ThroughDate, DateTimeOffset KnownAt, IReadOnlyList<BondCouponCashAllocationDto> CashAllocations,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ReversesReceiptId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CorrectionRationale = null);

public sealed record BondCouponBookAccountsDto(string Receivable, string Payable, string Income,
    string Cash, string Tax, string Fees);

public sealed record BondCouponRecognitionAccountDto(string Path, string LedgerName, string AccountType);

public sealed record BondCouponRecognizedCashDto(BondCouponCashAllocationDto Allocation,
    decimal ObservedPaymentAmount, AccountCashFlowDto ObservedPayment,
    AccountCashPaymentAllocationDto RetainedAllocation);

public readonly record struct BondCouponAllocationConsumptionDto(decimal NetAmount, decimal GrossAmount,
    decimal WithholdingTax, decimal Fees);

public sealed record BondCouponObligationConsumptionDto(string ExpectedFlowId, decimal GrossAmount);

/// <summary>Raw model targets and their adopted ledger targets; residual is raw minus booked.</summary>
public sealed record BondCouponBookedTargetsDto(string PolicyVersion,
    decimal RawReceivableEarned, decimal BookedReceivableEarned, decimal ReceivableRoundingResidual,
    decimal RawPayableEarned, decimal BookedPayableEarned, decimal PayableRoundingResidual,
    decimal PreviousRawReceivableEarned, decimal PreviousRawPayableEarned);

/// <summary>Cash clearing uses an immutable earned coupon after governed terminal principal closes its lot.</summary>
public sealed record BondCouponTerminalCashAuthorityDto(string ModelVersion, string Mode,
    Guid PrincipalMutationBatchId, Guid PrincipalJournalEntryId, DateOnly PrincipalEffectiveDate,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] BondCouponRecognitionReceiptDto? EarnedReceipt)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BondIncomeTerminalCouponOpeningDto? Opening { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OpenLotAmortizationInstructionDto? OpeningIncomeInstruction { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BondCouponBookedTargetsDto? OpeningTargets { get; init; }

    [JsonIgnore]
    public OpenLotAmortizationInstructionDto FrozenIncomeInstruction => EarnedReceipt?.Instruction.IncomeInstruction
        ?? OpeningIncomeInstruction ?? throw new InvalidOperationException("Frozen terminal income authority is unavailable.");
    [JsonIgnore]
    public BondCouponBookedTargetsDto FrozenTargets => EarnedReceipt?.Instruction.BookedTargets
        ?? OpeningTargets ?? throw new InvalidOperationException("Frozen terminal earned targets are unavailable.");
}

public interface IBondCouponRecognitionEvidenceVerifier
{
    Task VerifyAsync(AccountCashForecastScopeDto scope, IReadOnlyList<BondCouponRecognizedCashDto> cash,
        CancellationToken ct = default);
}

public sealed record BondCouponRecognitionInstructionDto(string ModelVersion,
    AccountCashForecastScopeDto Scope, BondCouponRecognitionRequest Request,
    OpenLotAmortizationInstructionDto IncomeInstruction, Guid EventId, string? PreviousReceiptId,
    decimal CumulativeEarned, decimal PreviousEarned, decimal PriorGrossSettled,
    IReadOnlyList<BondCouponRecognizedCashDto> Cash,
    IReadOnlyList<BondCouponRecognitionAccountDto> Accounts,
    IReadOnlyList<ProjectedAccountingEffectLineDto> Lines)
{
    public decimal CumulativeReceivableEarned { get; init; }
    public decimal CumulativePayableEarned { get; init; }
    public decimal PreviousReceivableEarned { get; init; }
    public decimal PreviousPayableEarned { get; init; }
    public decimal PriorReceivableSettled { get; init; }
    public decimal PriorPayableSettled { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BondCouponBookAccountsDto? AccountMappings { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BondCouponBookedTargetsDto? BookedTargets { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BondCouponTerminalCashAuthorityDto? TerminalCashAuthority { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BondCouponRecognitionReceiptDto? ReversalTarget { get; init; }
}

public sealed record BondCouponRecognitionReceiptDto(string ReceiptId, Guid JournalEntryId,
    DateTimeOffset PostedAtUtc, BondCouponRecognitionInstructionDto Instruction);

public sealed record BondCouponRecognitionPreviewDto(string Fingerprint, decimal EarnedDelta,
    decimal SettledGross, decimal RemainingSignedBalance, bool HasChanges,
    BondCouponRecognitionInstructionDto Instruction)
{
    public decimal RemainingReceivable { get; init; }
    public decimal RemainingPayable { get; init; }
}

public sealed record BondCouponRecognitionPreparationDto(BondCouponRecognitionPreviewDto Preview,
    ProjectAssetAccountingEventRequestDto Projection);

public sealed record BondCouponRecognitionPrepareRequestDto(BondCouponRecognitionRequest Request,
    string ExpectedFingerprint);

public interface IBondCouponRecognitionStore
{
    Task<BondCouponRecognitionReceiptDto?> GetLatestCouponRecognitionAsync(AccountCashForecastScopeDto scope,
        Guid taxLotRecordId, CancellationToken ct = default);
    Task<BondCouponRecognitionReceiptDto?> GetCouponRecognitionRunAsync(AccountCashForecastScopeDto scope,
        Guid taxLotRecordId, string runId, CancellationToken ct = default);
    Task<decimal> GetConsumedCouponCashAsync(AccountCashForecastScopeDto scope, string paymentFlowId,
        CancellationToken ct = default);
    Task<decimal> GetConsumedCouponObligationAsync(AccountCashForecastScopeDto scope, Guid taxLotRecordId,
        string expectedFlowId, CancellationToken ct = default);
    Task<BondCouponAllocationConsumptionDto> GetConsumedCouponAllocationAsync(AccountCashForecastScopeDto scope,
        string allocationId, CancellationToken ct = default);
    Task<IReadOnlyList<BondCouponObligationConsumptionDto>> GetConsumedCouponObligationsAsync(AccountCashForecastScopeDto scope,
        Guid taxLotRecordId, CancellationToken ct = default);
    Task<BondCouponRecognitionReceiptDto?> GetCouponRecognitionReceiptAsync(AccountCashForecastScopeDto scope,
        Guid taxLotRecordId, string receiptId, CancellationToken ct = default);
}

public interface IBondCouponRecognitionService
{
    Task VerifyTerminalOpeningAsync(AccountCashForecastScopeDto scope, OpenLotAmortizationInstructionDto inputs,
        BondIncomeTerminalCouponOpeningDto opening, Guid periodId, CancellationToken ct = default);
    Task<BondCouponRecognitionPreviewDto> PreviewAsync(AccountCashForecastScopeDto scope,
        BondCouponRecognitionRequest request, CancellationToken ct = default);
    Task<BondCouponRecognitionPreparationDto> PrepareAsync(AccountCashForecastScopeDto scope,
        BondCouponRecognitionRequest request, string expectedFingerprint, string actor,
        DateTimeOffset projectedAtUtc, CancellationToken ct = default);
    Task VerifyAsync(BondCouponRecognitionInstructionDto instruction, CancellationToken ct = default);
    Task VerifyPreparedSourceAsync(BondCouponRecognitionInstructionDto instruction,
        RetainedEvidenceIdentityDto source, CancellationToken ct = default);
}

public static class BondCouponRecognitionCalculator
{
    public const string ModelVersion = "bond-coupon-recognition-v1";

    public static bool IsBoundedShare(BondCouponCashAllocationDto share, AccountCashPaymentAllocationDto source)
        => share.AllocationId == source.AllocationId && share.PaymentVersionId == source.ActualVersionId
            && share.NetAmount != 0m && share.GrossAmount != 0m
            && Math.Sign(share.NetAmount) == Math.Sign(source.NetAmount)
            && Math.Sign(share.GrossAmount) == Math.Sign(source.GrossAmount)
            && Math.Abs(share.NetAmount) <= Math.Abs(source.NetAmount)
            && Math.Abs(share.GrossAmount) <= Math.Abs(source.GrossAmount)
            && share.WithholdingTax >= 0m && share.WithholdingTax <= source.WithholdingTax
            && share.Fees >= 0m && share.Fees <= source.Fees
            && share.GrossAmount == share.NetAmount + share.WithholdingTax + share.Fees;

    public static bool IsAvailableShare(BondCouponCashAllocationDto share, AccountCashPaymentAllocationDto source,
        BondCouponAllocationConsumptionDto consumed)
        => IsBoundedShare(share, source)
            && Within(consumed.NetAmount, source.NetAmount) && Within(consumed.GrossAmount, source.GrossAmount)
            && consumed.WithholdingTax >= 0m && consumed.Fees >= 0m
            && Within(consumed.NetAmount + share.NetAmount, source.NetAmount)
            && Within(consumed.GrossAmount + share.GrossAmount, source.GrossAmount)
            && consumed.WithholdingTax + share.WithholdingTax <= source.WithholdingTax
            && consumed.Fees + share.Fees <= source.Fees;

    private static bool Within(decimal value, decimal limit)
        => value == 0m || Math.Sign(value) == Math.Sign(limit) && Math.Abs(value) <= Math.Abs(limit);

    /// <summary>Receivables and payables remain separate; no contractual setoff is inferred.</summary>
    public static IReadOnlyList<ProjectedAccountingEffectLineDto> LinesBySide(string currency,
        BondCouponBookAccountsDto accounts, decimal receivableEarnedDelta, decimal payableEarnedDelta,
        decimal receivableBeforeCash, decimal payableBeforeCash, IReadOnlyList<BondCouponCashAllocationDto> cash)
    {
        if (receivableBeforeCash < 0m || payableBeforeCash < 0m)
            throw new ArgumentException("Revised coupon economics overstate previously settled cash; reverse the affected recognition first.");
        var rows = new List<ProjectedAccountingEffectLineDto>();
        Add(accounts.Receivable, receivableEarnedDelta);
        Add(accounts.Payable, -payableEarnedDelta);
        Add(accounts.Income, -(receivableEarnedDelta - payableEarnedDelta));
        decimal received = 0m, paid = 0m;
        foreach (var allocation in cash)
        {
            if (allocation.NetAmount == 0m || allocation.GrossAmount == 0m || allocation.WithholdingTax < 0m || allocation.Fees < 0m
                || allocation.GrossAmount != allocation.NetAmount + allocation.WithholdingTax + allocation.Fees
                || new[] { allocation.NetAmount, allocation.GrossAmount, allocation.WithholdingTax, allocation.Fees }
                    .Any(amount => amount != decimal.Round(amount, 12, MidpointRounding.ToEven)))
                throw new ArgumentException("Signed coupon cash must retain exact gross, net, nonnegative deductions and journal precision.");
            if (allocation.GrossAmount > 0m)
            {
                received += allocation.GrossAmount;
                if (received > receivableBeforeCash)
                    throw new ArgumentException("Coupon receipts exceed the earned receivable.");
                Add(accounts.Receivable, -allocation.GrossAmount);
            }
            else
            {
                paid -= allocation.GrossAmount;
                if (paid > payableBeforeCash)
                    throw new ArgumentException("Coupon payments exceed the earned payable.");
                Add(accounts.Payable, -allocation.GrossAmount);
            }
            Add(accounts.Cash, allocation.NetAmount);
            Add(accounts.Tax, allocation.WithholdingTax);
            Add(accounts.Fees, allocation.Fees);
        }
        return rows.GroupBy(l => l.AccountId, StringComparer.Ordinal).Select(group =>
        {
            var signed = group.Sum(l => l.Debit - l.Credit);
            return new ProjectedAccountingEffectLineDto(group.Key, Math.Max(signed, 0m), Math.Max(-signed, 0m), currency,
                "Retained coupon accrual/collection; receivable and payable remain distinct");
        }).Where(l => l.Debit != 0m || l.Credit != 0m).ToArray();

        void Add(string account, decimal signed)
        {
            if (signed == 0m)
                return;
            if (string.IsNullOrWhiteSpace(account))
                throw new ArgumentException("Coupon account mappings must be explicit.");
            var rounded = decimal.Round(Math.Abs(signed), 12, MidpointRounding.ToEven);
            rows.Add(new(account, signed > 0m ? rounded : 0m, signed < 0m ? rounded : 0m, currency));
        }
    }

    public static IReadOnlyList<ProjectedAccountingEffectLineDto> Lines(string currency,
        BondCouponBookAccountsDto accounts, decimal earnedDelta, decimal outstandingBeforeCash,
        IReadOnlyList<BondCouponCashAllocationDto> cash)
    {
        var result = new List<ProjectedAccountingEffectLineDto>();
        var previousBalance = outstandingBeforeCash - earnedDelta;
        Add(accounts.Receivable, Math.Max(outstandingBeforeCash, 0m) - Math.Max(previousBalance, 0m));
        Add(accounts.Payable, -(Math.Max(-outstandingBeforeCash, 0m) - Math.Max(-previousBalance, 0m)));
        Add(accounts.Income, -earnedDelta);
        decimal cleared = 0m;
        foreach (var allocation in cash)
        {
            if (allocation.NetAmount == 0m || allocation.GrossAmount == 0m || allocation.WithholdingTax < 0m || allocation.Fees < 0m
                || new[] { allocation.NetAmount, allocation.GrossAmount, allocation.WithholdingTax, allocation.Fees }
                    .Any(amount => amount != decimal.Round(amount, 12, MidpointRounding.ToEven))
                || allocation.GrossAmount != allocation.NetAmount + allocation.WithholdingTax + allocation.Fees
                || Math.Sign(allocation.GrossAmount) != Math.Sign(outstandingBeforeCash))
                throw new ArgumentException("Coupon clearing requires signed gross/net and nonnegative evidenced cash costs.");
            cleared += allocation.GrossAmount;
            if (Math.Abs(cleared) > Math.Abs(outstandingBeforeCash))
                throw new ArgumentException("Coupon cash cannot clear more than the earned/purchased outstanding balance.");
            if (allocation.GrossAmount > 0m)
                Add(accounts.Receivable, -allocation.GrossAmount);
            else
                Add(accounts.Payable, -allocation.GrossAmount);
            Add(accounts.Cash, allocation.NetAmount);
            Add(accounts.Tax, allocation.WithholdingTax);
            Add(accounts.Fees, allocation.Fees);
        }
        return result.GroupBy(line => line.AccountId, StringComparer.Ordinal).Select(group => new ProjectedAccountingEffectLineDto(
            group.Key, Math.Max(group.Sum(line => line.Debit - line.Credit), 0m), Math.Max(group.Sum(line => line.Credit - line.Debit), 0m), currency,
            "Retained coupon accrual/collection; no payment instruction"))
            .Where(line => line.Debit != 0m || line.Credit != 0m).ToArray();

        void Add(string account, decimal signedDebit)
        {
            if (signedDebit == 0m)
                return;
            if (string.IsNullOrWhiteSpace(account))
                throw new ArgumentException("Coupon book account mappings must be explicit.");
            var amount = decimal.Round(Math.Abs(signedDebit), 12, MidpointRounding.ToEven);
            result.Add(new(account, signedDebit > 0m ? amount : 0m, signedDebit < 0m ? amount : 0m, currency));
        }
    }

    public static string Fingerprint(BondCouponRecognitionInstructionDto instruction)
        => Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(instruction));
}
