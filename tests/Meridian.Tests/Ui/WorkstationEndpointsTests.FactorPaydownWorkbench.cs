using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Api;
using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.Ledger;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    [Fact]
    public async Task FactorPaydownDraft_IsDiscoverableAfterRestartAndSubmitsWithImmutableLotAuthority()
    {
        var directory = Path.Combine(Path.GetTempPath(), "meridian-factor-workbench-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var fixture = FactorPreparationFixture();
            var draftPath = Path.Combine(directory, "drafts.json");
            var spine = Substitute.For<IAssetAccountingEventSpineService>();
            AssetAccountingEventSpineDto? projected = null;
            AssetAccountingPostingCandidateDto? result = null;
            spine.GetLatestAsync(Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(_ => result?.Spine);
            spine.ProjectAsync(Arg.Any<ProjectAssetAccountingEventRequestDto>(), Arg.Any<CancellationToken>()).Returns(call =>
                new AssetAccountingEventSpineAppendResultDto(projected = FactorProjectedSpine(call.Arg<ProjectAssetAccountingEventRequestDto>()), false));
            spine.BuildPostingCandidateAsync(Arg.Any<AssetAccountingPostingCandidateRequestDto>(), Arg.Any<CancellationToken>())
                .Returns(_ => result = FactorDraftedCandidate(projected!, "ops-user", "Reviewed paydown"));
            await using var app = await CreateAppAsync(services =>
            {
                fixture.Register(services);
                services.AddSingleton<IManualJournalEntryDraftStore>(new FileManualJournalEntryDraftStore(draftPath));
                services.AddSingleton(spine);
            }, currentUserPermissions: UserPermission.ManageLedgerReports, currentUserCompanyId: "company-alpha", mapLedgerApi: true);
            var client = app.GetTestClient();
            using var previewResponse = await client.PostAsJsonAsync(FactorPreparationRoute(fixture.Request), fixture.Request, ServerJsonOptions);
            using var preview = JsonDocument.Parse(await previewResponse.Content.ReadAsStringAsync());
            var command = new CreateFactorPaydownDraftRequestDto(fixture.Request,
                preview.RootElement.GetProperty("preparationFingerprint").GetString()!, "Reviewed paydown");

            using var response = await client.PostAsJsonAsync(FactorDraftRoute(fixture.Request), command, ServerJsonOptions);
            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            var store = new FileManualJournalEntryDraftStore(draftPath);
            var restarted = new ManualJournalEntryWorkbenchService(store,
                app.Services.GetRequiredService<IAccountingConfigurationService>(), new InMemoryAccountingActionAuditStore(), journalStore: fixture.Periods);
            var workbench = await restarted.GetWorkbenchAsync(fixture.Request.FundProfileId, fixture.Request.LedgerBookId,
                tenantId: "company-alpha", companyId: "company-alpha");
            var draft = workbench.Drafts.Should().ContainSingle().Subject;
            draft.JournalEntryId.Should().Be(result!.Candidate.JournalEntryId!.Value);
            draft.AssetAccountingDraft.Should().BeEquivalentTo(result);
            draft.Status.Should().Be(ManualJournalEntryStatusDto.Draft);
            using var queueResponse = await client.GetAsync(
                $"{UiApiRoutes.LedgerManualJournalEntryWorkbench}?fundProfileId={Uri.EscapeDataString(fixture.Request.FundProfileId)}&ledgerBookId={fixture.Request.LedgerBookId:D}");
            queueResponse.StatusCode.Should().Be(HttpStatusCode.OK, await queueResponse.Content.ReadAsStringAsync());
            using var queueJson = JsonDocument.Parse(await queueResponse.Content.ReadAsStringAsync());
            var wireDraft = queueJson.RootElement.GetProperty("drafts").EnumerateArray().Single(item =>
                item.GetProperty("journalEntryId").GetGuid() == draft.JournalEntryId);
            wireDraft.GetProperty("assetAccountingDraft").ValueKind.Should().Be(JsonValueKind.Object,
                "the browser relies on this retained marker to disable generic edits, approval and posting");
            wireDraft.GetProperty("assetAccountingDraft").GetProperty("spine").GetProperty("eventId").GetGuid()
                .Should().Be(result.Spine.EventId);
            var clientRevalidation = await restarted.ValidateDraftAsync(new ValidateManualJournalEntryDraftRequest(
                draft with { AssetAccountingDraft = null, Lines = [draft.Lines[0] with { Amount = 1m }] }, "ops-user",
                LedgerBookId: draft.LedgerBookId, TenantId: draft.TenantId, CompanyId: draft.CompanyId));
            clientRevalidation.AssetAccountingDraft.Should().BeEquivalentTo(result);
            clientRevalidation.Lines.Should().BeEquivalentTo(draft.Lines,
                "browser validation resolves the exact retained source instead of roundtripping authoritative decimals");
            var forgedIntroduction = () => restarted.SaveDraftAsync(new SaveManualJournalEntryDraftRequest(
                draft with { JournalEntryId = Guid.NewGuid(), Version = 0 }, "ops-user",
                LedgerBookId: draft.LedgerBookId, TenantId: draft.TenantId, CompanyId: draft.CompanyId));
            await forgedIntroduction.Should().ThrowAsync<InvalidOperationException>().WithMessage("*governed server intake*");
            using var retry = await client.PostAsJsonAsync(FactorDraftRoute(fixture.Request), command, ServerJsonOptions);
            retry.StatusCode.Should().Be(HttpStatusCode.OK, await retry.Content.ReadAsStringAsync());
            (await store.ListAsync(fixture.Request.FundProfileId, fixture.Request.LedgerBookId,
                tenantId: "company-alpha", companyId: "company-alpha")).Should().ContainSingle();
            await spine.Received(1).BuildPostingCandidateAsync(Arg.Any<AssetAccountingPostingCandidateRequestDto>(), Arg.Any<CancellationToken>());

            var submitted = await restarted.SubmitApprovalAsync(new SubmitManualJournalEntryApprovalRequest(draft.JournalEntryId,
                draft.FundProfileId, "ops-user", draft.Version, CorrelationId: "factor-submit",
                LedgerBookId: draft.LedgerBookId, TenantId: draft.TenantId, CompanyId: draft.CompanyId));
            submitted.Status.Should().Be(ManualJournalEntryStatusDto.Submitted);
            submitted.AssetAccountingDraft.Should().BeEquivalentTo(result);
            submitted.AssetAccountingDraft!.Spine.DraftedLotMutation!.FactorPaydown.Should().BeEquivalentTo(result.Spine.FactorPaydown);
            var strippingSource = () => restarted.SaveDraftAsync(new SaveManualJournalEntryDraftRequest(
                submitted with { AssetAccountingDraft = null, Memo = "Changed economics" }, "ops-user",
                LedgerBookId: draft.LedgerBookId, TenantId: draft.TenantId, CompanyId: draft.CompanyId));
            await strippingSource.Should().ThrowAsync<InvalidOperationException>().WithMessage("*immutable*");
            foreach (var action in new[] { JournalEntryLifecycleActionDto.Approve, JournalEntryLifecycleActionDto.Post,
                JournalEntryLifecycleActionDto.Reverse, JournalEntryLifecycleActionDto.Rebook })
            {
                var act = () => restarted.ApplyLifecycleActionAsync(new JournalEntryLifecycleActionRequestDto(
                    submitted.JournalEntryId, submitted.FundProfileId, action, "independent-controller", submitted.Version,
                    LedgerBookId: submitted.LedgerBookId, TenantId: submitted.TenantId, CompanyId: submitted.CompanyId));
                await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*governed asset posting workflow*");
            }
        }
        finally { Directory.Delete(directory, true); }
    }
}
