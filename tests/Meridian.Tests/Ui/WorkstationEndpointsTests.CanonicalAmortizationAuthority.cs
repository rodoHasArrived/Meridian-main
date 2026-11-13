using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.FinancialOperations.Ledger;
using Meridian.Identity.Auth;
using Meridian.Storage.AssetOperations;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CanonicalAmortizationDraft_RealSpineRetainsOccurrenceAndAllDimensionsAndRecoversExactRetry(bool interruptAfterExpected)
    {
        var fixture = await CanonicalAmortizationFixture();
        var original = (await fixture.Positions.GetSecurityAsync(fixture.Security.SecurityId)).BookPositions.Single();
        var policies = new AccountingPolicyService();
        var eventType = AssetAccountingEventTypeNames.For(AssetAccountingEventKindDto.DepreciationAmortization);
        await policies.CreatePolicyAsync(new CreateAccountingPolicyRequest(AccountingBasisKindDto.Gaap, "amort-policy", "1",
            "Amortization policy", original.EffectiveFrom, RulePack: new AccountingPolicyRulePackDto("amort-pack", "1",
                [new AccountingPolicyRuleDto("amort-rule", AccountingTreatmentKindDto.General, "1", SourceEventType: eventType,
                    RequiresEvidence: true, RequiresApproval: true, AllowsAutoPosting: false)])));
        var configuration = new AccountingConfigurationService(new InMemoryAccountingConfigurationStore(), new InMemoryAccountingActionAuditStore(), fixture.Books);
        foreach (var node in new[] { new ChartOfAccountsNodeDto("asset", fixture.Request.AssetAccountPath, "Investments", "Asset"),
                     new ChartOfAccountsNodeDto("income", fixture.Request.IncomeAccountPath, "Amortization", "Revenue") })
            await configuration.UpsertChartNodeAsync(new UpsertChartOfAccountsNodeRequest("fund-alpha", node, "controller",
                CompanyId: "company-alpha", LedgerBookId: fixture.Request.LedgerBookId, TenantId: "company-alpha"));
        await configuration.UpsertPostingRuleAsync(new UpsertPostingRuleRequest("fund-alpha",
            new PostingRuleDto("amort-rule", "Amortization", eventType, "generated", "1", EffectiveFrom: original.EffectiveFrom, Priority: 100,
                Formulas: [new AccountingRuleFormulaDto("amount", AccountingRuleFormulaKindDto.SourceAmount, 0m)],
                GeneratedPostings: [new GeneratedPostingLineDto("asset", fixture.Request.AssetAccountPath, AccountingTemplateLineSideDto.Credit, "amount", 0m, "USD"),
                    new GeneratedPostingLineDto("income", fixture.Request.IncomeAccountPath, AccountingTemplateLineSideDto.Debit, "amount", 0m, "USD")]), "controller",
            CompanyId: "company-alpha", LedgerBookId: fixture.Request.LedgerBookId, TenantId: "company-alpha"));
        var securities = Substitute.For<ISecurityMasterQueryService>();
        securities.GetRecordedByIdAsOfAsync(fixture.Security.SecurityId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(fixture.Security.ToDetail());
        var candidateBuilder = new AccountingPostingCandidateService(configuration,
            new AccountingJournalDraftService(policies, new AccountingBasisProjectionService(policies)), fixture.Books, policies, taxLotStore: fixture.Periods);
        var eventStore = new InterruptAmortizationProjectionStore(fixture.Positions, interruptAfterExpected);
        var spine = new AssetAccountingEventSpineService(eventStore, fixture.Positions, securities, fixture.Books, policies,
            configuration, candidateBuilder, fixture.Periods);
        await using var app = await CreateAppAsync(services =>
        {
            fixture.Register(services);
            services.AddSingleton<IAccountingConfigurationService>(configuration);
            services.AddSingleton<IAssetAccountingEventSpineService>(spine);
        }, currentUserPermissions: UserPermission.ManageLedgerReports, currentUserCompanyId: "company-alpha", mapLedgerApi: true);
        var client = app.GetTestClient();
        using var previewResponse = await client.PostAsJsonAsync(AmortizationRoute(fixture.Request), fixture.Request, ServerJsonOptions);
        previewResponse.StatusCode.Should().Be(HttpStatusCode.OK, await previewResponse.Content.ReadAsStringAsync());
        using var preview = JsonDocument.Parse(await previewResponse.Content.ReadAsStringAsync());
        var command = new CreateCanonicalAmortizationDraftRequestDto(fixture.Request,
            preview.RootElement.GetProperty("preparationFingerprint").GetString()!, "Reviewed amortization occurrence");
        (await fixture.Positions.GetBookPositionAsync(original.PositionId))!.Version.Should().Be(1, "preparation is read-only");
        preview.RootElement.GetProperty("instruction").GetProperty("expectedBookPositionVersion").GetInt64().Should().Be(2);
        if (interruptAfterExpected)
        {
            using var interrupted = await client.PostAsJsonAsync(AmortizationRoute(fixture.Request, true), command, ServerJsonOptions);
            interrupted.StatusCode.Should().Be(HttpStatusCode.Conflict);
            var expectation = await spine.GetLatestAsync(Guid.Parse(fixture.Request.EventEvidence.SubjectId), 1);
            expectation!.SpineVersion.Should().Be(1);
            using var changed = await client.PostAsJsonAsync(AmortizationRoute(fixture.Request, true), command with { Description = "Different review" }, ServerJsonOptions);
            changed.StatusCode.Should().Be(HttpStatusCode.Conflict, "a retry cannot rewrite the retained Expected review");
        }
        using var response = await client.PostAsJsonAsync(AmortizationRoute(fixture.Request, true), command, ServerJsonOptions);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var store = app.Services.GetRequiredService<IManualJournalEntryDraftStore>();
        var draft = (await store.ListAsync("fund-alpha", fixture.Request.LedgerBookId, tenantId: "company-alpha", companyId: "company-alpha")).Should().ContainSingle().Subject;
        var retained = draft.AssetAccountingDraft!.Spine;
        retained.Scope.ExpectedBookPositionVersion.Should().Be(2);
        retained.DraftedLotMutation!.Amortization!.ExpectedBookPositionVersion.Should().Be(2);
        retained.Scope.Dimensions.Should().BeEquivalentTo(original.BookContext.Dimensions);
        retained.ProjectedEffect!.Lines.Should().OnlyContain(line => line.Dimensions!.EntityId == "entity-1" && line.Dimensions.AccountId == "account-1"
            && line.Dimensions.CostCenterId == "fixed-income" && line.Dimensions.PortfolioId == "portfolio-1");
        retained.DraftedCandidateResult!.GeneratedPostingLines.Select(line => line.Dimensions).Should().OnlyContain(dim => dim!.EntityId == "entity-1" && dim.AccountId == "account-1");
        var current = (await fixture.Positions.GetBookPositionAsync(original.PositionId))!;
        current.Version.Should().Be(2);
        current.OriginEvent.Should().BeEquivalentTo(original.OriginEvent);
        current.CurrentEconomicState.Should().BeEquivalentTo(original.CurrentEconomicState);
        var snapshot = await fixture.Positions.GetSecurityAsync(original.SecurityId);
        snapshot.ProjectionLineages.Should().Contain(item => item.TriggerEvent.EventId == retained.EventId);
        using var replay = await client.PostAsJsonAsync(AmortizationRoute(fixture.Request, true), command, ServerJsonOptions);
        replay.StatusCode.Should().Be(HttpStatusCode.OK, await replay.Content.ReadAsStringAsync());
        (await fixture.Positions.GetBookPositionAsync(original.PositionId))!.Version.Should().Be(2, "exact retries do not retain a second position mutation");
        (await store.ListAsync("fund-alpha", fixture.Request.LedgerBookId, tenantId: "company-alpha", companyId: "company-alpha")).Should().ContainSingle();

        // A caller cannot authorize another occurrence by copying a valid prepared projection.
        var forgedId = Guid.NewGuid();
        var forgedEvidence = retained.RetainedEvidence.Select(item => item.EvidenceId == fixture.Request.EventEvidence.EvidenceId
            ? item with { SubjectId = forgedId.ToString("D"), ContentHashSha256 = new string('f', 64) } : item).ToArray();
        var forgedEvent = retained.EconomicEvent with { EventId = forgedId, RetainedEvidence = forgedEvidence, SourceContentHash = new string('f', 64) };
        var forgedLineage = retained.ProjectionLineage with { ProjectionRunId = forgedId, TriggerEvent = forgedEvent, RetainedEvidence = forgedEvidence };
        var forged = new ProjectAssetAccountingEventRequestDto(retained.EventKind, retained.Scope, forgedEvent, forgedLineage,
            retained.ProjectedEffect with { ProjectionRunId = forgedId }, retained.EventAmount, retained.Currency, 1,
            "ops-user", DateTimeOffset.UtcNow, forgedEvidence);
        await spine.Invoking(service => service.ProjectAsync(forged)).Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*exact authoritative economic-event*");
    }

    private sealed class InterruptAmortizationProjectionStore(IAssetAccountingEventProjectionStore inner, bool interrupt) : IAssetAccountingEventProjectionStore
    {
        private bool _interrupt = interrupt;
        public Task<AssetAccountingEventProjectionRecord?> GetAsync(Guid eventId, long eventVersion, long spineVersion, CancellationToken ct = default)
            => inner.GetAsync(eventId, eventVersion, spineVersion, ct);
        public Task<AssetAccountingEventProjectionRecord?> GetLatestAsync(Guid eventId, long eventVersion, CancellationToken ct = default)
            => inner.GetLatestAsync(eventId, eventVersion, ct);
        public Task<AssetAccountingEventAppendResult> AppendAsync(AssetAccountingEventSpineDto projection, long expectedSpineVersion,
            long expectedBookPositionVersion, CancellationToken ct = default)
        {
            if (_interrupt && projection.SpineVersion == 2) { _interrupt = false; throw new InvalidOperationException("Synthetic interruption after retained Expected occurrence."); }
            return inner.AppendAsync(projection, expectedSpineVersion, expectedBookPositionVersion, ct);
        }
    }
}
