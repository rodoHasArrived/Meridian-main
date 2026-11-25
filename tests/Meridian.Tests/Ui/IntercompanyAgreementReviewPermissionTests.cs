using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Meridian.Application.FundStructure;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Services;
using Meridian.Contracts.Workstation;
using Meridian.Identity;
using Meridian.Identity.Auth;
using Meridian.Storage.FundStructure;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;

namespace Meridian.Tests.Ui;

public sealed class IntercompanyAgreementReviewPermissionTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData("approve")]
    [InlineData("reject")]
    public async Task ComplianceOnly_CannotMakeOrdinaryDecision_EvenAsIndependentScopedReviewer(string action)
    {
        await using var fixture = await Fixture.CreateAsync(UserPermission.ManageCompliance, "independent-reviewer");

        var response = await fixture.DecideAsync(action, fixture.Command);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        fixture.AssertSubmitted();
        fixture.Authorization.VerifyNoOtherCalls();
        fixture.Evidence.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("approve", UserPermission.ManageFundStructure)]
    [InlineData("reject", UserPermission.ManageFundStructure)]
    [InlineData("approve", UserPermission.AdminMaintenance)]
    [InlineData("reject", UserPermission.AdminMaintenance)]
    public async Task NativeManagementPermission_PreservesOrdinaryIndependentDecision(string action, UserPermission permission)
    {
        await using var fixture = await Fixture.CreateAsync(permission, "independent-reviewer");

        var response = await fixture.DecideAsync(action, fixture.Command);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, fixture.Store.State!.AggregateVersion);
        Assert.Equal(action == "approve" ? IntercompanyAgreementProposalStateDto.Approved : IntercompanyAgreementProposalStateDto.Rejected,
            fixture.Store.State.Proposals.Single().State);
        Assert.Null(fixture.Store.State.History.Last().ExternalReview);
    }

    [Theory]
    [InlineData("approve")]
    [InlineData("reject")]
    public async Task ComplianceOnly_PreparerCanAttestCompleteExternalReview_ForEitherDecision(string action)
    {
        await using var fixture = await Fixture.CreateAsync(UserPermission.ManageCompliance, "preparer");

        var response = await fixture.DecideAsync(action, fixture.Command with { ExternalReview = fixture.Review });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var attestation = fixture.Store.State!.History.Last().ExternalReview!;
        Assert.Equal("preparer", attestation.Operator);
        Assert.Equal("External counsel", attestation.ExternalReviewer);
        Assert.Equal(fixture.Command.SubmittedHash, attestation.SubmittedHash);
        Assert.Equal(fixture.Review.Evidence, attestation.Evidence);
        Assert.Equal(attestation, fixture.Store.State.Proposals.Single().ExternalReview);
        if (action == "approve")
            Assert.Equal(attestation, Assert.Single(fixture.Store.State.Revisions).ExternalReview);
        else
            Assert.Empty(fixture.Store.State.Revisions);
        fixture.Authorization.Verify(service => service.AuthorizeManyAsync("preparer", UserPermission.ManageCompliance,
            AccessScopeKindDto.LegalEntity, It.IsAny<IReadOnlyCollection<Guid>>(), UserPermission.ManageCompliance,
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        fixture.Evidence.Verify(service => service.ResolveAsync(new("tenant-a", "preparer"), fixture.Command.AgreementId,
            fixture.Command.ProposalId, It.Is<IReadOnlyList<IntercompanyAgreementEvidenceDto>>(items => items.Count == 1
                && items[0] == fixture.Review.Evidence), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Theory]
    [InlineData("approve", "reviewer", HttpStatusCode.BadRequest)]
    [InlineData("reject", "reviewer", HttpStatusCode.BadRequest)]
    [InlineData("approve", "evidence", HttpStatusCode.BadRequest)]
    [InlineData("reject", "evidence", HttpStatusCode.BadRequest)]
    [InlineData("approve", "hash", HttpStatusCode.Conflict)]
    [InlineData("reject", "hash", HttpStatusCode.Conflict)]
    public async Task ComplianceOnly_MalformedExternalReviewCannotReplaceOrdinaryPermission(
        string action, string invalidField, HttpStatusCode expected)
    {
        await using var fixture = await Fixture.CreateAsync(UserPermission.ManageCompliance, "independent-reviewer");
        var review = invalidField switch
        {
            "reviewer" => fixture.Review with { ExternalReviewer = "" },
            "evidence" => fixture.Review with { Evidence = new("") },
            _ => fixture.Review with { SubmittedHash = new string('b', 64) }
        };

        var response = await fixture.DecideAsync(action, fixture.Command with { ExternalReview = review });

        Assert.Equal(expected, response.StatusCode);
        fixture.AssertSubmitted();
    }

    [Theory]
    [InlineData("approve", UserPermission.ManageFundStructure)]
    [InlineData("reject", UserPermission.ManageFundStructure)]
    [InlineData("approve", UserPermission.AdminMaintenance)]
    [InlineData("reject", UserPermission.AdminMaintenance)]
    public async Task ExternalReview_StillRequiresCompliancePermission(string action, UserPermission permission)
    {
        await using var fixture = await Fixture.CreateAsync(permission, "preparer");

        var response = await fixture.DecideAsync(action, fixture.Command with { ExternalReview = fixture.Review });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        fixture.AssertSubmitted();
        fixture.Authorization.VerifyNoOtherCalls();
        fixture.Evidence.VerifyNoOtherCalls();
    }

    private sealed class Fixture(WebApplication app, MemoryStore store, IntercompanyAgreementCommandDto command,
        IntercompanyExternalReviewDto review, Mock<IScopedAuthorizationService> authorization,
        Mock<IIntercompanyAgreementEvidenceAuthority> evidence) : IAsyncDisposable
    {
        public MemoryStore Store => store;
        public IntercompanyAgreementCommandDto Command => command;
        public IntercompanyExternalReviewDto Review => review;
        public Mock<IScopedAuthorizationService> Authorization => authorization;
        public Mock<IIntercompanyAgreementEvidenceAuthority> Evidence => evidence;

        public Task<HttpResponseMessage> DecideAsync(string action, IntercompanyAgreementCommandDto decision)
            => app.GetTestClient().PostAsJsonAsync(
                $"/api/fund-structure/intercompany-agreements/{command.AgreementId}/proposals/{command.ProposalId}/{action}", decision);

        public void AssertSubmitted()
        {
            Assert.Equal(2, store.State!.AggregateVersion);
            Assert.Equal(IntercompanyAgreementProposalStateDto.Submitted, Assert.Single(store.State.Proposals).State);
            Assert.Empty(store.State.Revisions);
            Assert.All(store.State.History, item => Assert.Null(item.ExternalReview));
        }

        public ValueTask DisposeAsync() => app.DisposeAsync();

        public static async Task<Fixture> CreateAsync(UserPermission permission, string actor)
        {
            var first = Guid.NewGuid();
            var second = Guid.NewGuid();
            var store = new MemoryStore(first, second);
            var access = new Mock<IIntercompanyAgreementAccessAuthority>();
            access.Setup(service => service.CanAccessEntitiesAsync(It.IsAny<IntercompanyAgreementCallContext>(),
                It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
            var evidence = new Mock<IIntercompanyAgreementEvidenceAuthority>(MockBehavior.Strict);
            evidence.Setup(service => service.ResolveAsync(It.IsAny<IntercompanyAgreementCallContext>(), It.IsAny<Guid>(),
                It.IsAny<Guid>(), It.IsAny<IReadOnlyList<IntercompanyAgreementEvidenceDto>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((IntercompanyAgreementCallContext _, Guid _, Guid _, IReadOnlyList<IntercompanyAgreementEvidenceDto> items,
                    CancellationToken _) => items);
            var retainedEvidence = new IntercompanyAgreementEvidenceDto("vault:agreement", "artifact-1", new string('a', 64));
            var service = new IntercompanyAgreementService(store, access.Object, evidence.Object, new Clock());
            var context = new IntercompanyAgreementCallContext("tenant-a", "preparer");
            var create = new IntercompanyAgreementCommandDto(Guid.NewGuid(), Guid.NewGuid(), 0, Guid.NewGuid(),
                new("IC-1", "Synthetic loan", first, "Lender", second, "Borrower", IntercompanyAgreementTypeDto.Loan,
                    "USD", new(2026, 1, 1), Terms: new(FixedAnnualRate: 0.05m), Evidence: [retainedEvidence]), "Synthetic rationale");
            await service.CreateAsync(context, create);
            var submitted = await service.SubmitAsync(context, create with
            { CommandId = Guid.NewGuid(), ExpectedAggregateVersion = 1, Draft = null });
            var hash = submitted.Detail.Proposals.Single().SubmittedHash!;
            var command = create with
            { CommandId = Guid.NewGuid(), ExpectedAggregateVersion = 2, Draft = null, SubmittedHash = hash };
            var review = new IntercompanyExternalReviewDto("External counsel", "counsel:matter-1", new(2026, 11, 11),
                retainedEvidence, "I attest the retained external review covers these submitted terms.", hash);
            evidence.Invocations.Clear();

            var authorization = new Mock<IScopedAuthorizationService>(MockBehavior.Strict);
            authorization.Setup(scope => scope.AuthorizeManyAsync(actor, It.IsAny<UserPermission>(),
                AccessScopeKindDto.LegalEntity, It.IsAny<IReadOnlyCollection<Guid>>(), permission, It.IsAny<CancellationToken>()))
                .ReturnsAsync((string user, UserPermission required, AccessScopeKindDto kind, IReadOnlyCollection<Guid> ids,
                    UserPermission _, CancellationToken _) => (IReadOnlyDictionary<Guid, ScopedAuthorizationDecisionDto>)ids
                    .ToDictionary(id => id, id => new ScopedAuthorizationDecisionDto(true, user, required, kind, id, "Synthetic grant")));
            var structure = new Mock<IFundStructureStore>(MockBehavior.Strict);
            structure.Setup(graph => graph.GetNodeTenantsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
                new FundStructureTenantMap(true, new Dictionary<Guid, string> { [first] = "tenant-a", [second] = "tenant-a" }));
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
            builder.WebHost.UseTestServer();
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddSingleton(structure.Object);
            builder.Services.AddSingleton(authorization.Object);
            builder.Services.AddSingleton<IIntercompanyAgreementAccessAuthority, HttpContextIntercompanyAgreementAccessAuthority>();
            builder.Services.AddSingleton<IIntercompanyAgreementService>(services => new IntercompanyAgreementService(store,
                services.GetRequiredService<IIntercompanyAgreementAccessAuthority>(), evidence.Object, new Clock()));
            var app = builder.Build();
            app.Use(async (http, next) =>
            {
                http.Items[LoginSessionMiddleware.CurrentUserKey] = actor;
                http.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permission;
                http.Items[LoginSessionMiddleware.CurrentTenantIdKey] = "tenant-a";
                http.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] = "company-a";
                await next(http);
            });
            app.MapIntercompanyAgreementEndpoints(JsonOptions);
            await app.StartAsync();
            return new(app, store, command, review, authorization, evidence);
        }
    }

    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 12, 0, 0, 0, TimeSpan.Zero);
    }

    private sealed class MemoryStore(Guid first, Guid second) : IIntercompanyAgreementStore
    {
        public IntercompanyAgreementStoreState? State { get; private set; }
        public Task<IntercompanyAgreementStoreState?> GetAsync(string tenantId, Guid agreementId, CancellationToken ct = default)
            => Task.FromResult(State);
        public Task<IReadOnlyList<IntercompanyAgreementStoreState>> ListAsync(string tenantId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<IntercompanyAgreementStoreState>>(State is null ? [] : [State]);
        public Task<IIntercompanyAgreementPostingLease> AcquirePostingLeaseAsync(string tenantId, IReadOnlyList<Guid> ids,
            CancellationToken ct = default) => throw new NotSupportedException();
        public async Task<T> ExecuteAsync<T>(string tenantId, Guid agreementId,
            Func<IIntercompanyAgreementStoreTransaction, CancellationToken, Task<T>> operation, CancellationToken ct = default)
        {
            var transaction = new Transaction(State, first, second);
            var result = await operation(transaction, ct);
            State = transaction.Pending ?? State;
            return result;
        }

        private sealed class Transaction(IntercompanyAgreementStoreState? state, Guid first, Guid second)
            : IIntercompanyAgreementStoreTransaction
        {
            public IntercompanyAgreementStoreState? State => state;
            public IntercompanyAgreementStoreState? Pending { get; private set; }
            public void Save(IntercompanyAgreementStoreState value) => Pending = value;
            public Task<LegalEntitySummaryDto?> GetLegalEntityAsync(Guid id, CancellationToken ct = default)
                => Task.FromResult(id == first || id == second ? new LegalEntitySummaryDto(id, LegalEntityTypeDto.Other,
                    id.ToString("N"), "Synthetic entity", "US", "USD", true, new(2025, 1, 1, 0, 0, 0, TimeSpan.Zero), null) : null);
        }
    }
}
