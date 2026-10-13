using System.Text.Json;
using Meridian.Contracts.AssetOperations;

namespace Meridian.Contracts.Accounting.Lots;

/// <summary>Reviewed assertion; Storage binds it to both immutable posting receipts before any write.</summary>
public sealed record OpenLotAmortizationContinuationDto(
    string ModelVersion,
    Guid SuccessorMutationBatchId,
    OpenLotSuccessorInstructionDto Exchange,
    OpenLotAmortizationInstructionDto PredecessorAmortization);

/// <summary>Continues existing v2 economics only across an unchanged one-for-one Reg S/144A exchange.</summary>
public static class OpenLotAmortizationContinuation
{
    public const string ModelVersion = "canonical-lot-exchange-continuation-v1";

    /// <returns>The immutable original acquisition that owns the continued calculation schedule.</returns>
    public static OpenLotDto Validate(OpenLotAmortizationInstructionDto instruction)
    {
        var lot = instruction.ExpectedLot;
        var continuation = instruction.Continuation;
        if (lot.Acquisition.CorporateActionLineage is null)
        {
            Require(continuation is null, "An ordinary acquisition cannot carry successor amortization authority.");
            return lot;
        }
        Require(continuation is not null,
            "Corporate-action successors require a separately reviewed amortization/yield continuation; inherited acquisition facts do not authorize a new schedule.");
        Require(continuation!.ModelVersion == ModelVersion && continuation.SuccessorMutationBatchId != Guid.Empty
            && instruction.CalculationVersion == OpenLotAmortization.ModelVersion,
            "Successor amortization requires a supported version and an immutable exchange receipt.");
        ArgumentNullException.ThrowIfNull(continuation.Exchange);
        ArgumentNullException.ThrowIfNull(continuation.PredecessorAmortization);
        var exchange = continuation.Exchange;
        var prior = continuation.PredecessorAmortization;
        Require(prior.Continuation is null && prior.Reversal is null
            && prior.ExpectedLot.Acquisition.CorporateActionLineage is null
            && prior.CalculationVersion == OpenLotAmortization.ModelVersion,
            "Exchange continuation requires an ordinary retained v2 predecessor amortization; successor chains and historical calculations require separate review.");
        OpenLotSuccessors.ValidateRetained(exchange);
        Require(exchange.Projection.Treatment.ActionType == CorporateActionAccountingTypeDto.RegS144AExchange
            && exchange.Successors.Count == 1 && exchange.ExpectedLot.Acquisition.QuantityBasis == LotQuantityBasis.Face,
            "Amortization continuation supports only a one-for-one face-lot Reg S/144A exchange.");
        var parent = exchange.ExpectedLot;
        var target = exchange.Successors[0];
        Require(target.Lot.OriginalQuantity == parent.OpenQuantity,
            "Amortization continuation requires unchanged exchanged face quantity.");
        var origin = OpenLotSuccessors.WithRetainedLineage(exchange, target.Lot, lot.Acquisition.CorporateActionLineage!);
        Require(lot.Version >= origin.Version && lot.OpenQuantity > 0m && lot.OpenQuantity <= origin.OpenQuantity
            && OpenLotAmortization.SameLot(origin with
            {
                Version = lot.Version,
                OpenQuantity = lot.OpenQuantity,
                OpenTransactionCostBasis = lot.OpenTransactionCostBasis,
                OpenFunctionalCostBasis = lot.OpenFunctionalCostBasis
            }, lot), "Successor amortization must retain the exact exchange identity, acquisition facts and holding history.");
        var projected = OpenLotAmortization.Project(prior);
        Require(OpenLotAmortization.SameLot(prior.ExpectedLot with
        {
            Version = prior.ExpectedLot.Version + 1,
            OpenTransactionCostBasis = projected.TransactionCostBasis,
            OpenFunctionalCostBasis = projected.FunctionalCostBasis
        }, parent), "Exchange continuation requires the unchanged immediately preceding amortization snapshot.");
        Require(exchange.ExpectedSecurityVersion == prior.Security.Version
            && exchange.ExpectedSecurityHash == OpenLotAmortization.SecurityHash(prior.Security)
            && prior.AsOfDate <= exchange.Projection.EconomicEvent!.EffectiveDate
            && instruction.AsOfDate >= exchange.Projection.EconomicEvent.EffectiveDate
            && instruction.AsOfDate > prior.AsOfDate,
            "Exchange continuation must follow the retained amortization and corporate-action dates and reference authority.");
        if (instruction.Reversal is null)
        {
            Require(target.ExpectedSecurityVersion == instruction.Security.Version
                && target.ExpectedSecurityHash == OpenLotAmortization.SecurityHash(instruction.Security)
                && prior.Security.AssetClass == instruction.Security.AssetClass
                && prior.Security.Currency == instruction.Security.Currency
                && JsonElement.DeepEquals(prior.Security.CommonTerms, instruction.Security.CommonTerms)
                && JsonElement.DeepEquals(prior.Security.AssetSpecificTerms, instruction.Security.AssetSpecificTerms),
                "Exchange continuation requires unchanged retained predecessor and successor economic terms; reference revisions require separate schedule review.");
        }
        return prior.ExpectedLot;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new ArgumentException(message);
    }
}
