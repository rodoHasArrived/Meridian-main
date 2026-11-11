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

// The lot model and the open-lot read model.
public sealed partial class SecurityMasterWorkbenchQueryService
{
    private static SecurityMasterLotModelDto BuildLotModel(
        SecurityDetailDto detail,
        SecurityEconomicDefinitionRecord? economic,
        TradingParametersDto? trading)
    {
        var assetCapability = GetAssetCapability(detail.AssetClass);
        var economicTerms = economic?.EconomicTerms;
        var hasStructuredFactor = TryGetNestedJsonDecimal(economicTerms, "structuredProduct", "factor").HasValue;
        var multiplier =
            trading?.ContractMultiplier
            ?? TryGetJsonDecimal(detail.AssetSpecificTerms, "multiplier")
            ?? TryGetNestedJsonDecimal(economicTerms, "contract", "multiplier");
        var lotSize = trading?.LotSize ?? TryGetJsonDecimal(detail.CommonTerms, "lotSize");

        var quantityModel =
            hasStructuredFactor ? "FactorAdjustedFace" :
            assetCapability.UsesFaceValueLots ? "FaceValue" :
            multiplier is > 0m ? "ContractUnits" :
            "Units";

        var summary = quantityModel switch
        {
            "FactorAdjustedFace" => $"Lots should reconcile by current face using factor-adjusted exposure{FormatLotSizeSuffix(lotSize)}.",
            "FaceValue" => $"Lots should reconcile by face/par exposure{FormatLotSizeSuffix(lotSize)}.",
            "ContractUnits" => multiplier is > 0m
                ? $"Lots should reconcile in contract units with multiplier {multiplier:0.########}{FormatLotSizeSuffix(lotSize)}."
                : $"Lots should reconcile in contract units{FormatLotSizeSuffix(lotSize)}.",
            _ => $"Lots should reconcile in whole or fractional units{FormatLotSizeSuffix(lotSize)}."
        };

        return new SecurityMasterLotModelDto(
            QuantityModel: quantityModel,
            LotSize: lotSize,
            ContractMultiplier: multiplier,
            UsesFaceValue: assetCapability.UsesFaceValueLots,
            SupportsFactorAdjustedExposure: hasStructuredFactor,
            RequiresResolvedSecurityId: true,
            Summary: summary);
    }

    private async Task<SecurityMasterOpenLotReadModelDto> BuildOpenLotReadModelAsync(
        SecurityDetailDto detail,
        SecurityEconomicDefinitionRecord? economic,
        TradingParametersDto? trading,
        string? fundProfileId,
        DateTimeOffset asOfUtc,
        CancellationToken ct)
    {
        var lotModel = BuildLotModel(detail, economic, trading);
        var currentFactor = TryGetNestedJsonDecimal(economic?.EconomicTerms, "structuredProduct", "factor");
        var currentFactorDate = TryGetNestedJsonDateOnly(economic?.EconomicTerms, "structuredProduct", "factorDate");

        if (string.IsNullOrWhiteSpace(fundProfileId))
        {
            return new SecurityMasterOpenLotReadModelDto(
                QuantityModel: lotModel.QuantityModel,
                LotSize: lotModel.LotSize,
                ContractMultiplier: lotModel.ContractMultiplier,
                UsesFaceValue: lotModel.UsesFaceValue,
                SupportsFactorAdjustedExposure: lotModel.SupportsFactorAdjustedExposure,
                RequiresResolvedSecurityId: lotModel.RequiresResolvedSecurityId,
                CurrentFactor: currentFactor,
                CurrentFactorDate: currentFactorDate,
                AsOfUtc: asOfUtc,
                Summary: "Open-lot read model is unscoped. Select a fund profile to materialize account and portfolio lots.",
                Lots: [],
                ProvenanceHistory: []);
        }

        var scopedFundProfileId = fundProfileId.Trim();
        // Tenant isolation (SEC-005): withhold a foreign fund's lots — treat as no scoped runs so the
        // unscoped/empty read model is returned and no cross-tenant lots are materialized.
        IReadOnlyList<StrategyRunEntry> relatedRuns =
            await IsFundAccessibleToCurrentTenantAsync(scopedFundProfileId, ct).ConfigureAwait(false)
                ? await LoadFundRunsAsync(scopedFundProfileId, ct).ConfigureAwait(false)
                : [];
        if (relatedRuns.Count == 0)
        {
            return new SecurityMasterOpenLotReadModelDto(
                QuantityModel: lotModel.QuantityModel,
                LotSize: lotModel.LotSize,
                ContractMultiplier: lotModel.ContractMultiplier,
                UsesFaceValue: lotModel.UsesFaceValue,
                SupportsFactorAdjustedExposure: lotModel.SupportsFactorAdjustedExposure,
                RequiresResolvedSecurityId: lotModel.RequiresResolvedSecurityId,
                CurrentFactor: currentFactor,
                CurrentFactorDate: currentFactorDate,
                AsOfUtc: asOfUtc,
                Summary: $"Fund profile {scopedFundProfileId} has no recorded runs with open-lot context.",
                Lots: [],
                ProvenanceHistory: []);
        }

        var normalizedIdentifiers = BuildNormalizedIdentifierSet(detail);
        var settlementCycleDays = TryGetJsonInt(detail.CommonTerms, "settlementCycleDays") ?? 0;
        var lots = new List<SecurityMasterOpenLotDto>();
        var provenance = new List<SecurityMasterOpenLotProvenanceDto>();

        foreach (var run in relatedRuns)
        {
            ct.ThrowIfCancellationRequested();

            var latestSnapshot = run.Metrics?.Snapshots.LastOrDefault();
            if (latestSnapshot is null)
            {
                continue;
            }

            var portfolio = await _portfolioReadService.BuildSummaryAsync(run, ct).ConfigureAwait(false);
            var matchingPositions = portfolio?.Positions
                .Where(position => MatchesSecurity(position.Symbol, position.Security?.SecurityId, detail, normalizedIdentifiers))
                .ToArray()
                ?? [];

            var snapshotLots = ExtractSnapshotOpenLots(latestSnapshot);
            var matchingLots = snapshotLots
                .Where(item => MatchesSecurity(item.Lot.Symbol, securityId: null, detail, normalizedIdentifiers))
                .ToArray();

            if (matchingLots.Length == 0)
            {
                continue;
            }

            var portfolioId = portfolio?.PortfolioId ?? run.PortfolioId ?? run.RunId;
            provenance.Add(new SecurityMasterOpenLotProvenanceDto(
                ProvenanceId: $"run-{run.RunId}",
                RunId: run.RunId,
                PortfolioId: portfolioId,
                AccountScopeId: matchingPositions.FirstOrDefault()?.AccountScopeId,
                AccountScopeDisplayName: matchingPositions.FirstOrDefault()?.AccountScopeDisplayName,
                SourceSystem: "strategy-run-snapshot",
                SourceRecordId: $"{run.RunId}:{latestSnapshot.Timestamp:O}",
                AsOfUtc: latestSnapshot.Timestamp,
                Summary: $"{matchingLots.Length} open lot(s) matched from run {run.RunId} portfolio snapshot."));

            foreach (var snapshotLot in matchingLots)
            {
                var quantity = snapshotLot.Lot.Quantity;
                var price = snapshotLot.Lot.EntryPrice;
                var position = ResolveSnapshotPosition(latestSnapshot, snapshotLot);
                // IsShort is additive and therefore false when an older persisted snapshot did
                // not contain the field. Recover direction from the account-scoped signed
                // position so legacy short lots cannot be presented with inverted economics.
                var (isShort, unrealizedPnl) = SecurityMasterSnapshotLotEconomics.Project(snapshotLot.Lot, position);
                var costBasis = snapshotLot.Lot.CostBasis();
                var accountScopeId = snapshotLot.AccountId
                    ?? snapshotLot.Lot.AccountId
                    ?? matchingPositions.FirstOrDefault(static position => !string.IsNullOrWhiteSpace(position.AccountScopeId))?.AccountScopeId;
                var accountScopeDisplayName = snapshotLot.AccountDisplayName
                    ?? matchingPositions.FirstOrDefault(static position => !string.IsNullOrWhiteSpace(position.AccountScopeDisplayName))?.AccountScopeDisplayName;
                var vehicleScopeId = matchingPositions.FirstOrDefault(static position => !string.IsNullOrWhiteSpace(position.VehicleScopeId))?.VehicleScopeId;
                var vehicleScopeDisplayName = matchingPositions.FirstOrDefault(static position => !string.IsNullOrWhiteSpace(position.VehicleScopeDisplayName))?.VehicleScopeDisplayName;
                var (originalFace, currentFace, factorAdjustedQuantity, factorAdjustedFace) = ProjectLotFaces(quantity, lotModel, currentFactor);

                lots.Add(new SecurityMasterOpenLotDto(
                    SecurityId: detail.SecurityId,
                    PortfolioId: portfolioId,
                    RunId: run.RunId,
                    AccountScopeId: accountScopeId,
                    AccountScopeDisplayName: accountScopeDisplayName,
                    VehicleScopeId: vehicleScopeId,
                    VehicleScopeDisplayName: vehicleScopeDisplayName,
                    LotId: snapshotLot.Lot.LotId.ToString("N"),
                    Symbol: snapshotLot.Lot.Symbol,
                    TradeDate: snapshotLot.Lot.OpenedAt,
                    SettleDate: settlementCycleDays > 0 ? snapshotLot.Lot.OpenedAt.AddDays(settlementCycleDays) : null,
                    OriginalQuantity: quantity,
                    CurrentQuantity: quantity,
                    OriginalFace: originalFace,
                    CurrentFace: currentFace,
                    FactorAdjustedQuantity: factorAdjustedQuantity,
                    FactorAdjustedFace: factorAdjustedFace,
                    CostBasis: costBasis,
                    EntryPrice: price,
                    UnrealizedPnl: unrealizedPnl,
                    Currency: detail.Currency,
                    LotStatus: "Open",
                    SourceSystem: "strategy-run-snapshot",
                    SourceRecordId: $"{run.RunId}:{portfolioId}:{snapshotLot.Lot.LotId:N}",
                    AsOfUtc: latestSnapshot.Timestamp,
                    SourceUpdatedBy: null,
                    SourceReason: "Latest scoped run snapshot",
                    IsLongTerm: snapshotLot.Lot.IsLongTerm(latestSnapshot.Timestamp),
                    Notes: snapshotLot.Lot.Notes)
                {
                    IsShort = isShort
                });
            }
        }

        var orderedLots = lots
            .OrderBy(static lot => lot.TradeDate)
            .ThenBy(static lot => lot.LotId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var summary = orderedLots.Length == 0
            ? $"No scoped open lots currently reference security {detail.SecurityId} in fund profile {scopedFundProfileId}."
            : lotModel.SupportsFactorAdjustedExposure && currentFactor.HasValue
                ? $"{orderedLots.Length} scoped open lot(s) reconcile with factor-adjusted face support at factor {currentFactor.Value:0.########}."
                : $"{orderedLots.Length} scoped open lot(s) matched across {orderedLots.Select(static lot => lot.PortfolioId).Distinct(StringComparer.OrdinalIgnoreCase).Count()} portfolio(s).";

        return new SecurityMasterOpenLotReadModelDto(
            QuantityModel: lotModel.QuantityModel,
            LotSize: lotModel.LotSize,
            ContractMultiplier: lotModel.ContractMultiplier,
            UsesFaceValue: lotModel.UsesFaceValue,
            SupportsFactorAdjustedExposure: lotModel.SupportsFactorAdjustedExposure,
            RequiresResolvedSecurityId: lotModel.RequiresResolvedSecurityId,
            CurrentFactor: currentFactor,
            CurrentFactorDate: currentFactorDate,
            AsOfUtc: orderedLots.LastOrDefault()?.AsOfUtc ?? asOfUtc,
            Summary: summary,
            Lots: orderedLots,
            ProvenanceHistory: provenance
                .OrderByDescending(static item => item.AsOfUtc)
                .ThenBy(static item => item.RunId, StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    private static IReadOnlyList<SnapshotOpenLotEnvelope> ExtractSnapshotOpenLots(PortfolioSnapshot snapshot)
    {
        var lots = new List<SnapshotOpenLotEnvelope>();
        foreach (var account in snapshot.Accounts.Values)
        {
            foreach (var lot in account.OpenLots ?? [])
            {
                lots.Add(new SnapshotOpenLotEnvelope(account.AccountId, account.DisplayName, lot));
            }
        }

        if (lots.Count > 0)
        {
            return lots;
        }

        foreach (var position in snapshot.Positions.Values)
        {
            foreach (var lot in position.OpenLots ?? [])
            {
                lots.Add(new SnapshotOpenLotEnvelope(lot.AccountId, null, lot));
            }
        }

        return lots;
    }

    private static Position? ResolveSnapshotPosition(
        PortfolioSnapshot snapshot,
        SnapshotOpenLotEnvelope snapshotLot)
    {
        var accountId = snapshotLot.AccountId ?? snapshotLot.Lot.AccountId;
        if (!string.IsNullOrWhiteSpace(accountId) &&
            snapshot.Accounts.TryGetValue(accountId, out var account) &&
            account.Positions.TryGetValue(snapshotLot.Lot.Symbol, out var accountPosition))
        {
            return accountPosition;
        }

        return snapshot.Positions.TryGetValue(snapshotLot.Lot.Symbol, out var aggregatePosition)
            ? aggregatePosition
            : null;
    }

    private static (decimal? OriginalFace, decimal? CurrentFace, decimal? FactorAdjustedQuantity, decimal? FactorAdjustedFace) ProjectLotFaces(
        decimal quantity,
        SecurityMasterLotModelDto lotModel,
        decimal? currentFactor)
    {
        if (!lotModel.UsesFaceValue && !lotModel.SupportsFactorAdjustedExposure)
        {
            return (null, null, null, null);
        }

        if (lotModel.SupportsFactorAdjustedExposure)
        {
            var factor = currentFactor is > 0m ? currentFactor.Value : 1m;
            var currentFace = quantity * factor;
            return (quantity, currentFace, currentFace, currentFace);
        }

        return lotModel.UsesFaceValue
            ? (quantity, quantity, null, null)
            : (null, null, null, null);
    }
}
