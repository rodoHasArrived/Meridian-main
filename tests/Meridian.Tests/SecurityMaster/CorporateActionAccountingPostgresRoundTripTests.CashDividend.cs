using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster.CorporateActions;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Operations;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Meridian.TestSupport;
using Meridian.Tests.Storage;
using Meridian.Tests.TestHelpers;
using Npgsql;

namespace Meridian.Tests.SecurityMaster;

public sealed partial class CorporateActionAccountingPostgresRoundTripTests
{
    [LedgerDatabaseFact]
    public async Task CashDividendNativeDeclaration_IsDefaultOff_RevisionChecked_AndBindsOnlyOneBook()
    {
        await using var fixture = await NativeDividendFixture.CreateAsync();
        var disabled = new PostgresCorporateActionOperationsStore(new SecurityMasterOptions
        {
            ConnectionString = fixture.Options.ConnectionString,
            Schema = fixture.Options.Schema,
            PreloadProjectionCache = false,
            EnableSinglePrincipalCashDividendAttestations = false
        });
        var request = fixture.DeclarationRequest(0, true);
        (await Assert.ThrowsAsync<CorporateActionOperationException>(() => disabled.ChangeCashDividendDeclarationAsync(request)))
            .Code.Should().Be("cash_dividend_policy_disabled");
        await Assert.ThrowsAsync<CorporateActionPermissionDeniedException>(() => fixture.Store.ChangeCashDividendDeclarationAsync(
            request with { Authority = new(true) }));
        var enabled = await fixture.Store.ChangeCashDividendDeclarationAsync(request);
        await Assert.ThrowsAsync<CorporateActionStateConflictException>(() => fixture.Store.ChangeCashDividendDeclarationAsync(
            fixture.DeclarationRequest(0, true) with { TenantId = Tenant.ToUpperInvariant() }));
        var replay = await fixture.Store.ChangeCashDividendDeclarationAsync(request);
        replay.Replayed.Should().BeTrue();
        replay.Declaration.Should().Be(enabled.Declaration);
        await Assert.ThrowsAsync<CorporateActionVersionConflictException>(() => fixture.Store.ChangeCashDividendDeclarationAsync(
            fixture.DeclarationRequest(0, false)));
        await Assert.ThrowsAsync<CorporateActionValidationException>(() => fixture.Store.ChangeCashDividendDeclarationAsync(
            fixture.DeclarationRequest(1, true) with { LedgerBookId = Guid.NewGuid() }));
        var withdrawn = await fixture.Store.ChangeCashDividendDeclarationAsync(fixture.DeclarationRequest(1, false)
            with
        { AccountRoleBinding = null, Authority = new(true) });
        withdrawn.Declaration.AccountRoleBinding.Should().Be(fixture.Binding);
        (await fixture.Store.GetCashDividendDeclarationStateAsync(Tenant, Company)).History.Should().HaveCount(2);
        (await fixture.Store.GetCashDividendDeclarationAsync("another-tenant", Company)).Should().BeNull();
    }

    [LedgerDatabaseFact]
    public async Task CashDividendNativeReservation_ReaffirmationAndWithdrawalRetainOneExactBudgetCharge()
    {
        await using var fixture = await NativeDividendFixture.CreateAsync();
        var declaration = (await fixture.Store.ChangeCashDividendDeclarationAsync(fixture.DeclarationRequest(0, true))).Declaration;
        var candidate = await fixture.CandidateAsync(Guid.NewGuid(), 6_000m, 10_000m, declaration);
        var attested = await fixture.Store.AttestCashDividendAsync(candidate.Request, candidate.Receipt,
            Guid.NewGuid(), candidate.Fingerprint);
        var readback = await fixture.Store.GetCaseAsync(candidate.Request.CaseId, Tenant, Company);
        readback!.AccountingStatus!.ApprovalId.Should().BeNull();
        readback.AccountingStatus.CashDividendAttestationId.Should().Be(attested.Attestation.AttestationId);
        readback.AccountingStatus.DecisionKind.Should().Be("PreparerAttestation");
        var replay = await fixture.Store.AttestCashDividendAsync(candidate.Request, candidate.Receipt, Guid.NewGuid(), candidate.Fingerprint);
        replay.Replayed.Should().BeTrue();
        await fixture.Store.ChangeCashDividendDeclarationAsync(fixture.DeclarationRequest(1, false));
        var renewed = (await fixture.Store.ChangeCashDividendDeclarationAsync(fixture.DeclarationRequest(2, true))).Declaration;
        var reaffirmRequest = candidate.Request with
        {
            ExpectedVersion = attested.Case.Version,
            ExpectedDeclarationRevision = renewed.Revision,
            IdempotencyKey = "reaffirm"
        };
        var reaffirm = await fixture.Store.AttestCashDividendAsync(reaffirmRequest,
            candidate.Receipt with { AttestationId = PreparerAttestationApprovalBoundary.NewReceiptId(), DeclarationRevision = renewed.Revision },
            Guid.NewGuid(), new string('b', 64));
        reaffirm.Attestation.BudgetReservationId.Should().Be(attested.Attestation.BudgetReservationId);
        (await fixture.ScalarAsync("select count(*) from {schema}.cash_dividend_attestation_reservations")).Should().Be(1);
        // A second independently identified case cannot split the same security/ex-date event over its cap.
        var split = await fixture.CandidateAsync(candidate.SecurityId, 6_000m, 10_000m, renewed);
        (await Assert.ThrowsAsync<CorporateActionOperationException>(() => fixture.Store.AttestCashDividendAsync(
            split.Request, split.Receipt, Guid.NewGuid(), split.Fingerprint))).Code.Should().Be("cash_dividend_attestation_limit");
        (await fixture.ScalarAsync("select count(*) from {schema}.cash_dividend_attestation_reservations")).Should().Be(1);
        var preparation = new CorporateActionOperationsService(fixture.Store);
        await preparation.TransitionCaseAsync(new(reaffirm.Case.CaseId, reaffirm.Case.Version,
            "return-attested-case-to-preparation", Tenant, Company, CorporateActionCaseStates.AccountingReview,
            Preparer, "Refresh the accounting preparation", ScopeAssertion: reaffirm.Case.Scope,
            Authority: PreparationAuthority));
        (await fixture.Store.GetCashDividendAttestationAsync(reaffirm.Case.CaseId, Tenant, Company)).Should().BeNull();
        (await fixture.ScalarAsync("select count(*) from {schema}.cash_dividend_attestations where voided_at is not null")).Should().Be(2);
        (await fixture.ScalarAsync("select count(*) from {schema}.cash_dividend_attestation_reservations")).Should().Be(1);
    }

    [LedgerDatabaseFact]
    public async Task CashDividendNativeReservation_ConcurrentCasesCannotExceedBookMonthLimit()
    {
        await using var fixture = await NativeDividendFixture.CreateAsync();
        var declaration = (await fixture.Store.ChangeCashDividendDeclarationAsync(fixture.DeclarationRequest(0, true))).Declaration;
        for (var i = 0; i < 9; i++)
        {
            var seed = await fixture.CandidateAsync(Guid.NewGuid(), 10_000m, 10_000m, declaration);
            await fixture.Store.AttestCashDividendAsync(seed.Request, seed.Receipt, Guid.NewGuid(), seed.Fingerprint);
        }
        var candidates = new List<NativeDividendCandidate>();
        for (var i = 0; i < 2; i++)
            candidates.Add(await fixture.CandidateAsync(Guid.NewGuid(), 10_000m, 10_000m, declaration));
        var results = await Task.WhenAll(candidates.Select(async candidate =>
        {
            try
            {
                await fixture.Store.AttestCashDividendAsync(candidate.Request, candidate.Receipt, Guid.NewGuid(), candidate.Fingerprint);
                return true;
            }
            catch (CorporateActionOperationException exception) when (exception.Code == "cash_dividend_attestation_limit")
            { return false; }
        }));
        results.Count(value => value).Should().Be(1);
        (await fixture.ScalarAsync("select sum(gross_usd)::bigint from {schema}.cash_dividend_attestation_reservations")).Should().Be(100_000);
        // The database prevents rewriting a reservation, independently of application callers.
        await Assert.ThrowsAsync<PostgresException>(() => fixture.ScalarAsync(
            "update {schema}.cash_dividend_attestation_reservations set gross_usd=1 returning 1"));
    }

    private sealed record NativeDividendCandidate(Guid SecurityId, AttestCorporateActionCashDividendRequestDto Request,
        CorporateActionCashDividendAttestationDto Receipt, string Fingerprint);

    private sealed class NativeDividendFixture : IAsyncDisposable
    {
        private readonly PostgresTestServer _server;
        private readonly HashSet<Guid> _securities = [];
        internal SecurityMasterOptions Options { get; }
        internal PostgresCorporateActionOperationsStore Store { get; }
        internal Guid BookId { get; } = Guid.NewGuid();
        internal Guid PeriodId { get; } = Guid.NewGuid();
        internal CashDividendAccountRoleBindingDto Binding => new(BookId, "assets/dividend-receivable", "income/dividends",
            "corpact-pack", "v1", Rule, "v1", DividendReceivableAccountName: "Dividend receivable", DividendIncomeAccountName: "Dividend income");

        private NativeDividendFixture(PostgresTestServer server, SecurityMasterOptions options)
        { _server = server; Options = options; Store = new(options); }

        internal static async Task<NativeDividendFixture> CreateAsync()
        {
            var server = await PostgresTestServer.CreateAsync("MERIDIAN_LEDGER_CONNECTION_STRING");
            var options = new SecurityMasterOptions
            {
                ConnectionString = server.ConnectionString,
                Schema = server.CreateSchemaName("ca_attest"),
                PreloadProjectionCache = false,
                EnableSinglePrincipalCashDividendAttestations = true
            };
            await SecurityMasterTestCapabilityFloor.InitializeAndActivateAsync(server, options);
            return new(server, options);
        }

        internal ChangeCashDividendAttestationDeclarationRequestDto DeclarationRequest(long revision, bool enabled) => new(
            Guid.NewGuid(), revision, Tenant, Company, BookId, enabled,
            CorporateActionCashDividendAttestationPolicy.Version, CorporateActionCashDividendAttestationPolicy.PolicyHash,
            "Synthetic native-store integration fixture", Preparer, new(true, CanBindAccountingRoles: true), Binding);

        internal async Task<NativeDividendCandidate> CandidateAsync(Guid securityId, decimal gross, decimal whole,
            CashDividendAttestationDeclarationDto declaration)
        {
            if (_securities.Add(securityId))
                await CreateSecurityAsync(Options, securityId, default);
            var preparation = new CorporateActionOperationsService(Store);
            var scope = new CorporateActionCaseScopeDto(Tenant, Company, FundProfileId: Fund,
                CustodyAccountId: Guid.NewGuid().ToString("D"),
                LedgerBookId: BookId.ToString("D"), PeriodId: PeriodId.ToString("D"), AccountingBasis: "GAAP", FunctionalCurrency: "USD");
            var processingCase = await CreateAccountingReviewCaseAsync(Store, preparation, securityId, scope, default);
            var projection = new CorporateActionCaseAccountingProjectionDto(Guid.NewGuid(), processingCase.CaseId,
                processingCase.Version, processingCase.CorporateActionId, 1, 3, new string('a', 64), new string('b', 64),
                "native-dividend:" + processingCase.CaseId, new string('c', 64), Guid.NewGuid(), 1,
                Binding.RulePackId, Binding.RulePackVersion, Binding.SelectedRuleId, Binding.SelectedRuleVersion,
                BookId, PeriodId, 1, "GAAP", Fund, "USD", EffectiveDate, gross, gross, Guid.NewGuid(), 1, true,
                Preparer, DateTimeOffset.UtcNow, true);
            var attachRequest = new AttachCorporateActionAccountingProjectionRequestDto(processingCase.CaseId,
                processingCase.Version, "native-attach", Tenant, Company, projection.AccountingEventId, 1, 3,
                projection.ProjectionInputHash, projection.DraftedCandidateFingerprint, projection.PostingIdempotencyKey,
                projection.PolicyDecisionId, 1, projection.LotSnapshotId, 1, Preparer, scope, Authority: PreparationAuthority);
            var attached = await Store.AttachAccountingProjectionAsync(attachRequest, projection, new string('d', 64));
            processingCase = await TransitionAsync(preparation, attached.Case, CorporateActionCaseStates.ReadyForApproval, default);
            var source = processingCase.SourceSnapshot!.ProviderIdentity;
            var snapshot = JsonSerializer.Serialize(new { fixture = "native allocator operands", processingCase.CaseId, gross, whole });
            var snapshotHash = Sha256Digest.ComputeUtf8(snapshot);
            var request = new AttestCorporateActionCashDividendRequestDto(processingCase.CaseId, processingCase.Version,
                "native-attest", Tenant, Company, attached.Projection.ProjectionId, declaration.Revision, snapshotHash,
                "Inspected synthetic entitlement", "cash-dividend-eligibility:" + snapshotHash, snapshotHash,
                CorporateActionCashDividendAttestationPolicy.Confirmation, Preparer, scope, Authority: new(true, false));
            var receipt = new CorporateActionCashDividendAttestationDto(PreparerAttestationApprovalBoundary.NewReceiptId(),
                processingCase.CaseId, attached.Projection.ProjectionId, processingCase.Version, declaration.DeclarationId,
                declaration.Revision, declaration.PolicyVersion, declaration.PolicyHash, Guid.NewGuid(),
                CorporateActionCashDividendAttestationValidation.RelatedEventKey(securityId, EffectiveDate), EffectiveDate,
                source.ProviderId, source.SourceEventId, source.SourceEventVersion, source.EvidenceHash!, snapshotHash, snapshot,
                gross, whole, PeriodStart, Preparer, DateTimeOffset.UtcNow, request.Reason, request.EvidenceReference,
                request.EvidenceHash, request.Confirmation, CorporateActionCashDividendAttestationPolicy.Disclosure,
                AccountRoleBinding: Binding);
            return new(securityId, request, receipt, Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(request)));
        }

        internal async Task<long> ScalarAsync(string sql)
        {
            await using var connection = new NpgsqlConnection(Options.ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = sql.Replace("{schema}", '"' + Options.Schema + '"', StringComparison.Ordinal);
            return Convert.ToInt64(await command.ExecuteScalarAsync());
        }
        public ValueTask DisposeAsync() => _server.DisposeAsync();
    }
}
