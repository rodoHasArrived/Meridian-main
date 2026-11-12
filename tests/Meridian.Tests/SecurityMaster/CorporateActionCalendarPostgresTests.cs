using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Application.SecurityMaster.CorporateActions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Meridian.TestSupport;
using Meridian.Tests.Storage;
using Meridian.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.SecurityMaster;

[Trait("Category", "Integration")]
public sealed class CorporateActionCalendarPostgresTests
{
    [LedgerDatabaseFact]
    public async Task DeadlineCaptureImportCorrectionWithdrawal_ReloadsDurablyAndReplaysExactCommands()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var ct = timeout.Token;
        await using var server = await PostgresTestServer.CreateAsync("MERIDIAN_LEDGER_CONNECTION_STRING", ct: ct);
        var options = new SecurityMasterOptions
        {
            ConnectionString = server.ConnectionString,
            Schema = server.CreateSchemaName("deadline_calendar"),
            PreloadProjectionCache = false
        };
        await SecurityMasterTestCapabilityFloor.InitializeAndActivateAsync(server, options, ct);
        var securityId = Guid.NewGuid();
        await CreateSecurityAsync(options, securityId, ct);
        var store = new PostgresCorporateActionOperationsStore(options);
        var operations = new CorporateActionOperationsService(store);
        var scope = new CorporateActionCaseScopeDto("calendar-tenant", "calendar-company", FinancialAccountId: "broker-account");
        var date = new DateOnly(2026, 11, 12);
        var action = new CorporateActionDto(Guid.NewGuid(), securityId, "Dividend", date.AddMonths(-1), date,
            0.24m, "USD", null, null, null, null, null, null, null);
        var hash = CorporateActionEconomicFingerprint.Compute(action);
        var proposal = await operations.RecordSourceProposalAsync(new(action,
            new("fixture-source", Guid.NewGuid().ToString(), "v1", DateTimeOffset.UtcNow, hash, "document://source/notice",
                CorporateActionProviderReleaseStatusDto.AcceptanceEligible), "operator"), ct);
        var accepted = await store.AcceptSourceProposalAsync(new(proposal.ProposalId, proposal.Version, "accept-source", scope, "operator"),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, new string('a', 64), ct);
        var service = new CorporateActionCalendarService(operations, store);
        var value = new CorporateActionElectionDeadlineDto(Guid.NewGuid(), "BrokerElection", "Fixture broker",
            At: new DateTimeOffset(2026, 11, 12, 16, 0, 0, TimeSpan.FromHours(-5)), TimeZoneId: "America/New_York", FinancialAccountId: "broker-account", SourceText: "Election instructions due at 4pm EST on 12 November 2026");
        var request = new RecordCorporateActionDeadlineRequestDto(accepted.Case.CaseId, accepted.Case.Version, "record-deadline",
            scope.TenantId, scope.CompanyId, value, "document://broker/notice", hash, "Reviewed the named account's actual broker cutoff", scope);
        var wrongAccount = async () => await service.RecordAsync(request with
        {
            IdempotencyKey = "wrong-account",
            Deadline = value with { FinancialAccountId = "another-broker-account" }
        }, "operator", null, ct);
        await wrongAccount.Should().ThrowAsync<CorporateActionScopeMismatchException>();
        var recorded = await service.RecordAsync(request, "operator", "trace", ct);

        // Reconstruct the production store/service; no process-local fixture can satisfy this read.
        var reloadedStore = new PostgresCorporateActionOperationsStore(options);
        var reloaded = new CorporateActionCalendarService(new CorporateActionOperationsService(reloadedStore), reloadedStore);
        var history = await reloaded.GetDeadlinesAsync(request.CaseId, scope.TenantId, scope.CompanyId, ct);
        history.History.Should().ContainSingle().Which.Deadline.At.Should().Be(value.At);
        history.History.Should().OnlyContain(e => e.EvidenceAssurance == "OperatorRecorded");
        var replay = await reloaded.RecordAsync(request, "operator", "trace", ct);
        replay.Replayed.Should().BeTrue();
        replay.Evidence.EvidenceId.Should().Be(recorded.Evidence.EvidenceId);
        var calendar = await reloaded.GetCalendarAsync(scope.TenantId, scope.CompanyId, date, date,
            account: "broker-account", ct: ct);
        calendar.Items.Should().ContainSingle(e => e.DateKind == "BrokerElection");
        calendar.Items.Should().ContainSingle(e => e.DateKind == "PaymentDate");
        calendar.Items.Should().NotContain(e => e.DateKind == "ExDate");

        var corrected = await reloaded.RecordAsync(request with
        {
            ExpectedVersion = recorded.Case.Version,
            IdempotencyKey = "correct-deadline",
            Deadline = value with { At = value.At!.Value.AddHours(-1), SupersedesEvidenceId = recorded.Evidence.EvidenceId }
        }, "operator", "correction", ct);
        var staleCorrection = async () => await reloaded.RecordAsync(request with
        {
            ExpectedVersion = corrected.Case.Version,
            IdempotencyKey = "stale-predecessor",
            Deadline = value with { SupersedesEvidenceId = recorded.Evidence.EvidenceId }
        }, "operator", null, ct);
        await staleCorrection.Should().ThrowAsync<CorporateActionStateConflictException>();

        var csv = $"{CorporateActionDeadlinePolicy.CsvHeader}\n{Guid.NewGuid()},IssuerElection,Fixture issuer,2026-11-12,,,,,document://issuer/notice,{hash},Reviewed source,,false,Issuer election deadline 12 November";
        var import = new CorporateActionDeadlineImportRequestDto(request.CaseId, corrected.Case.Version, "csv-deadlines",
            scope.TenantId, scope.CompanyId, csv, ScopeAssertion: scope);
        var preview = await reloaded.PreviewImportAsync(import, ct);
        preview.Errors.Should().BeEmpty();
        var imported = await reloaded.ImportAsync(import with { PreviewHash = preview.PreviewHash }, "operator", "import-trace", ct);
        imported.Complete.Should().BeTrue();
        var importReplay = await reloaded.ImportAsync(import with { PreviewHash = preview.PreviewHash }, "operator", "import-trace", ct);
        importReplay.Results.Should().OnlyContain(r => r.Replayed);

        var withdrawal = await reloaded.RecordAsync(request with
        {
            ExpectedVersion = imported.Results[^1].Case.Version,
            IdempotencyKey = "withdraw-deadline",
            Deadline = CorporateActionDeadlinePolicy.Read(corrected.Evidence.Metadata) with
            { Withdrawn = true, SupersedesEvidenceId = corrected.Evidence.EvidenceId }
        }, "operator", "withdrawal", ct);
        var final = await reloaded.GetCalendarAsync(scope.TenantId, scope.CompanyId, date, date, ct: ct);
        final.Items.Should().NotContain(e => e.DateKind == "BrokerElection");
        final.Items.Should().ContainSingle(e => e.DateKind == "IssuerElection");
        (await reloaded.GetDeadlinesAsync(request.CaseId, scope.TenantId, scope.CompanyId, ct)).History.Should().HaveCount(4);
        var foreignRead = async () => await reloaded.GetDeadlinesAsync(request.CaseId, scope.TenantId, "another-company", ct);
        await foreignRead.Should().ThrowAsync<CorporateActionNotFoundException>();

        var frozen = await operations.TransitionCaseAsync(new(request.CaseId, withdrawal.Case.Version, "cancel-case", scope.TenantId,
            scope.CompanyId, "Cancelled", "operator", "Fixture case cancelled", Authority: new(true, false, false, false, false), ScopeAssertion: scope), ct);
        var frozenCapture = async () => await reloaded.RecordAsync(request with
        {
            ExpectedVersion = frozen.Case.Version,
            IdempotencyKey = "frozen-capture",
            Deadline = value with { SeriesId = Guid.NewGuid() }
        }, "operator", null, ct);
        await frozenCapture.Should().ThrowAsync<CorporateActionStateConflictException>();
    }

    private static async Task CreateSecurityAsync(SecurityMasterOptions options, Guid id, CancellationToken ct)
    {
        var events = new PostgresSecurityMasterEventStore(options, NullLogger<PostgresSecurityMasterEventStore>.Instance);
        var snapshots = new PostgresSecurityMasterSnapshotStore(options);
        var store = new PostgresSecurityMasterStore(options);
        var rebuilder = new SecurityMasterAggregateRebuilder(events, snapshots);
        var service = new SecurityMasterService(events, snapshots, store, rebuilder, options, NullLogger<SecurityMasterService>.Instance);
        await service.CreateAsync(new CreateSecurityRequest(id, "Equity",
            JsonSerializer.SerializeToElement(new
            {
                displayName = "Deadline fixture equity",
                currency = "USD",
                countryOfRisk = "US",
                issuerName = "Fixture issuer",
                exchange = "XNYS",
                lotSize = 1,
                tickSize = 0.01m
            }),
            JsonSerializer.SerializeToElement(new { shareClass = "Common" }),
            [new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, "DL" + id.ToString("N"), true, DateTimeOffset.UtcNow)],
            DateTimeOffset.UtcNow, "test", "operator", null, "Deadline fixture"), ct);
    }
}
