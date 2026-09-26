using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.Integrity;
using Meridian.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.Tests.Storage;

public sealed class OpenLotBackfillReconciliationTests
{
    [Fact]
    public void LegacySurvey_ReportsMissingFactsWithoutInferringUnitsOrSameCurrencyFx()
    {
        var legacy = Legacy();
        OpenLotBackfillRules.Issues(legacy).Should().BeEquivalentTo(
            "MissingSecurityIdentity", "MissingBookPositionIdentity", "MissingAcquisitionQuantityBasis", "MissingAcquisitionCurrencyFxEvidence");
        legacy.Acquisition.Should().BeNull();
        legacy.SecurityId.Should().BeEmpty();
    }

    [Theory]
    [InlineData("QuantityBasis")]
    [InlineData("AcquisitionFxRateToFunctional")]
    [InlineData("FunctionalCurrency")]
    [InlineData("SecurityMasterVersion")]
    public void RetainedSource_RequiresExplicitFactsInsteadOfJsonDefaults(string missing)
    {
        var json = JsonSerializer.SerializeToNode(Facts(Legacy()))!.AsObject();
        json.Remove(missing);
        var content = JsonSerializer.SerializeToUtf8Bytes(json);
        var read = () => OpenLotBackfillRules.ReadFacts(content, Sha256Digest.Compute(content));
        read.Should().Throw<LedgerValidationException>().WithMessage("*valid typed JSON*");
    }

    [Fact]
    public void RetainedSource_VerifiesActualBytesBeforePromotingFacts()
    {
        var content = JsonSerializer.SerializeToUtf8Bytes(Facts(Legacy()));
        var read = () => OpenLotBackfillRules.ReadFacts(content, new string('a', 64));
        read.Should().Throw<LedgerValidationException>().WithMessage("*SHA-256*");
    }

    [Fact]
    public void GovernedEnrichment_RestoresCanonicalReadinessAndReconcilesPartialForeignCurrencyLot()
    {
        var legacy = Legacy() with { OpenQuantity = 4.25m };
        var evidence = Evidence(Facts(legacy));
        var enriched = OpenLotBackfillRules.Enrich(legacy, evidence);
        enriched.OriginalQuantity.Should().Be(legacy.OriginalQuantity);
        enriched.OpenQuantity.Should().Be(legacy.OpenQuantity);
        enriched.UnitCost.Should().Be(legacy.UnitCost);
        enriched.Currency.Should().Be(legacy.Currency);
        var canonical = enriched.ToOpenLot();
        canonical.OpenTransactionCostBasis.Should().Be(425m);
        canonical.OpenFunctionalCostBasis.Should().Be(467.5m);
        canonical.Acquisition.AcquisitionFxRateToFunctional.Should().Be(1.1m);
        canonical.Acquisition.Evidence.Should().ContainSingle().Which.ContentHashSha256.Should().Be(evidence.ContentHashSha256);
        OpenLotBackfillRules.Issues(enriched).Should().BeEmpty();
    }

    [Fact]
    public void FunctionalCurrencyDurableRows_KeepLegacyFunctionalCostAndProjectTransactionBasisSeparately()
    {
        var legacy = Legacy() with { Currency = "USD", UnitCost = 110m, OpenQuantity = 5m };
        var facts = Facts(legacy) with
        {
            AcquisitionCurrency = "EUR",
            TransactionCostBasis = 1000m,
            FunctionalCostBasis = 1100m,
            AcquisitionFxRateToFunctional = 1.1m
        };
        var canonical = OpenLotBackfillRules.Enrich(legacy, Evidence(facts)).ToOpenLot();
        canonical.OpenTransactionCostBasis.Should().Be(500m);
        canonical.OpenFunctionalCostBasis.Should().Be(550m);
        var unexplained = () => OpenLotBackfillRules.Enrich(legacy with { UnitCost = 111m }, Evidence(facts));
        unexplained.Should().Throw<LedgerValidationException>().WithMessage("*reconcile*");
    }

    [Fact]
    public void SameCurrencyFacts_RequireExplicitUnitFxAndExactBasisAgreement()
    {
        var legacy = Legacy() with { Currency = "USD" };
        var facts = Facts(legacy) with
        {
            AcquisitionCurrency = "USD",
            FunctionalCurrency = "USD",
            AcquisitionFxRateToFunctional = 1m,
            TransactionCostBasis = 1000m,
            FunctionalCostBasis = 1000m
        };
        OpenLotBackfillRules.Enrich(legacy, Evidence(facts)).ToOpenLot().OpenFunctionalCostBasis.Should().Be(1000m);
        var drift = () => OpenLotBackfillRules.Enrich(legacy, Evidence(facts with { FunctionalCostBasis = 1000.001m }));
        drift.Should().Throw<ArgumentException>().WithMessage("*agree exactly*");
        var absentFx = () => OpenLotBackfillRules.Enrich(legacy, Evidence(facts with { AcquisitionFxRateToFunctional = 0m }));
        absentFx.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Backfill_RejectsUnreviewedSelfReviewedOrIncorrectlyScopedEvidence()
    {
        var legacy = Legacy();
        var accepted = Evidence(Facts(legacy));
        var pending = () => OpenLotBackfillRules.Enrich(legacy, accepted with { ReviewStatus = "Pending", Version = 1 });
        var self = () => OpenLotBackfillRules.Enrich(legacy, accepted with { ReviewedBy = accepted.RetainedBy.ToUpperInvariant() });
        var differentLot = () => OpenLotBackfillRules.Enrich(legacy, accepted with { Facts = accepted.Facts with { TaxLotRecordId = Guid.NewGuid() } });
        pending.Should().Throw<LedgerValidationException>().WithMessage("*independent accepted review*");
        self.Should().Throw<LedgerValidationException>().WithMessage("*independent accepted review*");
        differentLot.Should().Throw<LedgerValidationException>().WithMessage("*different lot*");
        legacy.Acquisition.Should().BeNull();
    }

    [Fact]
    public void Backfill_CannotReplaceExistingIdentityOrEstablishedAcquisitionEvidence()
    {
        var legacy = Legacy();
        var evidence = Evidence(Facts(legacy));
        var wrongIdentity = () => OpenLotBackfillRules.Enrich(legacy with { SecurityId = Guid.NewGuid() }, evidence);
        wrongIdentity.Should().Throw<LedgerValidationException>().WithMessage("*cannot replace*");
        var canonical = OpenLotBackfillRules.Enrich(legacy, evidence);
        var second = () => OpenLotBackfillRules.Enrich(canonical, evidence);
        second.Should().Throw<LedgerValidationException>().WithMessage("*cannot be backfilled again*");
    }

    [Fact]
    public void FaceBackfill_RequiresRetainedFactorAndParConventionAndConservesBasis()
    {
        var legacy = Legacy();
        var facts = Facts(legacy) with
        {
            QuantityBasis = LotQuantityBasis.Face,
            OriginalFace = 1000m,
            FaceValueTerms = new(100m, 0.8m, BondAmortizationMethod.ConstantYield, 0.04m)
        };
        var canonical = OpenLotBackfillRules.Enrich(legacy, Evidence(facts)).ToOpenLot();
        canonical.OriginalQuantity.Should().Be(1000m);
        canonical.OpenTransactionCostBasis.Should().Be(1000m);
        canonical.Acquisition.FaceValueTerms!.BookedFactor.Should().Be(0.8m);
        var incomplete = () => OpenLotBackfillRules.Enrich(legacy, Evidence(facts with { FaceValueTerms = null }));
        incomplete.Should().Throw<LedgerValidationException>();
    }

    internal static LedgerTaxLotRecord Legacy()
        => OpenLotConvergenceTests.Lot(1) with { Acquisition = null, SecurityId = Guid.Empty, BookPositionId = Guid.Empty };

    internal static OpenLotBackfillFactsDto Facts(LedgerTaxLotRecord lot)
        => new(lot.LedgerBookId, lot.TaxLotRecordId, Guid.NewGuid(), 3, Guid.NewGuid(), 5,
            lot.AcquiredDate, lot.OriginalQuantity, lot.UnitCost, lot.Currency,
            LotQuantityBasis.Units, "EUR", "USD", 1.1m, 1000m, 1100m, lot.AcquiredDate, null, null);

    internal static OpenLotBackfillEvidenceDto Evidence(OpenLotBackfillFactsDto facts)
    {
        var content = JsonSerializer.SerializeToUtf8Bytes(facts);
        var now = DateTimeOffset.Parse("2026-09-04T10:00:00Z");
        return new(Guid.NewGuid(), facts.LedgerBookId, facts.TaxLotRecordId, facts,
            "custodian", "statement:2026-01", "evidence://custodian/acquisition", Sha256Digest.Compute(content),
            "preparer", now.AddMinutes(-10), 2, "Accepted", "reviewer", now, "Verified acquisition source and identity mapping.");
    }
}
