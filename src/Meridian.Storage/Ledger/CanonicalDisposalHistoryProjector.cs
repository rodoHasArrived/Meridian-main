using Meridian.Contracts.Accounting.Lots;
using Meridian.Ledger;

namespace Meridian.Storage.Ledger;

/// <summary>Certifies disposal economics against retained current basis while preserving acquisition evidence.</summary>
public static class CanonicalDisposalHistoryProjector
{
    public static LedgerTaxLotReliefProjection Project(LedgerTaxLotDisposalHistoryRecord disposal,
        JournalEntry entry, Guid ledgerBookId, string functionalCurrency)
    {
        var canonical = disposal.CanonicalLots;
        if (disposal.JournalEntryId != entry.JournalEntryId)
            throw new LedgerValidationException("Retained disposal belongs to a different journal.");
        if (canonical is null || canonical.Count != disposal.Lots.Count || canonical.Count == 0)
            throw new LedgerValidationException("Disposal history lacks canonical acquisition evidence. Resolve the open-lot backfill exception for this durable lot with reviewed acquisition evidence.");
        if (!Enum.IsDefined(disposal.ReliefMethod))
            throw new LedgerValidationException("Retained disposal has an unsupported relief method.");
        var recipients = CertifyDeferralRecipients(disposal, canonical);
        var averageCost = disposal.ReliefMethod == LedgerTaxLotReliefMethod.AverageCost;
        if (averageCost)
            CertifyAverageCostRelief(disposal, canonical);
        var history = new List<LedgerTaxLotDisposalHistoryLot>(canonical.Count);
        var seen = new HashSet<Guid>();
        for (var index = 0; index < canonical.Count; index++)
        {
            var lot = canonical[index];
            OpenLotValidation.Validate(lot);
            var retained = disposal.Lots[index];
            var scale = lot.Acquisition.QuantityBasis == LotQuantityBasis.Face
                ? LedgerTaxLotFaceValueTerms.LedgerLotParBasis : 1m;
            var quantity = retained.Quantity * scale;
            if (!seen.Add(lot.TaxLotRecordId) || lot.LedgerBookId != ledgerBookId ||
                lot.Acquisition.FunctionalCurrency != functionalCurrency || lot.LotId != retained.LotId ||
                lot.AcquiredDate != retained.AcquiredDate || retained.HoldingPeriodStart > retained.AcquiredDate ||
                quantity <= 0m || quantity > lot.OpenQuantity ||
                (!averageCost && (quantity == lot.OpenQuantity ? lot.OpenFunctionalCostBasis
                    : lot.OpenFunctionalCostBasis * quantity / lot.OpenQuantity) != retained.CostBasis) ||
                entry.Lines.Any(line => !IsRetainedRecipientDebit(line, recipients) &&
                    (line.Dimensions?.InstrumentId != lot.SecurityId ||
                        line.Dimensions?.PositionId != lot.BookPositionId)))
                throw new LedgerValidationException("Retained disposal quantity, basis, or security/book-position scope differs from canonical lot evidence.");
            history.Add(retained with
            {
                Quantity = quantity,
                // The retained mutation unit cost remains an acquisition fact. Reporting prices
                // every slice from its certified current basis, including governed restatements.
                UnitCost = retained.CostBasis / quantity,
                HoldingPeriodStart = retained.HoldingPeriodStart < lot.Acquisition.HoldingPeriodStartDate
                    ? retained.HoldingPeriodStart : lot.Acquisition.HoldingPeriodStartDate
            });
        }
        var assetLines = entry.Lines.Where(line => line.Account == disposal.Account).ToArray();
        if (assetLines.Length != 1 || assetLines[0].Debit != 0m ||
            assetLines[0].Credit != disposal.Lots.Sum(static lot => lot.CostBasis))
            throw new LedgerValidationException("Retained disposal basis does not match the exact asset-account journal movement.");
        var recognized = entry.Lines.Where(line => line.Account.Name == LedgerAccounts.RealizedGain.Name)
            .Sum(static line => line.Credit - line.Debit)
            - entry.Lines.Where(line => line.Account.Name == LedgerAccounts.RealizedLoss.Name)
                .Sum(static line => line.Debit - line.Credit);
        var projection = ProjectCertifiedEconomics(disposal, entry, canonical, history, recognized, functionalCurrency);
        if (projection.CostBasis != disposal.Lots.Sum(static lot => lot.CostBasis) ||
            projection.RecognizedGainOrLoss != recognized)
            throw new LedgerValidationException("Canonical disposal report does not reconcile to retained journal economics.");
        return projection with { CanonicalOpenLots = canonical };
    }

    // The generic relief projector reprices quantity times unit cost and rounds basis to cents.
    // Canonical basis can contain fractional cents or a full-lot decimal residual; re-relieving
    // a derived unit cost would discard that certified amount. Only proceeds attribution rounds.
    private static LedgerTaxLotReliefProjection ProjectCertifiedEconomics(
        LedgerTaxLotDisposalHistoryRecord disposal, JournalEntry entry,
        IReadOnlyList<OpenLotDto> canonical, IReadOnlyList<LedgerTaxLotDisposalHistoryLot> history,
        decimal recognized, string functionalCurrency)
    {
        var quantity = history.Sum(static lot => lot.Quantity);
        var basis = history.Sum(static lot => lot.CostBasis);
        var disallowed = disposal.WashSaleBasisIncreases.Sum(static increase => increase.Amount);
        var proceeds = basis + recognized - disallowed;
        if (proceeds < 0m)
            throw new LedgerValidationException("Retained canonical disposal cannot produce nonnegative proceeds.");
        if (disposal.WashSaleBasisIncreases.Count > 0 || entry.Lines.Any(static line =>
                line.Account.Name == LedgerAccounts.Cash.Name || line.Account.Name.StartsWith("Cash (", StringComparison.Ordinal)))
            CertifyDeferredDisposalJournal(disposal, entry, functionalCurrency, proceeds, disallowed);
        if (disposal.ProceedsAllocationVersion is not null and not LedgerTaxLotReliefProjector.CurrentProceedsAllocationVersion ||
            disposal.ProceedsAllocationVersion is null && disposal.SalePrice is not null)
            throw new LedgerValidationException("Retained canonical disposal has an unsupported proceeds allocation version or quote.");
        var scale = canonical[0].Acquisition.QuantityBasis == LotQuantityBasis.Face
            ? LedgerTaxLotFaceValueTerms.LedgerLotParBasis : 1m;
        var salePrice = disposal.SalePrice / scale ?? (disposal.ProceedsAllocationVersion is null
            ? decimal.Round(proceeds / quantity, 10, MidpointRounding.AwayFromZero) : proceeds / quantity);
        if (disposal.SalePrice is not null &&
            Meridian.Contracts.Ledger.LedgerCurrencyRounding.RoundCurrency(quantity * salePrice) != proceeds)
            throw new LedgerValidationException("Retained canonical disposal quote differs from journal proceeds.");
        var saleDate = entry.Metadata.EffectiveDate ?? DateOnly.FromDateTime(entry.Timestamp.UtcDateTime);
        var lots = history.Select((lot, index) => new LedgerTaxLot(lot.LotId, lot.AcquiredDate,
            lot.Quantity, lot.UnitCost, canonical[index].SecurityId,
            lot.HoldingPeriodStart < lot.AcquiredDate ? lot.HoldingPeriodStart : null)).ToArray();
        var input = new LedgerTaxLotReliefInput(disposal.Account, saleDate, quantity, salePrice,
            disposal.ReliefMethod, lots, financialAccountId: disposal.Account.FinancialAccountId,
            specificLotIds: disposal.ReliefMethod == LedgerTaxLotReliefMethod.SpecificId
                ? history.Select(static lot => lot.LotId).ToArray() : null);
        var selections = new List<LedgerTaxLotReliefSelection>(history.Count);
        var remainingProceeds = proceeds;
        var economicResult = recognized - disallowed;
        var remainingEconomicResult = economicResult;
        var remainingResult = Math.Abs(economicResult);
        var remainingBasis = basis;
        // Apply the retained allocator to certified basis directly: repricing unit costs would
        // lose adjusted-basis fractional cents. Versioned discrete relief keeps main's sign bounds.
        var signPreserving = disposal.ReliefMethod != LedgerTaxLotReliefMethod.AverageCost &&
            disposal.ProceedsAllocationVersion == LedgerTaxLotReliefProjector.CurrentProceedsAllocationVersion;
        var minimums = history.Select(lot => salePrice >= lot.UnitCost ? lot.CostBasis : 0m).ToArray();
        var maximums = history.Select(lot => salePrice <= lot.UnitCost ? lot.CostBasis : proceeds).ToArray();
        var remainingMinimum = minimums.Sum();
        var remainingMaximum = maximums.Sum();
        if (signPreserving && (proceeds < remainingMinimum || proceeds > remainingMaximum))
            throw new LedgerValidationException("Retained canonical proceeds cannot preserve each parcel's economic result sign.");
        for (var index = 0; index < history.Count; index++)
        {
            var retained = history[index];
            decimal parcelProceeds;
            decimal parcelResult;
            if (disposal.ReliefMethod == LedgerTaxLotReliefMethod.AverageCost)
            {
                // Allocate the pooled result with one sign so basis residuals cannot manufacture
                // loss parcels inside a gain-producing pool (or proceeds below zero).
                remainingBasis -= retained.CostBasis;
                var result = index == history.Count - 1 ? remainingResult
                    : Math.Min(remainingResult, Meridian.Contracts.Ledger.LedgerCurrencyRounding.RoundCurrency(
                        Math.Abs(economicResult) * (retained.Quantity / quantity)));
                if (economicResult < 0m)
                    result = Math.Clamp(result, Math.Max(0m, remainingResult - remainingBasis),
                        Math.Min(remainingResult, retained.CostBasis));
                remainingResult -= result;
                parcelResult = economicResult < 0m ? -result : result;
                parcelProceeds = retained.CostBasis + parcelResult;
            }
            else if (signPreserving)
            {
                remainingMinimum -= minimums[index];
                remainingMaximum -= maximums[index];
                var lower = Math.Max(minimums[index], remainingProceeds - remainingMaximum);
                var upper = Math.Min(maximums[index], remainingProceeds - remainingMinimum);
                parcelProceeds = Math.Clamp(Meridian.Contracts.Ledger.LedgerCurrencyRounding.RoundCurrency(
                    retained.Quantity * salePrice), lower, upper);
                parcelResult = parcelProceeds - retained.CostBasis;
            }
            else
            {
                parcelProceeds = index == history.Count - 1 ? remainingProceeds
                    : Math.Min(remainingProceeds, Meridian.Contracts.Ledger.LedgerCurrencyRounding.RoundCurrency(
                        retained.Quantity * salePrice));
                parcelResult = parcelProceeds - retained.CostBasis;
            }
            if (index == history.Count - 1)
            {
                parcelProceeds = remainingProceeds;
                parcelResult = remainingEconomicResult;
            }
            remainingProceeds -= parcelProceeds;
            remainingEconomicResult -= parcelResult;
            selections.Add(new(lots[index], retained.Quantity, retained.CostBasis, retained.UnitCost,
                parcelProceeds, parcelResult,
                TaxCharacterRule.Classify(retained.HoldingPeriodStart, saleDate),
                TaxCharacterRule.HoldingPeriodDays(retained.HoldingPeriodStart, saleDate),
                retained.HoldingPeriodStart < retained.AcquiredDate));
        }
        var economicLoss = selections.Where(static selection => selection.RealizedGainOrLoss < 0m)
            .Sum(static selection => -selection.RealizedGainOrLoss);
        return new(input, selections, proceeds, basis, economicResult,
            entry.Lines.Select(static line => (line.Account, line.Debit, line.Credit)).ToArray())
        {
            EffectiveLots = lots,
            WashSale = disposal.WashSaleBasisIncreases.Count == 0 ? null
                : new WashSaleOutcome(disallowed, Math.Max(0m, economicLoss - disallowed),
                    disposal.MatchedReplacementQuantity, disposal.WashSaleBasisIncreases)
        };
    }

    private static bool IsDisposalCash(LedgerEntry line, LedgerAccount assetAccount, string functionalCurrency)
    {
        var financialAccountId = assetAccount.FinancialAccountId;
        var cashAccount = string.IsNullOrWhiteSpace(financialAccountId)
            ? LedgerAccounts.Cash : LedgerAccounts.CashAccount(financialAccountId);
        var currencyCashAccount = LedgerAccounts.CashInCurrency(functionalCurrency, financialAccountId);
        return line.Account == cashAccount || line.Account == currencyCashAccount;
    }

    private static IReadOnlyList<LedgerTaxLotDisposalRecipientEvidence> CertifyDeferralRecipients(
        LedgerTaxLotDisposalHistoryRecord disposal, IReadOnlyList<OpenLotDto> canonical)
    {
        var recipients = disposal.DeferralRecipients ?? [];
        if (recipients.Count != disposal.WashSaleBasisIncreases.Count)
            throw new LedgerValidationException("Retained wash-sale deferrals lack exact replacement recipient evidence.");
        var seen = new HashSet<Guid>();
        for (var index = 0; index < recipients.Count; index++)
        {
            var recipient = recipients[index];
            var increase = disposal.WashSaleBasisIncreases[index];
            if (recipient.ReplacementTaxLotRecordId == Guid.Empty || !seen.Add(recipient.ReplacementTaxLotRecordId) ||
                canonical.Any(lot => lot.TaxLotRecordId == recipient.ReplacementTaxLotRecordId) ||
                string.IsNullOrWhiteSpace(recipient.ReplacementLotId) ||
                !string.Equals(recipient.ReplacementLotId, increase.ReplacementLotId, StringComparison.OrdinalIgnoreCase) ||
                recipient.Account is null || recipient.Account.AccountType != LedgerAccountType.Asset ||
                recipient.SecurityId == Guid.Empty || recipient.SecurityId != canonical[0].SecurityId ||
                recipient.BookPositionId == Guid.Empty || recipient.DeferredLoss <= 0m ||
                recipient.DeferredLoss != increase.Amount ||
                (increase.ReplacementAccount is not null && increase.ReplacementAccount != recipient.Account))
                throw new LedgerValidationException("Retained wash-sale deferral recipient identity or amount differs from its retained basis increase.");
        }
        return recipients;
    }

    private static bool IsRetainedRecipientDebit(LedgerEntry line,
        IReadOnlyList<LedgerTaxLotDisposalRecipientEvidence> recipients)
        => line.Credit == 0m && line.Debit > 0m && recipients.Any(recipient =>
            line.Account == recipient.Account && line.Dimensions?.InstrumentId == recipient.SecurityId &&
            line.Dimensions?.PositionId == recipient.BookPositionId);

    private static void CertifyDeferredDisposalJournal(LedgerTaxLotDisposalHistoryRecord disposal, JournalEntry entry,
        string functionalCurrency, decimal proceeds, decimal disallowed)
    {
        var assetAccount = disposal.Account;
        var financialAccountId = assetAccount.FinancialAccountId;
        var gainAccount = string.IsNullOrWhiteSpace(financialAccountId)
            ? LedgerAccounts.RealizedGain : LedgerAccounts.RealizedGainFor(financialAccountId);
        var lossAccount = string.IsNullOrWhiteSpace(financialAccountId)
            ? LedgerAccounts.RealizedLoss : LedgerAccounts.RealizedLossFor(financialAccountId);
        bool IsCash(LedgerEntry line) => IsDisposalCash(line, assetAccount, functionalCurrency);
        bool IsReplacementBasis(LedgerEntry line) => !IsCash(line) && line.Account != assetAccount &&
            line.Account.AccountType == LedgerAccountType.Asset && line.Credit == 0m && line.Debit > 0m;

        // Deferrals are retained separately from the atomic journal. Missing rows must not turn
        // a deferred loss into an apparent nonnegative result. Bind the amount to the posting shape
        // as an explicit disposal quote: exact scoped cash, source basis, result, and replacement
        // basis debits. Ambiguous fee or additional-disposal shapes need separate retained evidence.
        if (entry.Lines.Any(line =>
                (line.Account.Name == LedgerAccounts.RealizedGain.Name &&
                    (line.Account != gainAccount || line.Debit != 0m)) ||
                (line.Account.Name == LedgerAccounts.RealizedLoss.Name &&
                    (line.Account != lossAccount || line.Credit != 0m)) ||
                (IsCash(line) && line.Credit != 0m) ||
                (!IsCash(line) && line.Account != assetAccount && line.Account != gainAccount &&
                    line.Account != lossAccount && !IsReplacementBasis(line))))
            throw new LedgerValidationException("Retained wash-sale economics require supported cash, asset-basis, and realized-result journal lines.");

        if (entry.Lines.Where(IsCash).Sum(static line => line.Debit) != proceeds ||
            entry.Lines.Where(IsReplacementBasis).Sum(static line => line.Debit) != disallowed)
            throw new LedgerValidationException("Retained wash-sale deferrals do not reconcile to journal cash proceeds and replacement basis.");

        // A balanced debit to an unrelated asset is not evidence that the retained recipient
        // received the deferred basis. Certify each exact account and security/position amount;
        // multiple recipients sharing that posting scope can legitimately share a journal line.
        var expected = (disposal.DeferralRecipients ?? [])
            .GroupBy(static recipient => (recipient.Account, recipient.SecurityId, recipient.BookPositionId))
            .ToDictionary(static group => group.Key, static group => group.Sum(recipient => recipient.DeferredLoss));
        var actual = entry.Lines.Where(IsReplacementBasis)
            .GroupBy(static line => (line.Account, SecurityId: line.Dimensions?.InstrumentId ?? Guid.Empty,
                BookPositionId: line.Dimensions?.PositionId ?? Guid.Empty))
            .ToDictionary(static group => group.Key, static group => group.Sum(line => line.Debit));
        if (expected.Count != actual.Count || expected.Any(recipient =>
                !actual.TryGetValue(recipient.Key, out var amount) || amount != recipient.Value))
            throw new LedgerValidationException("Retained wash-sale deferrals do not reconcile to the exact replacement recipient journal basis.");
    }

    /// <summary>
    /// Re-runs pooled relief over the retained pre-relief pool (relieved lots plus every survivor
    /// the batch restated) and requires each retained slice to be exactly what the kernel relieves.
    /// </summary>
    private static void CertifyAverageCostRelief(LedgerTaxLotDisposalHistoryRecord disposal, IReadOnlyList<OpenLotDto> canonical)
    {
        var pool = disposal.PoolLots;
        if (pool is null || pool.Count < canonical.Count ||
            canonical.Any(lot => !pool.Any(member => member.TaxLotRecordId == lot.TaxLotRecordId)))
            throw new LedgerValidationException("Average-cost disposal history lacks its retained pre-relief pool; canonical reporting is blocked.");
        if (canonical.Any(lot => !pool.Any(member => SameReliefSnapshot(lot, member))))
            throw new LedgerValidationException("Average-cost canonical lot basis, version, scope, or acquisition facts differ from its retained pre-relief pool snapshot.");
        var scale = canonical[0].Acquisition.QuantityBasis == LotQuantityBasis.Face
            ? LedgerTaxLotFaceValueTerms.LedgerLotParBasis : 1m;
        OpenLotReliefResultDto relief;
        try
        {
            relief = new OpenLotReliefService().Select(pool,
                disposal.Lots.Sum(static lot => lot.Quantity) * scale, OpenLotReliefMethod.AverageCost);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            throw new LedgerValidationException($"Average-cost disposal history cannot be re-relieved: {exception.Message}");
        }

        if (relief.Selections.Count != canonical.Count || relief.Selections.Where((slice, index) =>
                slice.TaxLotRecordId != canonical[index].TaxLotRecordId ||
                slice.Quantity != disposal.Lots[index].Quantity * scale ||
                slice.FunctionalCostBasis != disposal.Lots[index].CostBasis).Any())
            throw new LedgerValidationException("Retained average-cost slices differ from the pooled relief over the retained pool.");
    }

    // The two snapshots can be deserialized independently. Compare authoritative values,
    // including evidence records in sequence, rather than the evidence-list object's identity.
    private static bool SameReliefSnapshot(OpenLotDto lot, OpenLotDto member)
        => lot.TaxLotRecordId == member.TaxLotRecordId &&
           lot.SecurityId == member.SecurityId && lot.BookPositionId == member.BookPositionId &&
           lot.LedgerBookId == member.LedgerBookId && lot.LotId == member.LotId &&
           lot.AcquiredDate == member.AcquiredDate && lot.OriginalQuantity == member.OriginalQuantity &&
           lot.OpenQuantity == member.OpenQuantity && lot.Version == member.Version &&
           lot.OpenTransactionCostBasis == member.OpenTransactionCostBasis &&
           lot.OpenFunctionalCostBasis == member.OpenFunctionalCostBasis &&
           lot.Acquisition is { } acquisition && member.Acquisition is { } poolAcquisition &&
           acquisition.QuantityBasis == poolAcquisition.QuantityBasis &&
           acquisition.AcquisitionCurrency == poolAcquisition.AcquisitionCurrency &&
           acquisition.FunctionalCurrency == poolAcquisition.FunctionalCurrency &&
           acquisition.AcquisitionFxRateToFunctional == poolAcquisition.AcquisitionFxRateToFunctional &&
           acquisition.TransactionCostBasis == poolAcquisition.TransactionCostBasis &&
           acquisition.FunctionalCostBasis == poolAcquisition.FunctionalCostBasis &&
           acquisition.HoldingPeriodStartDate == poolAcquisition.HoldingPeriodStartDate &&
           acquisition.FaceValueTerms == poolAcquisition.FaceValueTerms &&
           SameCorporateActionLineage(acquisition.CorporateActionLineage, poolAcquisition.CorporateActionLineage) &&
           acquisition.Evidence is { } evidence && poolAcquisition.Evidence is { } poolEvidence &&
           evidence.SequenceEqual(poolEvidence);

    private static bool SameCorporateActionLineage(OpenLotCorporateActionLineageDto? left, OpenLotCorporateActionLineageDto? right)
        => left is null ? right is null : right is not null &&
           left.CorporateActionId == right.CorporateActionId && left.ActionType == right.ActionType &&
           left.SourceCorporateActionId == right.SourceCorporateActionId &&
           left.EffectiveDate == right.EffectiveDate && left.PredecessorTaxLotRecordId == right.PredecessorTaxLotRecordId &&
           left.PredecessorVersion == right.PredecessorVersion && left.BasisAllocationPercent == right.BasisAllocationPercent &&
           left.Role == right.Role && left.ReportingTags is { } tags && right.ReportingTags is { } poolTags &&
           tags.SequenceEqual(poolTags, StringComparer.Ordinal);
}
