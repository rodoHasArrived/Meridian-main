using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.PrivateCapital;
using Meridian.Identity.Auth;
using Meridian.Ledger;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Evidence;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Meridian.Tests.Ui;

public sealed class PrivateCapitalEndpointsTests
{
    private const string Root = "/api/ledger/private-capital";
    private const string Tenant = "capital-tenant";
    private const string Company = "capital-company";
    private const string Fund = "capital-fund";
    private const string Actor = "authenticated-capital-operator";
    private const string VaultId = "ev-0123456789abcdef01234567";
    private const string EvidenceLink = "evidence://evidence-vault/" + VaultId;
    private static readonly Guid BookId = Guid.Parse("99b9fbe5-bd7f-4b36-875d-6d556b81723f");
    private static readonly Guid EntityId = Guid.Parse("57e691da-69dc-4b66-b61a-1b9dc03d84a4");
    private static readonly Guid PeriodId = Guid.Parse("7f2058d6-567a-4da8-8344-03b6ee7bf6bf");
    private static readonly DateOnly NoticeDate = new(2026, 11, 10);
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly PrivateCapitalScopeDto Scope = new(Tenant, Company, EntityId.ToString("D"), Fund, BookId, "USD");

    [Theory]
    [InlineData("lifecycle")]
    [InlineData("books")]
    [InlineData("registry")]
    public async Task Workbench_MissingAuthorityService_Returns503(string missing)
    {
        var fixture = new Fixture();
        await using var app = await fixture.StartAsync(missing: missing);

        var response = await app.GetTestClient().GetAsync(Url("/workbench"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Empty(fixture.Store.ReceivedCalls());
    }

    [Fact]
    public async Task BankObservationImport_MissingSameDatabaseAuthority_Returns503WithoutReadingSource()
    {
        var fixture = new Fixture();
        await using var app = await fixture.StartAsync();

        var response = await app.GetTestClient().PostAsJsonAsync(Url("/bank-observations"),
            new ImportPrivateCapitalBankObservationRequest(EvidenceLink, "bank-source-document"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Empty(fixture.Vault.ReceivedCalls());
    }

    [Theory]
    [InlineData("unowned")]
    [InlineData("tenant")]
    [InlineData("company")]
    [InlineData("fund")]
    public async Task Workbench_UnownedOrForeignFund_Returns403BeforeReadingCapital(string mismatch)
    {
        var fixture = new Fixture();
        fixture.Registry.ResolveAsync(Fund, Arg.Any<CancellationToken>()).Returns(
            mismatch == "unowned" ? null : new FundProfileOwnership(
                mismatch == "fund" ? "other-fund" : Fund,
                mismatch == "tenant" ? "other-tenant" : Tenant,
                mismatch == "company" ? "other-company" : Company));
        await using var app = await fixture.StartAsync();

        var response = await app.GetTestClient().GetAsync(Url("/workbench"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(fixture.Store.ReceivedCalls());
    }

    [Theory]
    [InlineData(FundStructureNodeKindDto.Fund)]
    [InlineData(FundStructureNodeKindDto.Vehicle)]
    [InlineData(FundStructureNodeKindDto.Account)]
    public async Task Workbench_BookIsNotEntityOwned_Returns409(FundStructureNodeKindDto kind)
    {
        var fixture = new Fixture();
        fixture.Books.GetBookAsync(BookId, Arg.Any<CancellationToken>()).Returns(Book() with { FundStructureNodeKind = kind });
        await using var app = await fixture.StartAsync();

        var response = await app.GetTestClient().GetAsync(Url("/workbench"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Empty(fixture.Store.ReceivedCalls());
    }

    [Fact]
    public async Task CreateCommitment_ReadPermissionCannotMutate()
    {
        var fixture = new Fixture();
        await using var app = await fixture.StartAsync(permission: UserPermission.ViewLedgerReports);

        var response = await app.GetTestClient().PostAsJsonAsync(Url("/commitments"), CommitmentRequest());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(fixture.Store.ReceivedCalls());
        Assert.Empty(fixture.Vault.ReceivedCalls());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task CreateCommitment_MissingAuthenticatedActor_IsRefused(string? actor)
    {
        var fixture = new Fixture();
        await using var app = await fixture.StartAsync(actor: actor);

        var response = await app.GetTestClient().PostAsJsonAsync(Url("/commitments"), CommitmentRequest());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(fixture.Store.ReceivedCalls());
    }

    [Theory]
    [InlineData("missing", HttpStatusCode.BadRequest)]
    [InlineData("noncanonical", HttpStatusCode.BadRequest)]
    [InlineData("foreign", HttpStatusCode.Conflict)]
    [InlineData("tampered", HttpStatusCode.Conflict)]
    [InlineData("unreviewed", HttpStatusCode.Conflict)]
    [InlineData("no-documents", HttpStatusCode.Conflict)]
    [InlineData("unavailable", HttpStatusCode.ServiceUnavailable)]
    public async Task CreateCommitment_UnverifiedSourceEvidence_DoesNotWrite(string failure, HttpStatusCode expected)
    {
        var fixture = new Fixture();
        var request = CommitmentRequest();
        if (failure == "missing") request = request with { EvidenceLinks = [] };
        if (failure == "noncanonical") request = request with { EvidenceLinks = ["https://example.test/agreement.pdf"] };
        if (failure == "foreign")
            fixture.Vault.TryGetVaultIdentityAsync(VaultId, Tenant, Company, Arg.Any<CancellationToken>())
                .Returns((EvidenceVaultIdentityDto?)null);
        if (failure == "tampered")
            fixture.Vault.VerifyRetainedContentAsync(VaultId, Tenant, Company, Arg.Any<CancellationToken>()).Returns(false);
        if (failure == "unreviewed")
            fixture.Vault.TryGetVaultIdentityAsync(VaultId, Tenant, Company, Arg.Any<CancellationToken>())
                .Returns(Identity(EvidenceDocumentReviewStatusDto.NeedsReview));
        if (failure == "no-documents")
            fixture.Vault.TryGetVaultIdentityAsync(VaultId, Tenant, Company, Arg.Any<CancellationToken>())
                .Returns(Identity() with { Documents = [] });
        await using var app = await fixture.StartAsync(missing: failure == "unavailable" ? "vault" : null);

        var response = await app.GetTestClient().PostAsJsonAsync(Url("/commitments"), request);

        Assert.Equal(expected, response.StatusCode);
        await fixture.Store.DidNotReceiveWithAnyArgs().SaveCommitmentDraftAsync(default!, default, default);
        if (failure is "foreign" or "tampered" or "unreviewed" or "no-documents")
            await fixture.Vault.Received(1).TryGetVaultIdentityAsync(VaultId, Tenant, Company, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateCommitment_UsesAuthenticatedActorAndBookScope_AndPreservesExactDecimal()
    {
        const decimal exact = 9007199254740993.123456789012m;
        var fixture = new Fixture();
        await using var app = await fixture.StartAsync();

        var response = await app.GetTestClient().PostAsJsonAsync(Url("/commitments"),
            new
            {
                commitmentId = "commitment-1", investorId = "investor-1", capitalAccountId = "capital-1",
                totalCommitment = exact, commitmentDate = NoticeDate, effectiveFrom = NoticeDate,
                evidenceLinks = new[] { EvidenceLink }, actor = "forged-body-actor", createdBy = "forged-body-actor",
                tenantId = "foreign-tenant", companyId = "foreign-company", fundProfileId = "foreign-fund",
                ledgerBookId = Guid.NewGuid(), currency = "EUR"
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.String, body.RootElement.GetProperty("totalCommitment").ValueKind);
        Assert.Equal("9007199254740993.123456789012", body.RootElement.GetProperty("totalCommitment").GetString());
        await fixture.Store.Received(1).SaveCommitmentDraftAsync(Arg.Is<PrivateCapitalCommitmentDto>(draft =>
            draft.CreatedBy == Actor && draft.Scope == Scope && draft.Revision == 1 && draft.TotalCommitment == exact),
            0, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApproveCommitment_UsesRetainedRevisionEvidenceAndAuthenticatedActor()
    {
        var fixture = new Fixture();
        fixture.Store.GetCommitmentAsync(Scope, "commitment-1", 2, Arg.Any<CancellationToken>())
            .Returns(Commitment() with { Revision = 2 });
        fixture.Store.ApproveCommitmentAsync(Scope, "commitment-1", 2, Actor, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Commitment() with { Revision = 2, ApprovalStatus = PrivateCapitalCommitmentApprovalStatus.Approved });
        await using var app = await fixture.StartAsync();

        var response = await app.GetTestClient().PostAsJsonAsync(Url("/commitments/commitment-1/approve"),
            new { revision = 2, actor = "forged-approver" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await fixture.Store.Received(1).ApproveCommitmentAsync(Scope, "commitment-1", 2, Actor,
            Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        await fixture.Vault.Received(1).VerifyRetainedContentAsync(VaultId, Tenant, Company, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("unowned")]
    [InlineData("foreign-book")]
    [InlineData("soft-closed")]
    [InlineData("hard-closed")]
    [InlineData("outside-date")]
    public async Task PreviewCall_RequiresSelectedBooksOpenPeriod(string failure)
    {
        var fixture = new Fixture();
        var period = Period();
        if (failure == "foreign-book") period = period with { LedgerBookId = Guid.NewGuid() };
        if (failure == "soft-closed") period = period with { Status = LedgerPeriodStatusDto.SoftClosed };
        if (failure == "hard-closed") period = period with { Status = LedgerPeriodStatusDto.HardClosed };
        if (failure == "outside-date") period = period with { EndDate = NoticeDate.AddDays(-1) };
        fixture.Books.ListPeriodsAsync(Arg.Any<LedgerPeriodQuery>(), Arg.Any<CancellationToken>())
            .Returns(failure == "unowned" ? Array.Empty<LedgerPeriodDto>() : [period]);
        await using var app = await fixture.StartAsync(permission: UserPermission.ManageDirectLending);

        var response = await app.GetTestClient().PostAsJsonAsync(Url("/calls/preview"), PreviewRequest());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Empty(fixture.Store.ReceivedCalls());
        await fixture.Books.Received(1).ListPeriodsAsync(
            Arg.Is<LedgerPeriodQuery>(query => query.LedgerBookId == BookId), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PreviewCall_PrivateCapitalPermissionUsesApprovedServerCommitmentWithoutWriting()
    {
        var fixture = new Fixture();
        fixture.Store.ListCommitmentsAsync(Scope, Arg.Any<CancellationToken>()).Returns([Commitment() with
        {
            ApprovalStatus = PrivateCapitalCommitmentApprovalStatus.Approved
        }]);
        await using var app = await fixture.StartAsync(permission: UserPermission.ManageDirectLending);

        var response = await app.GetTestClient().PostAsJsonAsync(Url("/calls/preview"), PreviewRequest());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("isReady").GetBoolean());
        Assert.Equal("300", body.RootElement.GetProperty("call").GetProperty("amountToCall").GetString());
        await fixture.Store.DidNotReceiveWithAnyArgs().SaveCallAsync(default!, default);
        await fixture.Store.DidNotReceiveWithAnyArgs().SaveCommitmentDraftAsync(default!, default, default);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WorkstationComposition_ResolvesLifecycleOnlyWithCapitalStore(bool configured)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        if (configured) services.AddSingleton(Substitute.For<IPrivateCapitalStore>());
        services.AddWorkstationSharedServices(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();

        Assert.Equal(configured, provider.GetService<PrivateCapitalLifecycleService>() is not null);
    }

    private static string Url(string suffix) => $"{Root}{suffix}?ledgerBookId={BookId:D}";
    private static CreatePrivateCapitalCommitmentRequest CommitmentRequest() => new(
        "commitment-1", "investor-1", "capital-1", 1000m, NoticeDate, NoticeDate, null, [EvidenceLink]);
    private static PreviewPrivateCapitalCallRequest PreviewRequest() => new(
        "call-1", 300m, NoticeDate, NoticeDate.AddDays(10), PeriodId.ToString("D"), ["commitment-1"]);
    private static PrivateCapitalCommitmentDto Commitment() => new(
        "commitment-1", Scope, 1, "investor-1", "capital-1", 1000m, NoticeDate, NoticeDate, null, true,
        PrivateCapitalCommitmentApprovalStatus.Draft, [EvidenceLink], Actor, Now);
    private static LedgerBookDto Book() => new(BookId, Fund, EntityId, FundStructureNodeKindDto.Entity,
        "Synthetic capital book", "USD", Now, Now);
    private static LedgerPeriodDto Period() => new(PeriodId, BookId, 2026, 11, "November 2026",
        new(2026, 11, 1), new(2026, 11, 30), LedgerPeriodStatusDto.Open, Now, null, 1);
    private static EvidenceVaultIdentityDto Identity(EvidenceDocumentReviewStatusDto status = EvidenceDocumentReviewStatusDto.Accepted)
        => new(VaultId, "agreement", "commitment-1", "manifest.json", "/manifest", Now, new string('a', 64), 1, "file")
        {
            TenantId = Tenant, Scope = Company,
            Documents = [new("document-1", "synthetic-agreement.pdf", EvidenceDocumentClassificationDto.Agreement,
                new string('a', 64), Now, "upload", Actor, Tenant, Company, EvidenceExtractionStatusDto.Accepted,
                [], new(status), [])]
        };

    private sealed class Fixture
    {
        public IPrivateCapitalStore Store { get; } = Substitute.For<IPrivateCapitalStore>();
        public ILedgerBookService Books { get; } = Substitute.For<ILedgerBookService>();
        public IFundProfileTenancyRegistry Registry { get; } = Substitute.For<IFundProfileTenancyRegistry>();
        public IEvidenceArtifactStore Vault { get; } = Substitute.For<IEvidenceArtifactStore>();

        public Fixture()
        {
            Books.GetBookAsync(BookId, Arg.Any<CancellationToken>()).Returns(Book());
            Books.ListPeriodsAsync(Arg.Any<LedgerPeriodQuery>(), Arg.Any<CancellationToken>()).Returns([Period()]);
            Registry.ResolveAsync(Fund, Arg.Any<CancellationToken>()).Returns(new FundProfileOwnership(Fund, Tenant, Company));
            Store.ListCommitmentsAsync(Arg.Any<PrivateCapitalScopeDto>(), Arg.Any<CancellationToken>()).Returns([]);
            Store.ListCallsAsync(Arg.Any<PrivateCapitalScopeDto>(), Arg.Any<CancellationToken>()).Returns([]);
            Store.ListPostingReceiptsAsync(Arg.Any<PrivateCapitalScopeDto>(), Arg.Any<CancellationToken>()).Returns([]);
            Store.SaveCommitmentDraftAsync(Arg.Any<PrivateCapitalCommitmentDto>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(call => call.ArgAt<PrivateCapitalCommitmentDto>(0));
            Vault.TryGetVaultIdentityAsync(VaultId, Tenant, Company, Arg.Any<CancellationToken>()).Returns(Identity());
            Vault.VerifyRetainedContentAsync(VaultId, Tenant, Company, Arg.Any<CancellationToken>()).Returns(true);
        }

        public async Task<WebApplication> StartAsync(string? missing = null,
            UserPermission permission = UserPermission.ManageDirectLending, string? actor = Actor)
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddSingleton(Store);
            if (missing != "lifecycle") builder.Services.AddSingleton(new PrivateCapitalLifecycleService(Store));
            if (missing != "books") builder.Services.AddSingleton(Books);
            if (missing != "registry") builder.Services.AddSingleton(Registry);
            if (missing != "vault") builder.Services.AddSingleton(Vault);
            var app = builder.Build();
            app.Use(async (context, next) =>
            {
                context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permission;
                if (actor is not null) context.Items[LoginSessionMiddleware.CurrentUserKey] = actor;
                context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = Tenant;
                context.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] = Company;
                await next();
            });
            app.MapLedgerEndpoints(new JsonSerializerOptions(JsonSerializerDefaults.Web));
            await app.StartAsync();
            return app;
        }
    }
}
