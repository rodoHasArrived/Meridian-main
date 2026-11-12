using System.Net.Http.Json;
using FluentAssertions;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Operations;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Strategies.Interfaces;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    [Fact]
    public async Task SecurityInstrumentExplorerRetainsNativePreparerPostedJournalWithoutIndependentApproval()
    {
        await using var app = await CreateAppAsync(RegisterFinancialRecordExplorerTestServices,
            currentUserPermissions: ExplorerOperatorPermissions);
        await app.Services.GetRequiredService<IStrategyRepository>()
            .RecordRunAsync(BuildActivePaperRun("financial-record-explorer-security-run", withBreaks: false));
        var source = app.Services.GetRequiredService<FinancialRecordExplorerAssetAccountingEventSpineService>();
        var journals = app.Services.GetRequiredService<FinancialRecordExplorerJournalStore>();
        var originalSpine = (await source.GetLatestAsync(FinancialRecordExplorerEventId, 1))!;
        var originalJournal = (await journals.QueryAsync(new())).Single();
        var (spine, journal) = BuildNativeExplorerProof(originalSpine, originalJournal);
        AssetAccountingEventSpineValidator.Validate(spine).Should().BeEmpty();
        source.OverrideSpine = spine;
        journals.OverrideRecords = [journal];

        var explorer = (await app.GetTestClient().GetFromJsonAsync<FinancialRecordExplorerDto>(
            "/api/workstation/financial-record-explorers/security-instrument", ServerJsonOptions))!;
        var row = explorer.Rows.Single(item => item.RecordId == $"security:{FinancialRecordExplorerAaplSecurityId:D}");
        row.Detail.Fields.Should().Contain(field => field.Label == "Attested by preparer (single-principal)");
        row.Detail.Fields.Should().Contain(field => field.Label == "Posted Journal");
        row.Detail.Fields.Should().NotContain(field => field.Label == "Approved");
        row.Detail.Impacts.Should().Contain(item => item.RelationshipId == "preparer-attestation"
            && item.Description.Contains("not independent", StringComparison.OrdinalIgnoreCase));
        row.Detail.Impacts.Should().NotContain(item => item.RelationshipId == "approval");
        explorer.RecordGraph.Edges.Should().Contain(edge => edge.Label == "attests as preparer");
    }

    [Fact]
    public async Task SecurityInstrumentExplorerCannotBindPostedJournalWithAnotherNativeReceipt()
    {
        await using var app = await CreateAppAsync(RegisterFinancialRecordExplorerTestServices,
            currentUserPermissions: ExplorerOperatorPermissions);
        await app.Services.GetRequiredService<IStrategyRepository>()
            .RecordRunAsync(BuildActivePaperRun("financial-record-explorer-security-run", withBreaks: false));
        var source = app.Services.GetRequiredService<FinancialRecordExplorerAssetAccountingEventSpineService>();
        var journals = app.Services.GetRequiredService<FinancialRecordExplorerJournalStore>();
        var (spine, journal) = BuildNativeExplorerProof((await source.GetLatestAsync(FinancialRecordExplorerEventId, 1))!,
            (await journals.QueryAsync(new())).Single());
        source.OverrideSpine = spine;
        journals.OverrideRecords = [AccountingPreparerAttestationTestData.WithMetadata(journal, journal.Entry.Metadata with
        {
            PreparerAttestation = journal.Entry.Metadata.PreparerAttestation! with
            { Receipt = journal.Entry.Metadata.PreparerAttestation.Receipt with { AttestationId = Guid.NewGuid() } }
        })];
        var explorer = (await app.GetTestClient().GetFromJsonAsync<FinancialRecordExplorerDto>(
            "/api/workstation/financial-record-explorers/security-instrument", ServerJsonOptions))!;
        var row = explorer.Rows.Single(item => item.RecordId == $"security:{FinancialRecordExplorerAaplSecurityId:D}");
        row.Detail.Fields.Should().NotContain(field => field.Label == "Posted Journal" || field.Label == "Approved");
        row.Detail.UsedIn.Should().NotContain(item => item.RelationshipId == "instrument-journal-proof");
    }

    private static (AssetAccountingEventSpineDto Spine, LedgerJournalEntryRecord Journal) BuildNativeExplorerProof(
        AssetAccountingEventSpineDto spine, LedgerJournalEntryRecord journal)
    {
        var native = AccountingPreparerAttestationTestData.Attach(journal,
            new("tenant-test", "tenant-test", spine.Scope.FundProfileId, spine.Scope.LedgerBookId, spine.Scope.PeriodId));
        var candidate = spine.DraftedCandidate! with { Actor = "preparer" };
        var fingerprint = AssetAccountingEventSpineValidator.CanonicalPayloadFingerprint(candidate);
        var binding = native.Entry.Metadata.PreparerAttestation! with
        { EventId = spine.EventId, EventVersion = spine.EventVersion, DraftedCandidateFingerprint = fingerprint };
        var tags = new Dictionary<string, string>(journal.Entry.Metadata.Tags!, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in native.Entry.Metadata.Tags!)
            tags[pair.Key] = pair.Value;
        tags["sourceEventId"] = spine.EventId.ToString("D");
        tags["sourceEventVersion"] = spine.EventVersion.ToString(System.Globalization.CultureInfo.InvariantCulture);
        tags["assetAccountingDraftFingerprint"] = fingerprint;
        tags["assetAccountingDraftResultFingerprint"] = spine.DraftedCandidateResultFingerprint!;
        native = AccountingPreparerAttestationTestData.WithMetadata(native,
            native.Entry.Metadata with { PreparerAttestation = binding, Tags = tags }) with
        { SourceEventId = spine.EventId };
        var stage = new AssetAccountingStageEvidenceDto(AssetAccountingLifecycleStageDto.PreparerAttested,
            binding.Receipt.AttestedAtUtc, binding.Receipt.AttestedBy, spine.RetainedEvidence,
            PreparerAttestationApprovalBoundary.ReceiptReference(binding.Receipt.AttestationId));
        var impact = spine.PostedJournalImpact! with
        {
            Lines = native.Entry.Lines.Select(line => new PostedJournalImpactLineDto(line.EntryId, line.Account.ToString(),
                line.Debit, line.Credit, "USD", Dimensions: spine.Scope.Dimensions)).ToArray()
        };
        return (spine with
        {
            Scope = spine.Scope with { TenantId = "tenant-test", CompanyId = "tenant-test" },
            DraftedCandidate = candidate,
            DraftedCandidateFingerprint = fingerprint,
            PreparerAttestation = binding,
            Stages = [.. spine.Stages.Take(3), stage, spine.Stages[^1] with { Evidence = spine.RetainedEvidence }],
            PostedJournalImpact = impact
        }, native);
    }
}
