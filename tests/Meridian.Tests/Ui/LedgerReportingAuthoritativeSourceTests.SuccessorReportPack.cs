using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Services;
using Xunit;
using Meridian.Storage.Ledger;

namespace Meridian.Tests.Ui;

public sealed partial class LedgerReportingAuthoritativeSourceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CaptureAsync_SuccessorReportPackRetainsImmutableOriginAndSignsItsArtifact(bool advanceRefunding)
    {
        var fixture = CreateFixture(successors: true,
            accountingBasis: advanceRefunding ? AccountingBasisKindDto.Statutory : AccountingBasisKindDto.Gaap);
        fixture.Parameters = fixture.Parameters with { OutputFormat = ReportingOutputFormatDto.Pdf };
        var batch = WithSuccessorOrigins(SuccessorBatch(fixture, advanceRefunding));
        fixture.JournalStore.Records.Add(batch.Journal);
        fixture.JournalStore.Successors.Add(batch);

        var capture = await fixture.Source.CaptureAsync(fixture.Parameters, fixture.Access, new ReportingAuthoritativeSourceCaptureIntent("capital-account-statement"));

        var pack = capture.CertifiedLedgerPresentation!.ReportPack;
        var artifact = pack.Artifacts.Single(item => item.Name == "corporate-action-lot-evidence.json");
        artifact.ChecksumSha256.Should().Be(Sha256Digest.ComputeUtf8(artifact.Content));
        using var json = JsonDocument.Parse(artifact.Content);
        var report = json.RootElement.EnumerateArray().Single();
        report.GetProperty("PredecessorAfter").GetProperty("OpenQuantity").GetDecimal().Should().Be(0m);
        report.GetProperty("Successors").EnumerateArray().Should().OnlyContain(lot =>
            lot.GetProperty("Acquisition").GetProperty("CorporateActionLineage").GetProperty("PredecessorTaxLotRecordId").GetGuid()
                == batch.CorporateAction!.ExpectedLot.TaxLotRecordId);
        var signedPayload = string.Join("\n", pack.Artifacts.OrderBy(item => item.Name, StringComparer.Ordinal)
            .Select(item => $"{item.Name}:{item.ChecksumSha256}"));
        pack.Signature.PayloadChecksumSha256.Should().Be(Sha256Digest.ComputeUtf8(signedPayload));
    }

    [Fact]
    public async Task CaptureAsync_AlteredImmutableSuccessorOriginBlocksCertifiedArtifact()
    {
        var fixture = CreateFixture(successors: true);
        fixture.Parameters = fixture.Parameters with { OutputFormat = ReportingOutputFormatDto.Pdf };
        var batch = WithSuccessorOrigins(SuccessorBatch(fixture, advanceRefunding: false));
        var successor = batch.Mutations.Single(item => item.LotBefore is null);
        var changed = successor.LotAfter with
        {
            Acquisition = successor.LotAfter.Acquisition! with
            {
                CorporateActionLineage = successor.LotAfter.Acquisition!.CorporateActionLineage! with { PredecessorVersion = 99 }
            }
        };
        var mutations = batch.Mutations.Select(item => item.MutationRecordId == successor.MutationRecordId ? item with { LotAfter = changed } : item).ToArray();
        fixture.JournalStore.Records.Add(batch.Journal);
        fixture.JournalStore.Successors.Add(batch with { Mutations = mutations, MutatedLots = mutations.Select(item => item.LotAfter).ToArray() });

        var capture = () => fixture.Source.CaptureAsync(fixture.Parameters, fixture.Access, new ReportingAuthoritativeSourceCaptureIntent("capital-account-statement")).AsTask();

        await capture.Should().ThrowAsync<ReportingAuthoritativeSourceUnavailableException>();
    }

    private static AtomicTaxLotJournalResult WithSuccessorOrigins(AtomicTaxLotJournalResult batch)
    {
        var mutations = batch.Mutations.Select(item => item.LotBefore is not null ? item : item with
        {
            LotAfter = item.LotAfter with
            {
                Acquisition = OpenLotSuccessors.WithLineage(batch.CorporateAction!, item.LotAfter.ToOpenLot()).Acquisition
            }
        }).ToArray();
        return batch with { Mutations = mutations, MutatedLots = mutations.Select(item => item.LotAfter).ToArray() };
    }
}
