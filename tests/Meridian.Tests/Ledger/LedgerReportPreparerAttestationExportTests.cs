using System.IO.Compression;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Documents;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Tests.FinancialOperations.Ledger;

namespace Meridian.Tests.Ledger;

public sealed class LedgerReportPreparerAttestationExportTests
{
    [Fact]
    public void FinancialPackRetainsCompactSignedDisclosureAndBothWorkbookRenderersExposeIt()
    {
        var (entry, request) = BuildInputs();
        var pack = Pack(entry, request);
        pack.PreparerAttestations.Should().ContainSingle(item => item.Classification == "PreparerAttestation"
            && !item.IndependentApprovalSatisfied && item.AttestationId == entry.Metadata.PreparerAttestation!.Receipt.AttestationId);
        var sidecar = pack.Artifacts.Single(item => item.Name == "accounting-preparer-attestations.json");
        sidecar.Content.Should().Contain("not independently reviewed").And.Contain("\"independentApprovalSatisfied\":false")
            .And.NotContain("entitlementSnapshotJson").And.NotContain("synthetic\":true");
        pack.Artifacts.Single(item => item.Name == "financial-statements.json").Content.Should().Contain("preparerAttestations");
        pack.Artifacts.Single(item => item.Name == "manifest.csv").Content.Should().Contain(sidecar.ChecksumSha256);
        LedgerReportPresentation.BuildTables(pack).Should().Contain(table => table.Title == "Accounting Preparer Attestations");

        foreach (var renderer in new ILedgerReportBinaryRenderer[] { BuiltInLedgerReportBinaryRenderer.Instance, new FinancialReportDocumentRenderer() })
        {
            using var archive = new ZipArchive(new MemoryStream(renderer.RenderWorkbook(pack)), ZipArchiveMode.Read);
            var allXml = string.Join("\n", archive.Entries.Where(item => item.FullName.EndsWith(".xml", StringComparison.Ordinal)).Select(item =>
            {
                using var reader = new StreamReader(item.Open());
                return reader.ReadToEnd();
            }));
            allXml.Should().Contain("PreparerAttestation").And.Contain("not independently reviewed");
        }
        Encoding.ASCII.GetString(BuiltInLedgerReportBinaryRenderer.Instance.RenderPdf(pack)).Should().Contain("Attested by preparer");
    }

    [Fact]
    public void OpeningBalanceDependencyAndFrozenReplayKeepOriginalDisclosureInEveryScheduledFormat()
    {
        var (entry, originalRequest) = BuildInputs();
        var schedule = new LedgerReportSchedule("native-report", "fund-a", "Native report", LedgerReportScheduleFrequency.Monthly,
            new(2026, 11, 2), 5, "USD", Enum.GetValues<LedgerReportExportFormat>(), ["controller@example.test"], "controller", originalRequest.GeneratedAtUtc);
        var export = new LedgerReportScheduledExport(schedule, "native-report", "2026-11", new(2026, 11, 2),
            new(2026, 11, 30), originalRequest.AsOf, originalRequest.GeneratedAtUtc, Enum.GetValues<LedgerReportExportFormat>(), ["controller@example.test"]);
        var request = export.ToReportPackRequest("controller", originalRequest.GeneratedAtUtc);
        var frozen = JsonSerializer.Serialize(entry);
        var pack = Pack(JsonSerializer.Deserialize<JournalEntry>(frozen)!, request);
        var replay = Pack(JsonSerializer.Deserialize<JournalEntry>(frozen)!, request);
        pack.PreparerAttestations.Should().ContainSingle("prior-period journals still support the current closing balance");
        pack.Signature.Should().Be(replay.Signature);
        var delivered = LedgerScheduledReportExportPackageBuilder.Build(pack, export);
        foreach (var name in new[] { "scheduled-export-financials.csv", "scheduled-export-financials.json", "regulatory-summary.xml" })
            delivered.Single(item => item.Name == name).Content.Should().Contain("not independently reviewed");
        delivered.Single(item => item.Name == "scheduled-export-financials.json").Content.Should().NotContain("entitlementSnapshotJson");
    }

    [Fact]
    public void MalformedNativeClaimStaysUnknownAndLegacyPacksOmitNewExportFields()
    {
        var (entry, request) = BuildInputs();
        var forged = new JournalEntry(entry.JournalEntryId, entry.Timestamp, entry.Description, entry.Lines,
            entry.Metadata with { PreparerAttestation = null });
        var unknown = Pack(forged, request);
        unknown.PreparerAttestations.Should().ContainSingle(item => item.Classification == "Unknown" && !item.IndependentApprovalSatisfied);
        var ordinary = new JournalEntry(entry.JournalEntryId, entry.Timestamp, entry.Description, entry.Lines,
            new JournalEntryMetadata());
        var legacy = Pack(ordinary, request);
        legacy.PreparerAttestations.Should().BeNull();
        legacy.Artifacts.Should().NotContain(item => item.Name == "accounting-preparer-attestations.json");
        legacy.Artifacts.Single(item => item.Name == "financial-statements.json").Content.Should().NotContain("preparerAttestations");
        JsonSerializer.Serialize(legacy).Should().NotContain("PreparerAttestations");
    }

    private static LedgerFinancialReportPack Pack(JournalEntry entry, LedgerReportPackRequest request)
    {
        var ledger = new Meridian.Ledger.Ledger();
        ledger.Post(entry);
        return LedgerReportPackBuilder.Build(ledger, request);
    }

    private static (JournalEntry Entry, LedgerReportPackRequest Request) BuildInputs()
    {
        var write = AccountingPostingCommandValidator.NormalizeAndValidate(CashDividendPreparerPostingContractTests.BuildWrite(), true, true, true);
        var binding = write.Entry.Metadata.PreparerAttestation!;
        var tags = new Dictionary<string, string>(write.Entry.Metadata.Tags!, StringComparer.OrdinalIgnoreCase)
        {
            ["assetAccountingDraftFingerprint"] = binding.DraftedCandidateFingerprint,
            ["assetAccountingDraftResultFingerprint"] = new string('e', 64)
        };
        var entry = new JournalEntry(write.Entry.JournalEntryId, write.Entry.Timestamp, write.Entry.Description,
            write.Entry.Lines, write.Entry.Metadata with { Tags = tags });
        return (entry, new("native-report", "fund-a", "2026-11", new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero),
            new(2026, 11, 30, 23, 59, 59, TimeSpan.Zero), new(2026, 11, 12, 0, 0, 0, TimeSpan.Zero), "USD", "controller",
            new(2026, 11, 12, 1, 0, 0, TimeSpan.Zero)));
    }
}
