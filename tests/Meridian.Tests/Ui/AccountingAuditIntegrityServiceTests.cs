using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Ui;

/// <summary>
/// W9-GOV-008 criterion three verification tooling. The report must say exactly what it checked:
/// verified chains, broken chains with their classification, chains this composition does not keep,
/// and stores it could not read are four different answers.
/// </summary>
public sealed class AccountingAuditIntegrityServiceTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("meridian-audit-integrity-").FullName;

    private string SnapshotPath => Path.Combine(_root, "accounting-configuration.json");

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A leaked temp directory must never fail a test run.
        }
    }

    [Fact]
    public async Task VerifyAsync_ReportsAnIntactFileChainAsVerifiedAndTheAbsentLedgerChainAsNotConfigured()
    {
        var store = new FileAccountingConfigurationStore(SnapshotPath);
        await store.AppendAsync(AuditEvent("post-journal"));
        await store.AppendAsync(AuditEvent("close-period"));

        var report = await new AccountingAuditIntegrityService(store, ledgerEventAuditVerifier: null).VerifyAsync();

        report.AllVerified.Should().BeTrue();
        var accounting = report.Chains.Single(chain => chain.Chain == AuditChainIdentifiers.AccountingActions);
        accounting.Status.Should().Be(AuditChainIntegrityStatus.Verified);
        accounting.LinksChecked.Should().Be(2);
        report.Chains.Single(chain => chain.Chain == AuditChainIdentifiers.LedgerEvents)
            .Status.Should().Be(AuditChainIntegrityStatus.NotConfigured);
    }

    [Fact]
    public async Task VerifyAsync_ReportsATamperedFileChainAsBrokenWithoutAppending()
    {
        var store = new FileAccountingConfigurationStore(SnapshotPath);
        await store.AppendAsync(AuditEvent("post-journal"));
        await store.AppendAsync(AuditEvent("close-period"));
        var snapshot = JsonNode.Parse(File.ReadAllText(SnapshotPath))!.AsObject();
        snapshot["auditEvents"]!.AsArray()[0]!["actor"] = "someone-else";
        File.WriteAllText(SnapshotPath, snapshot.ToJsonString());
        var tampered = File.ReadAllText(SnapshotPath);

        var report = await new AccountingAuditIntegrityService(store, ledgerEventAuditVerifier: null).VerifyAsync();

        report.AllVerified.Should().BeFalse();
        var accounting = report.Chains.Single(chain => chain.Chain == AuditChainIdentifiers.AccountingActions);
        accounting.Status.Should().Be(AuditChainIntegrityStatus.Broken);
        accounting.FailureKind.Should().Be(nameof(AccountingAuditChainStatus.EventMutated));
        accounting.FailedSequence.Should().NotBeNull();
        File.ReadAllText(SnapshotPath).Should().Be(tampered, "verification must never repair or extend the chain");
    }

    [Fact]
    public async Task VerifyAsync_ReportsAnEditedHeadJournalAsBrokenAndAnUnreadableOneAsUnavailable()
    {
        var store = new FileAccountingConfigurationStore(SnapshotPath);
        await store.AppendAsync(AuditEvent("post-journal"));
        var lines = File.ReadAllLines(store.AuditChainAnchorPath);
        var edited = JsonNode.Parse(lines[0])!.AsObject();
        edited["entryHash"] = new string('0', 64);

        File.WriteAllLines(store.AuditChainAnchorPath, [edited.ToJsonString(), .. lines[1..]]);
        var tampered = (await new AccountingAuditIntegrityService(store, ledgerEventAuditVerifier: null).VerifyAsync())
            .Chains.Single(chain => chain.Chain == AuditChainIdentifiers.AccountingActions);

        File.WriteAllLines(store.AuditChainAnchorPath, ["{not json", .. lines[1..]]);
        var unreadable = (await new AccountingAuditIntegrityService(store, ledgerEventAuditVerifier: null).VerifyAsync())
            .Chains.Single(chain => chain.Chain == AuditChainIdentifiers.AccountingActions);

        var newer = JsonNode.Parse(lines[0])!.AsObject();
        newer["schemaVersion"] = 99;
        File.WriteAllLines(store.AuditChainAnchorPath, [newer.ToJsonString(), .. lines[1..]]);
        var unsupported = (await new AccountingAuditIntegrityService(store, ledgerEventAuditVerifier: null).VerifyAsync())
            .Chains.Single(chain => chain.Chain == AuditChainIdentifiers.AccountingActions);

        tampered.Status.Should().Be(AuditChainIntegrityStatus.Broken, "a readable line failed its own hash");
        unsupported.Status.Should().Be(AuditChainIntegrityStatus.Unavailable, "a newer build's journal could not be checked");
        unsupported.FailureKind.Should().Be(nameof(AccountingAuditChainStatus.UnsupportedSchemaVersion));
        tampered.FailureKind.Should().Be("AnchorJournalTampered");
        unreadable.Status.Should().Be(AuditChainIntegrityStatus.Unavailable, "a line this build cannot read proves nothing");
    }

    [Fact]
    public async Task VerifyAsync_DoesNotCountAnUnchainedStoreAsVerified()
    {
        var report = await new AccountingAuditIntegrityService(
            new InMemoryAccountingActionAuditStore(),
            ledgerEventAuditVerifier: null).VerifyAsync();

        report.AllVerified.Should().BeFalse("no configured chain was verified");
        report.Chains.Should().OnlyContain(chain => chain.Status == AuditChainIntegrityStatus.NotConfigured);
    }

    [Fact]
    public async Task VerifyAsync_ReportsLedgerIntegrityFailureAsBrokenAndUnreadableStoresAsUnavailable()
    {
        var broken = await new AccountingAuditIntegrityService(
            new ThrowingAccountingVerifier(new IOException("disk at /secret/path")),
            new ThrowingLedgerVerifier(new LedgerValidationException("Ledger audit integrity failure: link 7 does not bind."))).VerifyAsync();

        broken.AllVerified.Should().BeFalse();
        var ledger = broken.Chains.Single(chain => chain.Chain == AuditChainIdentifiers.LedgerEvents);
        ledger.Status.Should().Be(AuditChainIntegrityStatus.Broken);
        ledger.Detail.Should().Contain("link 7");
        var accounting = broken.Chains.Single(chain => chain.Chain == AuditChainIdentifiers.AccountingActions);
        accounting.Status.Should().Be(AuditChainIntegrityStatus.Unavailable);
        accounting.FailureKind.Should().Be(nameof(IOException));
        accounting.Detail.Should().NotContain("/secret/path");
    }

    [Fact]
    public async Task VerifyAsync_ReportsALedgerBreakWithHowFarTheScanGotAndWhereItStopped()
    {
        var failure = new LedgerValidationException("Ledger audit integrity failure: Ledger audit event 8 has been mutated, removed, or reordered.");
        failure.Data[LedgerEventAuditFailureData.FailedSequence] = 8L;
        failure.Data[LedgerEventAuditFailureData.ChainedEventsVerified] = 7L;
        failure.Data[LedgerEventAuditFailureData.UnprotectedGenesisFacts] = 3L;
        failure.Data[LedgerEventAuditFailureData.FailureKind] = LedgerEventAuditFailureKinds.EventMutated;

        var report = await new AccountingAuditIntegrityService(accountingAuditStore: null, new ThrowingLedgerVerifier(failure)).VerifyAsync();

        var ledger = report.Chains.Single(chain => chain.Chain == AuditChainIdentifiers.LedgerEvents);
        ledger.Status.Should().Be(AuditChainIntegrityStatus.Broken);
        ledger.LinksChecked.Should().Be(7);
        ledger.FailedSequence.Should().Be(8);
        ledger.UnprotectedPreChainCount.Should().Be(3);
        ledger.FailureKind.Should().Be(LedgerEventAuditFailureKinds.EventMutated);
    }

    [Fact]
    public async Task VerifyAsync_ReportsALedgerChainUnderUnsupportedHashingRulesAsUnverifiedNotBroken()
    {
        var failure = new LedgerValidationException("Ledger audit integrity failure: The retained ledger audit head has an unsupported version.");
        failure.Data[LedgerEventAuditFailureData.UnsupportedSchemaVersion] = true;

        var report = await new AccountingAuditIntegrityService(accountingAuditStore: null, new ThrowingLedgerVerifier(failure)).VerifyAsync();

        report.AllVerified.Should().BeFalse();
        var ledger = report.Chains.Single(chain => chain.Chain == AuditChainIdentifiers.LedgerEvents);
        ledger.Status.Should().Be(AuditChainIntegrityStatus.Unavailable);
        ledger.FailureKind.Should().Be("UnsupportedSchemaVersion");
    }

    [Fact]
    public async Task VerifyAsync_ReportsAChainItCannotReadUnderThisBuildsRulesAsUnverifiedNotBroken()
    {
        var report = await new AccountingAuditIntegrityService(
            new FixedAccountingVerifier(new AccountingAuditChainVerification(
                AccountingAuditChainStatus.UnsupportedSchemaVersion, LinksChecked: 0, PreChainEventCount: 0, "Chain schema version 2 is not version 1.")),
            ledgerEventAuditVerifier: null).VerifyAsync();

        report.AllVerified.Should().BeFalse();
        var accounting = report.Chains.Single(chain => chain.Chain == AuditChainIdentifiers.AccountingActions);
        accounting.Status.Should().Be(AuditChainIntegrityStatus.Unavailable);
        accounting.FailureKind.Should().Be(nameof(AccountingAuditChainStatus.UnsupportedSchemaVersion));
    }

    [Fact]
    public async Task VerifyAsync_ReportsAVerifiedLedgerChainWithItsUnprotectedGenesisFacts()
    {
        var report = await new AccountingAuditIntegrityService(
            accountingAuditStore: null,
            new FixedLedgerVerifier(new LedgerEventAuditVerification(12, 3, DateTimeOffset.UnixEpoch))).VerifyAsync();

        report.AllVerified.Should().BeTrue();
        var ledger = report.Chains.Single(chain => chain.Chain == AuditChainIdentifiers.LedgerEvents);
        ledger.Status.Should().Be(AuditChainIntegrityStatus.Verified);
        ledger.LinksChecked.Should().Be(12);
        ledger.UnprotectedPreChainCount.Should().Be(3);
    }

    [Fact]
    public async Task VerifyAsync_PropagatesCancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => new AccountingAuditIntegrityService(
            new ThrowingAccountingVerifier(new OperationCanceledException(cts.Token)),
            ledgerEventAuditVerifier: null).VerifyAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static AccountingActionAuditEventDto AuditEvent(string action)
        => new(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            Actor: "operator@example.test",
            Action: action,
            FundProfileId: "fund-alpha",
            LedgerBookId: null,
            CorrelationId: null,
            BeforeHash: new string('0', 64),
            AfterHash: new string('1', 64),
            ValidationIssues: [],
            EvidenceLinks: [],
            CompanyId: "company-alpha",
            ReportGroupPrincipalIds: null,
            TenantId: "tenant-alpha");

    private sealed class ThrowingAccountingVerifier(Exception exception) : IAccountingActionAuditStore, IAccountingAuditChainVerifier
    {
        public Task AppendAsync(AccountingActionAuditEventDto auditEvent, CancellationToken ct = default) => throw exception;

        public Task<IReadOnlyList<AccountingActionAuditEventDto>> ListAsync(
            string? fundProfileId = null, Guid? ledgerBookId = null, CancellationToken ct = default,
            string? tenantId = null, string? companyId = null) => throw exception;

        public Task<AccountingAuditChainVerification> VerifyAuditChainAsync(CancellationToken ct = default) => throw exception;
    }

    private sealed class FixedAccountingVerifier(AccountingAuditChainVerification result) : IAccountingActionAuditStore, IAccountingAuditChainVerifier
    {
        public Task AppendAsync(AccountingActionAuditEventDto auditEvent, CancellationToken ct = default) => Task.CompletedTask;

        public Task<IReadOnlyList<AccountingActionAuditEventDto>> ListAsync(
            string? fundProfileId = null, Guid? ledgerBookId = null, CancellationToken ct = default,
            string? tenantId = null, string? companyId = null) => Task.FromResult<IReadOnlyList<AccountingActionAuditEventDto>>([]);

        public Task<AccountingAuditChainVerification> VerifyAuditChainAsync(CancellationToken ct = default) => Task.FromResult(result);
    }

    private sealed class ThrowingLedgerVerifier(Exception exception) : ILedgerEventAuditVerifier
    {
        public Task<LedgerEventAuditVerification> VerifyLedgerEventAuditAsync(CancellationToken ct = default) => throw exception;
    }

    private sealed class FixedLedgerVerifier(LedgerEventAuditVerification result) : ILedgerEventAuditVerifier
    {
        public Task<LedgerEventAuditVerification> VerifyLedgerEventAuditAsync(CancellationToken ct = default) => Task.FromResult(result);
    }
}
