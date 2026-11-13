using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Services;
using Meridian.Contracts.Tenancy;
using Meridian.Identity.Auth;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;

namespace Meridian.Tests.Ui;

public sealed class IntercompanyObligationEndpointTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Guid BookId = Guid.Parse("941d8a91-c293-4408-910d-d030447edbbb");
    private static readonly Guid EntityId = Guid.Parse("538da494-83da-487f-81fb-1278cb5a68a6");

    [Fact]
    public async Task MonitorDerivesNativeContextAndFundFromAuthenticatedBookScope()
    {
        var monitor = new Mock<IIntercompanyObligationMonitorService>();
        IntercompanyAgreementCallContext? actualContext = null;
        string? actualCompany = null;
        IntercompanyObligationMonitorRequestDto? actualRequest = null;
        monitor.Setup(x => x.EvaluateAsync(It.IsAny<IntercompanyAgreementCallContext>(), It.IsAny<string>(),
            It.IsAny<IntercompanyObligationMonitorRequestDto>(), It.IsAny<CancellationToken>()))
            .Callback<IntercompanyAgreementCallContext, string?, IntercompanyObligationMonitorRequestDto, CancellationToken>((context, company, request, _) =>
            { actualContext = context; actualCompany = company; actualRequest = request; })
            .ReturnsAsync(Observation());
        await using var app = await AppAsync(monitor.Object);
        var response = await app.GetTestClient().GetAsync($"/api/ledger/intercompany/obligations?ledgerBookId={BookId:D}&asOf=2026-11-13&tenantId=forged&actor=forged");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new IntercompanyAgreementCallContext("tenant-a", "operator-a"), actualContext);
        Assert.Equal("company-a", actualCompany);
        Assert.Equal("fund-a", actualRequest!.FundProfileId);
        Assert.Equal(BookId, actualRequest.LedgerBookId);
        Assert.Equal(30, actualRequest.ExpiryHorizonDays);
        Assert.Equal(new DateOnly(2026, 11, 13), actualRequest.AsOf);
    }

    [Fact]
    public async Task SameTenantDifferentCompanyBookIsDeniedBeforeMonitor()
    {
        var monitor = new Mock<IIntercompanyObligationMonitorService>(MockBehavior.Strict);
        await using var app = await AppAsync(monitor.Object, ownerCompany: "another-company");
        var response = await app.GetTestClient().GetAsync($"/api/ledger/intercompany/obligations?ledgerBookId={BookId:D}&asOf=2026-11-13");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        monitor.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("asOf=2026-11-13")]
    [InlineData("ledgerBookId=941d8a91-c293-4408-910d-d030447edbbb&asOf=not-a-date")]
    [InlineData("ledgerBookId=941d8a91-c293-4408-910d-d030447edbbb")]
    public async Task MissingBookOrInvalidDateIsBadRequestBeforeMonitor(string query)
    {
        var monitor = new Mock<IIntercompanyObligationMonitorService>(MockBehavior.Strict);
        await using var app = await AppAsync(monitor.Object);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await app.GetTestClient().GetAsync("/api/ledger/intercompany/obligations?" + query)).StatusCode);
        monitor.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("fundProfileId=another-fund")]
    [InlineData("entityId=11111111-2222-3333-4444-555555555555")]
    public async Task MismatchedFundOrEntityAssertionIsDenied(string extra)
    {
        var monitor = new Mock<IIntercompanyObligationMonitorService>(MockBehavior.Strict);
        await using var app = await AppAsync(monitor.Object);
        Assert.Equal(HttpStatusCode.Forbidden, (await app.GetTestClient().GetAsync(
            $"/api/ledger/intercompany/obligations?ledgerBookId={BookId:D}&asOf=2026-11-13&{extra}")).StatusCode);
        monitor.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task MissingAuthorityReturns503AndMissingScopeDeniesBeforeService()
    {
        await using (var unconfigured = await AppAsync(null))
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await unconfigured.GetTestClient().GetAsync(
                $"/api/ledger/intercompany/obligations?ledgerBookId={BookId:D}&asOf=2026-11-13")).StatusCode);
        var monitor = new Mock<IIntercompanyObligationMonitorService>(MockBehavior.Strict);
        await using var unscoped = await AppAsync(monitor.Object, includeScope: false);
        Assert.Equal(HttpStatusCode.Forbidden, (await unscoped.GetTestClient().GetAsync(
            $"/api/ledger/intercompany/obligations?ledgerBookId={BookId:D}&asOf=2026-11-13")).StatusCode);
        monitor.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DatePreviewUsesExplicitOldApprovedVersionRatherThanCurrentRevision()
    {
        var detail = Detail();
        var legal = Legal(detail);
        var preview = new Mock<IIntercompanyStructuredTermsPreviewService>();
        IntercompanyAgreementDraftDto? actualDraft = null;
        preview.Setup(x => x.Preview(It.IsAny<IntercompanyAgreementDraftDto>(), It.IsAny<string>()))
            .Callback<IntercompanyAgreementDraftDto, string>((draft, _) => actualDraft = draft)
            .Returns((IntercompanyAgreementDraftDto _, string hash) => Preview(hash));
        await using var app = await AppAsync(null, legal: legal.Object, preview: preview.Object);
        var response = await app.GetTestClient().GetAsync($"/api/fund-structure/intercompany-agreements/{detail.AgreementId:D}/schedule?version=1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Same(detail.Revisions[0].Terms, actualDraft);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(detail.Revisions[0].PayloadHash, body.RootElement.GetProperty("subjectHash").GetString());
    }

    [Fact]
    public async Task DatePreviewPinsSelectedSubmittedProposalAndRejectsMixedSelectors()
    {
        var detail = Detail();
        var legal = Legal(detail);
        var preview = new Mock<IIntercompanyStructuredTermsPreviewService>();
        preview.Setup(x => x.Preview(It.IsAny<IntercompanyAgreementDraftDto>(), It.IsAny<string>()))
            .Returns((IntercompanyAgreementDraftDto _, string hash) => Preview(hash));
        await using var app = await AppAsync(null, legal: legal.Object, preview: preview.Object);
        var proposal = detail.Proposals.Single();
        var response = await app.GetTestClient().GetAsync($"/api/fund-structure/intercompany-agreements/{detail.AgreementId:D}/schedule?proposalId={proposal.ProposalId:D}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        preview.Verify(x => x.Preview(proposal.Draft, proposal.SubmittedHash!), Times.Once);
        Assert.Equal(HttpStatusCode.BadRequest, (await app.GetTestClient().GetAsync(
            $"/api/fund-structure/intercompany-agreements/{detail.AgreementId:D}/schedule?version=1&proposalId={proposal.ProposalId:D}")).StatusCode);
    }

    [Fact]
    public async Task RawJsonKeepsStringEnumsAndExactDecimalTermsAcrossHttpBinding()
    {
        var legal = new Mock<IIntercompanyAgreementService>();
        IntercompanyAgreementCommandDto? actual = null;
        legal.Setup(x => x.CreateAsync(It.IsAny<IntercompanyAgreementCallContext>(), It.IsAny<IntercompanyAgreementCommandDto>(), It.IsAny<CancellationToken>()))
            .Callback<IntercompanyAgreementCallContext, IntercompanyAgreementCommandDto, CancellationToken>((_, command, _) => actual = command)
            .ReturnsAsync((IntercompanyAgreementCallContext _, IntercompanyAgreementCommandDto command, CancellationToken _) =>
                new IntercompanyAgreementCommandResultDto(Detail(), new(command.CommandId, "Create", "synthetic", "operator-a", DateTimeOffset.UtcNow, 1, Detail())));
        await using var app = await AppAsync(null, legal: legal.Object, permissions: UserPermission.ManageFundStructure);
        const string rate = "0.0123456789012345678901234567";
        var json = $$"""
            {"commandId":"{{Guid.NewGuid():D}}","agreementId":"{{Guid.NewGuid():D}}","expectedAggregateVersion":0,"proposalId":"{{Guid.NewGuid():D}}",
             "draft":{"code":"EXACT","name":"Exact decimal loan","partyOneId":"{{EntityId:D}}","partyOneRole":"Lender",
              "partyTwoId":"{{Guid.NewGuid():D}}","partyTwoRole":"Borrower","agreementType":"Loan","currency":"USD","effectiveFrom":"2026-11-13",
              "terms":{"structuredTerms":{"rateBasis":"Fixed","fixedAnnualRate":"{{rate}}","dayCount":"Actual360","compounding":"Simple",
               "schedule":{"startDate":"2026-11-13","endDate":"2027-11-13","frequency":"Monthly","calendarId":"WEEKENDS","businessDayConvention":"Following","settlementBusinessDays":2} } } } }
            """;
        var response = await app.GetTestClient().PostAsync("/api/fund-structure/intercompany-agreements", new StringContent(json, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(IntercompanyAgreementTypeDto.Loan, actual!.Draft!.AgreementType);
        Assert.Equal(IntercompanyRateBasisDto.Fixed, actual.Draft.Terms!.StructuredTerms!.RateBasis);
        Assert.Equal(decimal.Parse(rate, CultureInfo.InvariantCulture), actual.Draft.Terms.StructuredTerms.FixedAnnualRate);
        Assert.Equal(IntercompanyFrequencyDto.Monthly, actual.Draft.Terms.StructuredTerms.Schedule!.Frequency);
        var retained = JsonSerializer.Serialize(actual, JsonOptions);
        using var document = JsonDocument.Parse(retained);
        Assert.Equal(rate, document.RootElement.GetProperty("draft").GetProperty("terms").GetProperty("structuredTerms").GetProperty("fixedAnnualRate").GetString());
    }

    private static Mock<IIntercompanyAgreementService> Legal(IntercompanyAgreementDetailDto detail)
    {
        var legal = new Mock<IIntercompanyAgreementService>();
        legal.Setup(x => x.GetAsync(It.IsAny<IntercompanyAgreementCallContext>(), detail.AgreementId, It.IsAny<DateOnly?>(),
            It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>())).ReturnsAsync(detail);
        return legal;
    }
    private static IntercompanyStructuredTermsPreviewDto Preview(string hash) => new("Ready", "StructuredV1", hash,
        "synthetic-kernel", "WEEKENDS", "synthetic-calendar-hash", [], ["Dates only"]);
    private static IntercompanyObligationMonitorDto Observation() => new(new(2026, 11, 13), 30, DateTimeOffset.UtcNow,
        "Current", true, "synthetic-source", [], [], null, []);
    private static IntercompanyAgreementDetailDto Detail()
    {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.Parse("2026-11-13T00:00:00Z");
        var draft = new IntercompanyAgreementDraftDto("SYN", "Synthetic loan", EntityId, "Lender", Guid.NewGuid(), "Borrower",
            IntercompanyAgreementTypeDto.Loan, "USD", new(2026, 1, 1), Terms: new(FixedAnnualRate: 0.01m));
        var first = new IntercompanyAgreementRevisionDto(id, 1, null, null, IntercompanyAgreementActionDto.Activate, draft,
            Guid.NewGuid(), new string('a', 64), ["author"], "author", "reviewer", now, "Original terms");
        var current = first with
        {
            AgreementVersion = 2,
            PredecessorAgreementVersion = 1,
            BaseAgreementVersion = 1,
            Action = IntercompanyAgreementActionDto.Amend,
            Terms = draft with { Terms = new(FixedAnnualRate: 0.02m) },
            PayloadHash = new string('b', 64)
        };
        var proposal = new IntercompanyAgreementProposalDto(Guid.NewGuid(), IntercompanyAgreementActionDto.Amend,
            IntercompanyAgreementProposalStateDto.Submitted, draft with { Terms = new(FixedAnnualRate: 0.03m) }, 2,
            ["author"], "author", now, new string('c', 64), now, now, "Pending terms");
        return new(id, 7, draft.Code, draft.Name, draft.PartyOneId, draft.PartyOneRole, draft.PartyTwoId, draft.PartyTwoRole,
            draft.AgreementType, "USD", new(2026, 11, 13), IntercompanyAgreementStatusDto.Active, 2, current,
            [proposal], [first, current], [], new(false, false, false, false, false, false, false));
    }

    private static async Task<WebApplication> AppAsync(IIntercompanyObligationMonitorService? monitor,
        string ownerCompany = "company-a", bool includeScope = true,
        IIntercompanyAgreementService? legal = null, IIntercompanyStructuredTermsPreviewService? preview = null,
        UserPermission permissions = UserPermission.ViewLedgerReports)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        if (monitor is not null)
            builder.Services.AddSingleton(monitor);
        if (legal is not null)
            builder.Services.AddSingleton(legal);
        if (preview is not null)
            builder.Services.AddSingleton(preview);
        var journals = new Mock<ILedgerJournalStore>();
        journals.Setup(x => x.GetLedgerBookAsync(BookId, It.IsAny<CancellationToken>())).ReturnsAsync(new LedgerBookRecord(
            BookId, "fund-a", EntityId, FundStructureNodeKindDto.Entity, "Synthetic book", "USD", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        var owners = new Mock<IFundProfileTenancyRegistry>();
        owners.Setup(x => x.ResolveAsync("fund-a", It.IsAny<CancellationToken>())).ReturnsAsync(new FundProfileOwnership("fund-a", "tenant-a", ownerCompany));
        builder.Services.AddSingleton(journals.Object);
        builder.Services.AddSingleton(owners.Object);
        var app = builder.Build();
        app.Use(async (http, next) =>
        {
            http.Items[LoginSessionMiddleware.CurrentUserKey] = "operator-a";
            http.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permissions;
            if (includeScope)
            {
                http.Items[LoginSessionMiddleware.CurrentTenantIdKey] = "tenant-a";
                http.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] = "company-a";
            }
            await next(http);
        });
        app.MapIntercompanyObligationEndpoints(JsonOptions);
        app.MapIntercompanyAgreementEndpoints(JsonOptions);
        await app.StartAsync();
        return app;
    }
}
