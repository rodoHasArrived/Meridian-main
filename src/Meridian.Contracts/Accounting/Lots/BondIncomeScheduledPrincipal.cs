using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Ledger;

namespace Meridian.Contracts.Accounting.Lots;

/// <summary>Adopted scheduled repayment; each pinned income instruction proves current carrying basis.</summary>
public sealed record BondIncomeScheduledPrincipalDto(string ModelVersion,
    IReadOnlyList<OpenLotAmortizationInstructionDto> IncomeInstructions);

public static class BondIncomeScheduledPrincipal
{
    public const string ModelVersion = "bond-income-scheduled-principal-v1";

    public static IReadOnlyList<OpenLotFactorPaydownLotDto> Allocate(OpenLotFactorPaydownInstructionDto instruction,
        BondIncomeScheduledPrincipalDto scheduled)
    {
        ArgumentNullException.ThrowIfNull(scheduled.IncomeInstructions);
        if (scheduled.ModelVersion != ModelVersion || scheduled.IncomeInstructions.Count != instruction.Lots.Count
            || instruction.Scope.AccountingBasis != AccountingBasisKindDto.Gaap
            || !instruction.EconomicEvent.RetainedEvidence.Any(e => e.SubjectType == "BondPrincipalCashReceipt"
                && e.EffectiveDate == instruction.EconomicEvent.EffectiveDate && RetainedEvidenceIdentityValidator.IsComplete(e)))
            throw new ArgumentException("Scheduled GAAP principal requires dated retained cash-receipt evidence and versioned income instructions.");
        var raw = new decimal[instruction.Lots.Count];
        var precision = CurrencyMinorUnits.GetPrecision(instruction.Currency);
        var unit = new decimal(1, 0, 0, false, (byte)precision);
        for (var i = 0; i < instruction.Lots.Count; i++)
        {
            var lot = instruction.Lots[i].ExpectedLot;
            var inputs = scheduled.IncomeInstructions[i];
            if (!OpenLotAmortization.SameLot(lot, inputs.ExpectedLot) || inputs.AsOfDate != instruction.EconomicEvent.EffectiveDate
                || inputs.BondIncome is not { } income || inputs.CalculationVersion != BondIncomeCalculator.ModelVersion
                || lot.Acquisition.AcquisitionCurrency != instruction.Currency || lot.Acquisition.FunctionalCurrency != instruction.Currency)
                throw new ArgumentException("Scheduled principal must bind the exact same-currency lot, date and adopted income authority.");
            var target = OpenLotAmortization.Project(inputs);
            if (target.TransactionMovement != 0m || target.FunctionalMovement != 0m)
                throw new ArgumentException("Accrue the adopted income model before relieving scheduled principal basis.");
            var terms = lot.Acquisition.FaceValueTerms!;
            if ((lot.CurrentFactor ?? terms.BookedFactor) != instruction.PriorFactor)
                throw new ArgumentException("Scheduled-principal factor differs from the authoritative lot.");
            var payments = income.CurrentProjection.CashFlows.Where(f => f.PaymentDate == inputs.AsOfDate && f.Principal > 0m).ToArray();
            var parFace = lot.OriginalQuantity / terms.BookedFactor;
            if (payments.Length == 0 || payments.Any(f => f.IsForecast)
                || payments.Min(f => f.OutstandingPrincipal) != parFace * instruction.NewFactor)
                throw new ArgumentException("Observed contractual principal must match the dated unindexed factor decline.");
            raw[i] = payments.Sum(f => f.Principal) * lot.OpenQuantity / lot.OriginalQuantity;
        }
        if (instruction.PrincipalPaydown != decimal.Round(raw.Sum(), precision, MidpointRounding.AwayFromZero))
            throw new ArgumentException("Scheduled principal cash must equal the retained indexed contractual payments.");
        var floors = raw.Select(x => decimal.Round(x, precision, MidpointRounding.ToZero)).ToArray();
        var extra = (instruction.PrincipalPaydown - floors.Sum()) / unit;
        if (extra < 0m || extra != decimal.Truncate(extra) || extra > raw.Length)
            throw new ArgumentException("Scheduled principal allocation exceeds currency rounding bounds.");
        var receives = Enumerable.Range(0, raw.Length).OrderByDescending(i => raw[i] - floors[i])
            .ThenByDescending(i => i).Take((int)extra).ToHashSet();
        var allocated = new OpenLotFactorPaydownLotDto[raw.Length];
        for (var i = 0; i < raw.Length; i++)
        {
            var lot = instruction.Lots[i].ExpectedLot;
            var principal = floors[i] + (receives.Contains(i) ? unit : 0m);
            var after = lot.OpenTransactionCostBasis - principal;
            if (after < 0m || instruction.NewFactor == 0m && after != 0m)
                throw new ArgumentException("Scheduled principal requires fully accrued carrying basis and cannot leave a terminal residual.");
            allocated[i] = new(lot, instruction.PriorFactor, principal, after,
                lot.OpenFunctionalCostBasis - principal, i);
        }
        return allocated;
    }
}
