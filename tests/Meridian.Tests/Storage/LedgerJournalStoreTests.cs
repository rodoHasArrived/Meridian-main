using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Tests.Storage;

public sealed class LedgerJournalStoreTests
{
    [Fact]
    public void LedgerJournalStoreOptions_DefaultsToLedgerSchemaAndPeriodLocking()
    {
        var options = new LedgerJournalStoreOptions();

        options.SchemaName.Should().Be("ledger");
        options.EnablePeriodLocking.Should().BeTrue();
        options.ConnectionString.Should().BeEmpty();
    }

    [Fact]
    public void AddLedgerJournalStore_RegistersOptionsAndStore()
    {
        const string connectionString = "Host=localhost;Database=meridian_test;Username=meridian;Password=secret";
        var services = new ServiceCollection();

        services.AddLedgerJournalStore(connectionString);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<LedgerJournalStoreOptions>().ConnectionString.Should().Be(connectionString);
        var journalStore = provider.GetRequiredService<ILedgerJournalStore>();
        journalStore.Should().BeOfType<PostgresLedgerJournalStore>();
        provider.GetRequiredService<ITransactionalLedgerJournalStore>().Should().BeSameAs(journalStore);
        provider.GetRequiredService<IAccountingConfigurationStore>().Should().BeOfType<PostgresAccountingConfigurationStore>();
        provider.GetRequiredService<IAccountingActionAuditStore>().Should().BeOfType<PostgresAccountingConfigurationStore>();
        provider.GetRequiredService<LedgerMigrationRunner>().Should().NotBeNull();
        provider.GetRequiredService<ILedgerBookService>().Should().BeOfType<PostgresLedgerBookService>();
    }

    [Fact]
    public void AddLedgerJournalStore_RejectsBlankConnectionString()
    {
        var services = new ServiceCollection();

        var act = () => services.AddLedgerJournalStore(" ");

        act.Should().Throw<ArgumentException>()
            .WithMessage("*connection string*");
    }

    [Fact]
    public async Task AppendAsync_UnbalancedJournal_RejectsBeforeOpeningConnection()
    {
        var store = new PostgresLedgerJournalStore(new LedgerJournalStoreOptions());
        var write = new LedgerJournalEntryWrite(
            BuildUnbalancedJournalEntry(),
            AggregateId: Guid.NewGuid(),
            PeriodId: Guid.NewGuid());

        var act = () => store.AppendAsync(write);

        await act.Should().ThrowAsync<LedgerValidationException>()
            .WithMessage("*not balanced*");
    }

    [Fact]
    public void PostingGuard_OpenPeriod_AllowsOriginatingAndAdjustmentEntries()
    {
        var period = BuildAccountingPeriod("Open");
        var originating = BuildBalancedJournalWrite(period.PeriodId);
        var adjustment = originating with { PostingKind = LedgerPostingKindDto.Adjustment };

        var originatingAct = () => LedgerPeriodPostingGuard.Validate(originating, period);
        var adjustmentAct = () => LedgerPeriodPostingGuard.Validate(adjustment, period);

        originatingAct.Should().NotThrow();
        adjustmentAct.Should().NotThrow();
    }

    [Fact]
    public void PostingGuard_SoftClosedPeriod_RejectsOriginatingEntry()
    {
        var period = BuildAccountingPeriod("SoftClosed");
        var write = BuildBalancedJournalWrite(period.PeriodId);

        var act = () => LedgerPeriodPostingGuard.Validate(write, period);

        act.Should().Throw<LedgerValidationException>()
            .WithMessage("*soft-closed*Adjustment*");
    }

    [Fact]
    public void PostingGuard_SoftClosedPeriod_AllowsAdjustmentEntry()
    {
        var period = BuildAccountingPeriod("SoftClosed");
        var write = BuildBalancedJournalWrite(period.PeriodId) with
        {
            PostingKind = LedgerPostingKindDto.Adjustment,
            AdjustmentApproval = BuildApprovedAdjustmentApproval()
        };

        var act = () => LedgerPeriodPostingGuard.Validate(write, period);

        act.Should().NotThrow();
    }

    [Fact]
    public void PostingGuard_SoftClosedAdjustment_RequiresApprovalMetadata()
    {
        var period = BuildAccountingPeriod("SoftClosed");
        var write = BuildBalancedJournalWrite(period.PeriodId) with
        {
            PostingKind = LedgerPostingKindDto.Adjustment
        };

        var act = () => LedgerPeriodPostingGuard.Validate(write, period);

        act.Should().Throw<LedgerValidationException>()
            .WithMessage("*require approved governance metadata*");
    }

    [Fact]
    public void PostingGuard_AdjustmentApprovalMetadata_RequiresApprovedStatus()
    {
        var period = BuildAccountingPeriod("Open");
        var write = BuildBalancedJournalWrite(period.PeriodId) with
        {
            PostingKind = LedgerPostingKindDto.Adjustment,
            AdjustmentApproval = BuildApprovedAdjustmentApproval() with
            {
                Status = LedgerAdjustmentApprovalStatusDto.Pending
            }
        };

        var act = () => LedgerPeriodPostingGuard.Validate(write, period);

        act.Should().Throw<LedgerValidationException>()
            .WithMessage("*must be Approved*");
    }

    [Fact]
    public void PostingGuard_OriginatingEntry_RejectsAdjustmentApprovalMetadata()
    {
        var period = BuildAccountingPeriod("Open");
        var write = BuildBalancedJournalWrite(period.PeriodId) with
        {
            AdjustmentApproval = BuildApprovedAdjustmentApproval()
        };

        var act = () => LedgerPeriodPostingGuard.Validate(write, period);

        act.Should().Throw<LedgerValidationException>()
            .WithMessage("*approval metadata*not an Adjustment*");
    }

    [Fact]
    public void PostingGuard_HardClosedPeriod_RejectsAdjustmentEntry()
    {
        var period = BuildAccountingPeriod("HardClosed");
        var write = BuildBalancedJournalWrite(period.PeriodId) with
        {
            PostingKind = LedgerPostingKindDto.Adjustment
        };

        var act = () => LedgerPeriodPostingGuard.Validate(write, period);

        act.Should().Throw<LedgerValidationException>()
            .WithMessage("*hard-closed*no postings*");
    }

    [Fact]
    public void PostingGuard_UnknownStatus_RejectsEntry()
    {
        var period = BuildAccountingPeriod("Archived");
        var write = BuildBalancedJournalWrite(period.PeriodId);

        var act = () => LedgerPeriodPostingGuard.Validate(write, period);

        act.Should().Throw<LedgerValidationException>()
            .WithMessage("*unsupported status 'Archived'*");
    }

    [Fact]
    public void PostingGuard_TimestampOutsidePeriod_RejectsEntry()
    {
        var period = BuildAccountingPeriod("Open");
        var write = BuildBalancedJournalWrite(
            period.PeriodId,
            DateTimeOffset.Parse("2026-02-01T00:00:00Z"));

        var act = () => LedgerPeriodPostingGuard.Validate(write, period);

        act.Should().Throw<LedgerValidationException>()
            .WithMessage("*outside accounting period*2026-P01*");
    }

    [Fact]
    public void PostingGuard_InstrumentEntry_AllowsApprovedMappedSecurityMasterLineage()
    {
        var period = BuildAccountingPeriod("Open");
        var write = BuildInstrumentJournalWrite(period.PeriodId);

        var act = () => LedgerPeriodPostingGuard.Validate(write, period);

        act.Should().NotThrow();
    }

    [Theory]
    [MemberData(nameof(MultiAssetInstrumentGuardCases))]
    public void PostingGuard_MultiAssetInstrumentEntry_AllowsApprovedMappedSecurityMasterLineage(InstrumentLedgerGuardCase asset)
    {
        var period = BuildAccountingPeriod("Open");
        var write = BuildInstrumentJournalWrite(period.PeriodId, asset);

        var act = () => LedgerPeriodPostingGuard.Validate(write, period);

        act.Should().NotThrow();
    }

    [Theory]
    [MemberData(nameof(MultiAssetInstrumentGuardCases))]
    public void PostingGuard_MultiAssetInstrumentEntry_RequiresSecurityMasterProvenance(InstrumentLedgerGuardCase asset)
    {
        var period = BuildAccountingPeriod("Open");
        var write = BuildInstrumentJournalWrite(
            period.PeriodId,
            asset,
            includeSecurityMasterProvenance: false);

        var act = () => LedgerPeriodPostingGuard.Validate(write, period);

        act.Should().Throw<LedgerValidationException>()
            .WithMessage($"*without Security Master provenance for security '{asset.SecurityId}'*");
    }

    [Theory]
    [MemberData(nameof(MultiAssetInstrumentGuardCases))]
    public void PostingGuard_MultiAssetInstrumentEntry_RejectsMismatchedSecurityMasterIdentity(InstrumentLedgerGuardCase asset)
    {
        var period = BuildAccountingPeriod("Open");
        var write = BuildInstrumentJournalWrite(
            period.PeriodId,
            asset,
            provenanceSecurityId: Guid.Parse("FD064111-2940-4FF8-B4E7-48C053F97F40"));

        var act = () => LedgerPeriodPostingGuard.Validate(write, period);

        act.Should().Throw<LedgerValidationException>()
            .WithMessage($"*without Security Master provenance for security '{asset.SecurityId}'*");
    }

    [Theory]
    [MemberData(nameof(MultiAssetInstrumentGuardCases))]
    public void PostingGuard_MultiAssetInstrumentEntry_RequiresApprovedSecurityMasterEvidence(InstrumentLedgerGuardCase asset)
    {
        var period = BuildAccountingPeriod("Open");
        var write = BuildInstrumentJournalWrite(
            period.PeriodId,
            asset,
            includeApprovalEvidence: false);

        var act = () => LedgerPeriodPostingGuard.Validate(write, period);

        act.Should().Throw<LedgerValidationException>()
            .WithMessage("*without approved Security Master evidence*");
    }

    [Theory]
    [MemberData(nameof(MultiAssetInstrumentGuardCases))]
    public void PostingGuard_MultiAssetInstrumentEntry_RequiresActiveSecurityMasterStatus(InstrumentLedgerGuardCase asset)
    {
        var period = BuildAccountingPeriod("Open");
        var write = BuildInstrumentJournalWrite(
            period.PeriodId,
            asset,
            includeActiveSecurityMasterStatus: false);

        var act = () => LedgerPeriodPostingGuard.Validate(write, period);

        act.Should().Throw<LedgerValidationException>()
            .WithMessage("*without active Security Master status evidence*");
    }

    [Theory]
    [MemberData(nameof(MultiAssetInstrumentGuardCases))]
    public void PostingGuard_MultiAssetInstrumentEntry_RequiresLedgerMappingEvidence(InstrumentLedgerGuardCase asset)
    {
        var period = BuildAccountingPeriod("Open");
        var write = BuildInstrumentJournalWrite(
            period.PeriodId,
            asset,
            includeLedgerMapping: false);

        var act = () => LedgerPeriodPostingGuard.Validate(write, period);

        act.Should().Throw<LedgerValidationException>()
            .WithMessage("*without a Security Master ledger mapping reference*");
    }

    [Theory]
    [MemberData(nameof(MultiAssetInstrumentGuardCases))]
    public void PostingGuard_MultiAssetInstrumentEntry_RejectsLedgerMappingForDifferentInstrument(InstrumentLedgerGuardCase asset)
    {
        var period = BuildAccountingPeriod("Open");
        var write = BuildInstrumentJournalWrite(
            period.PeriodId,
            asset,
            ledgerMappingReference: "ledger-map:unrelated-security-gaap");

        var act = () => LedgerPeriodPostingGuard.Validate(write, period);

        act.Should().Throw<LedgerValidationException>()
            .WithMessage($"*{asset.Symbol}*without a Security Master ledger mapping tied to the resolved symbol or security id*");
    }

    [Fact]
    public void PostingGuard_InstrumentEntry_RequiresSecurityMasterProvenance()
    {
        var period = BuildAccountingPeriod("Open");
        var write = BuildInstrumentJournalWrite(
            period.PeriodId,
            includeSecurityMasterProvenance: false);

        var act = () => LedgerPeriodPostingGuard.Validate(write, period);

        act.Should().Throw<LedgerValidationException>()
            .WithMessage("*without Security Master provenance*");
    }

    [Fact]
    public void PostingGuard_InstrumentEntry_RequiresSecurityMasterApprovalEvidence()
    {
        var period = BuildAccountingPeriod("Open");
        var write = BuildInstrumentJournalWrite(
            period.PeriodId,
            includeApprovalEvidence: false);

        var act = () => LedgerPeriodPostingGuard.Validate(write, period);

        act.Should().Throw<LedgerValidationException>()
            .WithMessage("*without approved Security Master evidence*");
    }

    [Fact]
    public void PostingGuard_InstrumentEntry_RequiresActiveSecurityMasterStatus()
    {
        var period = BuildAccountingPeriod("Open");
        var write = BuildInstrumentJournalWrite(
            period.PeriodId,
            includeActiveSecurityMasterStatus: false);

        var act = () => LedgerPeriodPostingGuard.Validate(write, period);

        act.Should().Throw<LedgerValidationException>()
            .WithMessage("*without active Security Master status evidence*");
    }

    [Fact]
    public void PostingGuard_InstrumentEntry_RejectsActiveStatusFromProvenanceOnly()
    {
        var period = BuildAccountingPeriod("Open");
        var write = BuildInstrumentJournalWrite(
            period.PeriodId,
            includeActiveSecurityMasterStatus: false,
            includeProvenanceActiveSecurityStatus: true);

        var act = () => LedgerPeriodPostingGuard.Validate(write, period);

        act.Should().Throw<LedgerValidationException>()
            .WithMessage("*without active Security Master status evidence*");
    }

    [Fact]
    public void PostingGuard_InstrumentEntry_RequiresSecurityMasterLedgerMapping()
    {
        var period = BuildAccountingPeriod("Open");
        var write = BuildInstrumentJournalWrite(
            period.PeriodId,
            includeLedgerMapping: false);

        var act = () => LedgerPeriodPostingGuard.Validate(write, period);

        act.Should().Throw<LedgerValidationException>()
            .WithMessage("*without a Security Master ledger mapping reference*");
    }

    [Fact]
    public void PostingGuard_InstrumentEntry_RequiresLedgerMappingForResolvedInstrument()
    {
        var period = BuildAccountingPeriod("Open");
        var write = BuildInstrumentJournalWrite(
            period.PeriodId,
            ledgerMappingReference: "ledger-map:generic-securities");

        var act = () => LedgerPeriodPostingGuard.Validate(write, period);

        act.Should().Throw<LedgerValidationException>()
            .WithMessage("*without a Security Master ledger mapping tied to the resolved symbol or security id*");
    }

    [Fact]
    public void PostingGuard_MetadataSymbol_RequiresLedgerMappingForResolvedInstrument()
    {
        var period = BuildAccountingPeriod("Open");
        var write = BuildInstrumentJournalWrite(
            period.PeriodId,
            ledgerMappingReference: "ledger-map:generic-securities",
            includeInstrumentLine: false);

        var act = () => LedgerPeriodPostingGuard.Validate(write, period);

        act.Should().Throw<LedgerValidationException>()
            .WithMessage("*declares instrument symbol 'AAPL' without a Security Master ledger mapping tied to the resolved symbol or security id*");
    }

    [Fact]
    public void PostingGuard_InstrumentEntry_RequiresLineSymbolForSecurityMasterLineage()
    {
        var period = BuildAccountingPeriod("Open");
        var write = BuildInstrumentJournalWrite(
            period.PeriodId,
            instrumentLineSymbol: null);

        var act = () => LedgerPeriodPostingGuard.Validate(write, period);

        act.Should().Throw<LedgerValidationException>()
            .WithMessage("*without an instrument symbol for Security Master lineage*");
    }

    [Fact]
    public void PostingGuard_InstrumentEntry_RejectsLineageThatOnlyMatchesBySymbolSubstring()
    {
        var period = BuildAccountingPeriod("Open");
        var write = BuildInstrumentJournalWrite(period.PeriodId);
        var firstLine = write.Entry.Lines[0];
        var mutatedLines = write.Entry.Lines
            .Select(line => line.EntryId == firstLine.EntryId
                ? new LedgerEntry(
                    line.EntryId,
                    line.JournalEntryId,
                    line.Timestamp,
                    line.Account with { Symbol = "A" },
                    line.Debit,
                    line.Credit,
                    line.Description)
                : line)
            .ToArray();
        var mutatedEntry = new JournalEntry(
            write.Entry.JournalEntryId,
            write.Entry.Timestamp,
            write.Entry.Description,
            mutatedLines,
            write.Entry.Metadata with { Symbol = "A" });

        var act = () => LedgerPeriodPostingGuard.Validate(write with { Entry = mutatedEntry }, period);

        act.Should().Throw<LedgerValidationException>()
            .WithMessage("*declares instrument symbol 'A' without matching Security Master lineage*");
    }

    [Fact]
    public void PostingGuard_InstrumentEntry_RejectsMultipleSymbolsWithSingleSecurityMasterId()
    {
        var period = BuildAccountingPeriod("Open");
        var write = BuildInstrumentJournalWrite(
            period.PeriodId,
            extraInstrumentLineSymbol: "MSFT");

        var act = () => LedgerPeriodPostingGuard.Validate(write, period);

        act.Should().Throw<LedgerValidationException>()
            .WithMessage("*multiple instrument symbols with a single Security Master security id*");
    }

    [Fact]
    public void PostingGuard_InstrumentEntry_RejectsMetadataSymbolThatDiffersFromLineSymbol()
    {
        var period = BuildAccountingPeriod("Open");
        var write = BuildInstrumentJournalWrite(
            period.PeriodId,
            metadataSymbol: "MSFT");

        var act = () => LedgerPeriodPostingGuard.Validate(write, period);

        act.Should().Throw<LedgerValidationException>()
            .WithMessage("*multiple instrument symbols with a single Security Master security id*");
    }

    [Fact]
    public void LedgerJournalMigration_DefinesJournalTablesAndLineageColumns()
    {
        var sql = ReadMigration("V_ledger_001__journal_entries.sql");

        sql.Should().Contain("create table if not exists __SCHEMA__.journal_entries");
        sql.Should().Contain("create table if not exists __SCHEMA__.journal_legs");
        sql.Should().Contain("unique (journal_entry_id)");
        sql.Should().Contain("aggregate_id uuid not null");
        sql.Should().Contain("period_id uuid not null");
        sql.Should().Contain("command_id uuid null");
        sql.Should().Contain("correlation_id uuid null");
    }

    [Fact]
    public void LedgerPeriodMigration_DefinesAccountingPeriodsAndCloseAudit()
    {
        var sql = ReadMigration("V_ledger_002__accounting_periods.sql");

        sql.Should().Contain("create table if not exists __SCHEMA__.accounting_periods");
        sql.Should().Contain("optimistic_version bigint not null default 1");
        sql.Should().Contain("create table if not exists __SCHEMA__.period_close_events");
        sql.Should().Contain("period_version bigint not null");
    }

    [Fact]
    public void LedgerBasisLineageMigration_DefinesJournalBasisColumnsAndIndexes()
    {
        var sql = ReadMigration("V_ledger_005__journal_basis_lineage.sql");

        sql.Should().Contain("add column if not exists accounting_basis text not null default 'Primary'");
        sql.Should().Contain("add column if not exists accounting_policy_id text not null default 'legacy-v1'");
        sql.Should().Contain("add column if not exists rule_id text null");
        sql.Should().Contain("add column if not exists source_event_id uuid null");
        sql.Should().Contain("ix_journal_entries_basis_period");
        sql.Should().Contain("ix_journal_entries_source_event");
        sql.Should().Contain("ix_journal_legs_basis_account");
    }

    [Fact]
    public void LedgerPostingKindMigration_DefinesJournalPostingKindColumnsAndIndexes()
    {
        var sql = ReadMigration("V_ledger_006__journal_posting_kind.sql");

        sql.Should().Contain("add column if not exists posting_kind text not null default 'Originating'");
        sql.Should().Contain("ck_journal_entries_posting_kind");
        sql.Should().Contain("ck_journal_legs_posting_kind");
        sql.Should().Contain("ix_journal_entries_period_posting_kind");
        sql.Should().Contain("ix_journal_legs_period_posting_kind");
    }

    [Fact]
    public void LedgerAdjustmentApprovalMigration_DefinesJournalApprovalMetadataColumnsAndIndexes()
    {
        var sql = ReadMigration("V_ledger_007__journal_adjustment_approval_metadata.sql");

        sql.Should().Contain("add column if not exists adjustment_approval_metadata jsonb null");
        sql.Should().Contain("ck_journal_entries_adjustment_approval_metadata");
        sql.Should().Contain("ck_journal_legs_adjustment_approval_metadata");
        sql.Should().Contain("ix_journal_entries_adjustment_approval_id");
        sql.Should().Contain("ix_journal_entries_period_adjustment_approval_status");
    }

    [Fact]
    public void LedgerOperationsContinuityMigration_DefinesWorkflowSnapshotAndAuditTables()
    {
        var sql = ReadMigration("V_ledger_008__operations_continuity.sql");

        sql.Should().Contain("create table if not exists __SCHEMA__.operations_continuity_workflows");
        sql.Should().Contain("workflow_json jsonb not null");
        sql.Should().Contain("derived_status text not null");
        sql.Should().Contain("create table if not exists __SCHEMA__.operations_continuity_audit");
        sql.Should().Contain("audit_json jsonb not null");
        sql.Should().Contain("ux_operations_continuity_audit_previous_hash");
        sql.Should().Contain("ux_operations_continuity_open_workflow");
    }

    [Fact]
    public void LedgerTaxLotPersistenceMigration_DefinesPoliciesLotsAndOpenLotIndexes()
    {
        var sql = ReadMigration("V_ledger_009__tax_lot_persistence.sql");

        sql.Should().Contain("create table if not exists __SCHEMA__.tax_lot_policies");
        sql.Should().Contain("relief_method text not null");
        sql.Should().Contain("ck_tax_lot_policies_relief_method");
        sql.Should().Contain("ux_tax_lot_policies_book_account_effective");
        sql.Should().Contain("create table if not exists __SCHEMA__.tax_lots");
        sql.Should().Contain("original_quantity numeric(38, 12) not null");
        sql.Should().Contain("open_quantity numeric(38, 12) not null");
        sql.Should().Contain("ck_tax_lots_quantity");
        sql.Should().Contain("ux_tax_lots_book_account_lot");
        sql.Should().Contain("ix_tax_lots_book_account_open");
        sql.Should().Contain("ix_tax_lots_source_journal_entry");
    }

    [Fact]
    public async Task SaveTaxLotPolicyAsync_RejectsInvalidPolicyBeforeOpeningConnection()
    {
        var store = new PostgresLedgerJournalStore(new LedgerJournalStoreOptions());
        var policy = new LedgerAccountTaxLotPolicyRecord(
            PolicyRecordId: Guid.Empty,
            LedgerBookId: Guid.NewGuid(),
            Account: new LedgerAccount("Assets:Securities", LedgerAccountType.Asset, "AAPL"),
            ReliefMethod: LedgerTaxLotReliefMethod.Hifo,
            PolicyId: "tax-lot-policy-aapl-hifo",
            EffectiveDate: new DateOnly(2026, 1, 1),
            CreatedAt: DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            UpdatedAt: DateTimeOffset.Parse("2026-01-01T00:00:00Z"));

        var act = () => store.SaveTaxLotPolicyAsync(policy);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*policy record id*");
    }

    [Fact]
    public async Task SaveTaxLotAsync_RejectsInvalidLotQuantityBeforeOpeningConnection()
    {
        var store = new PostgresLedgerJournalStore(new LedgerJournalStoreOptions());
        var lot = new LedgerTaxLotRecord(
            TaxLotRecordId: Guid.NewGuid(),
            LedgerBookId: Guid.NewGuid(),
            Account: new LedgerAccount("Assets:Securities", LedgerAccountType.Asset, "AAPL"),
            LotId: "lot-aapl-1",
            AcquiredDate: new DateOnly(2026, 1, 5),
            OriginalQuantity: 100m,
            OpenQuantity: 125m,
            UnitCost: 145m,
            Currency: "USD",
            CreatedAt: DateTimeOffset.Parse("2026-01-05T00:00:00Z"),
            UpdatedAt: DateTimeOffset.Parse("2026-01-05T00:00:00Z"));

        var act = () => store.SaveTaxLotAsync(lot);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>()
            .WithMessage("*Open tax-lot quantity*");
    }

    private static LedgerJournalEntryWrite BuildBalancedJournalWrite(
        Guid periodId,
        DateTimeOffset? timestamp = null)
    {
        var journalEntryId = Guid.NewGuid();
        var occurredAt = timestamp ?? DateTimeOffset.Parse("2026-01-31T21:00:00Z");
        const string description = "Balanced month-end test posting";
        return new LedgerJournalEntryWrite(
            new JournalEntry(
                journalEntryId,
                occurredAt,
                description,
                [
                    new LedgerEntry(
                        Guid.NewGuid(),
                        journalEntryId,
                        occurredAt,
                        new LedgerAccount("Cash", LedgerAccountType.Asset),
                        debit: 100m,
                        credit: 0m,
                        description),
                    new LedgerEntry(
                        Guid.NewGuid(),
                        journalEntryId,
                        occurredAt,
                        new LedgerAccount("Management fees", LedgerAccountType.Revenue),
                        debit: 0m,
                        credit: 100m,
                        description)
                ]),
            AggregateId: Guid.NewGuid(),
            PeriodId: periodId);
    }

    private static LedgerJournalEntryWrite BuildInstrumentJournalWrite(
        Guid periodId,
        bool includeSecurityMasterProvenance = true,
        bool includeApprovalEvidence = true,
        bool includeActiveSecurityMasterStatus = true,
        bool includeLedgerMapping = true,
        string? instrumentLineSymbol = "AAPL",
        string? ledgerMappingReference = null,
        string? extraInstrumentLineSymbol = null,
        string? metadataSymbol = "AAPL",
        bool includeInstrumentLine = true,
        bool includeProvenanceActiveSecurityStatus = false)
    {
        var journalEntryId = Guid.NewGuid();
        var occurredAt = DateTimeOffset.Parse("2026-01-31T21:00:00Z");
        var securityId = Guid.Parse("BCE42470-8F6B-4BD3-9FC7-B8763F8B48B1");
        var symbol = metadataSymbol ?? "AAPL";
        const string description = "Approved Security Master instrument posting";
        var mapping = includeLedgerMapping ? ledgerMappingReference ?? "ledger-map:aapl-gaap-securities" : "missing";
        var approval = includeApprovalEvidence ? "sm-approval:aapl-controller" : "missing";
        var activeStatus = includeActiveSecurityMasterStatus ? "security-status:active" : "missing";
        var provenanceStatus = includeProvenanceActiveSecurityStatus ? ";security-status:active" : string.Empty;
        var provenance = includeApprovalEvidence
            ? $"security-master:{securityId:N};snapshot:test-source-hash;approved:true{provenanceStatus}"
            : $"security-master:{securityId:N};snapshot:test-source-hash{provenanceStatus}";
        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["securityMasterLineage"] = $"{symbol}:{securityId:N}:{mapping}:{approval}:{activeStatus}:{provenance}"
        };
        if (includeSecurityMasterProvenance)
        {
            tags["securityMasterProvenance"] = provenance;
        }

        var lines = new List<LedgerEntry>();
        if (includeInstrumentLine)
        {
            lines.Add(new LedgerEntry(
                Guid.NewGuid(),
                journalEntryId,
                occurredAt,
                new LedgerAccount("Securities", LedgerAccountType.Asset, instrumentLineSymbol),
                debit: 100m,
                credit: 0m,
                description));
        }
        else
        {
            lines.Add(new LedgerEntry(
                Guid.NewGuid(),
                journalEntryId,
                occurredAt,
                new LedgerAccount("Cash", LedgerAccountType.Asset),
                debit: 100m,
                credit: 0m,
                description));
        }

        if (!string.IsNullOrWhiteSpace(extraInstrumentLineSymbol))
        {
            lines.Add(new LedgerEntry(
                Guid.NewGuid(),
                journalEntryId,
                occurredAt,
                new LedgerAccount("Securities", LedgerAccountType.Asset, extraInstrumentLineSymbol),
                debit: 50m,
                credit: 0m,
                description));
        }

        lines.Add(new LedgerEntry(
            Guid.NewGuid(),
            journalEntryId,
            occurredAt,
            new LedgerAccount("Cash", LedgerAccountType.Asset),
            debit: 0m,
            credit: lines.Sum(static line => line.Debit),
            description));

        return new LedgerJournalEntryWrite(
            new JournalEntry(
                journalEntryId,
                occurredAt,
                description,
                lines,
                new JournalEntryMetadata(
                    ActivityType: "operations-continuity",
                    Symbol: metadataSymbol,
                    SecurityId: securityId,
                    LedgerBook: "fund-close",
                    Tags: tags)),
            AggregateId: Guid.NewGuid(),
            PeriodId: periodId,
            CommandId: Guid.NewGuid(),
            AccountingPolicyId: "legacy-v1",
            AccountingPolicyVersion: "legacy-v1",
            RuleId: "operations-continuity-instrument-posting",
            RuleVersion: "v1",
            SourceEventId: Guid.NewGuid(),
            PostingKind: LedgerPostingKindDto.Originating);
    }

    private static LedgerJournalEntryWrite BuildInstrumentJournalWrite(
        Guid periodId,
        InstrumentLedgerGuardCase asset,
        bool includeSecurityMasterProvenance = true,
        bool includeApprovalEvidence = true,
        bool includeActiveSecurityMasterStatus = true,
        bool includeLedgerMapping = true,
        string? ledgerMappingReference = null,
        Guid? provenanceSecurityId = null)
    {
        var journalEntryId = Guid.NewGuid();
        var occurredAt = DateTimeOffset.Parse("2026-01-31T21:00:00Z");
        var description = $"Approved Security Master {asset.AssetClass} posting";
        var mapping = includeLedgerMapping
            ? ledgerMappingReference ?? asset.LedgerMappingReference
            : "missing";
        var approval = includeApprovalEvidence
            ? $"sm-approval:{asset.AssetClass.ToLowerInvariant()}-controller"
            : "missing";
        var activeStatus = includeActiveSecurityMasterStatus ? "security-status:active" : "missing";
        var provenanceId = provenanceSecurityId ?? asset.SecurityId;
        var provenance = includeApprovalEvidence
            ? $"security-master:{provenanceId:N};asset-class:{asset.AssetClass};snapshot:{asset.AssetClass.ToLowerInvariant()}-source-hash;approved:true"
            : $"security-master:{provenanceId:N};asset-class:{asset.AssetClass};snapshot:{asset.AssetClass.ToLowerInvariant()}-source-hash";
        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["securityMasterLineage"] = $"{asset.Symbol}:{asset.SecurityId:N}:asset-class:{asset.AssetClass}:{mapping}:{approval}:{activeStatus}:{provenance}",
            ["assetClass"] = asset.AssetClass
        };
        if (includeSecurityMasterProvenance)
        {
            tags["securityMasterProvenance"] = provenance;
        }

        return new LedgerJournalEntryWrite(
            new JournalEntry(
                journalEntryId,
                occurredAt,
                description,
                [
                    new LedgerEntry(
                        Guid.NewGuid(),
                        journalEntryId,
                        occurredAt,
                        new LedgerAccount(asset.AccountName, LedgerAccountType.Asset, asset.Symbol),
                        debit: asset.Debit,
                        credit: 0m,
                        description),
                    new LedgerEntry(
                        Guid.NewGuid(),
                        journalEntryId,
                        occurredAt,
                        new LedgerAccount(asset.OffsetAccountName, LedgerAccountType.Asset),
                        debit: 0m,
                        credit: asset.Debit,
                        description)
                ],
                new JournalEntryMetadata(
                    ActivityType: "multi-asset-ledger-proof",
                    Symbol: asset.Symbol,
                    SecurityId: asset.SecurityId,
                    LedgerBook: "fund-close",
                    Tags: tags)),
            AggregateId: Guid.NewGuid(),
            PeriodId: periodId,
            CommandId: Guid.NewGuid(),
            AccountingPolicyId: "legacy-v1",
            AccountingPolicyVersion: "legacy-v1",
            RuleId: $"multi-asset-{asset.AssetClass.ToLowerInvariant()}-posting",
            RuleVersion: "v1",
            SourceEventId: Guid.NewGuid(),
            PostingKind: LedgerPostingKindDto.Originating);
    }

    public static IEnumerable<object[]> MultiAssetInstrumentGuardCases()
    {
        foreach (var asset in InstrumentLedgerGuardCases)
        {
            yield return [asset];
        }
    }

    private static readonly IReadOnlyList<InstrumentLedgerGuardCase> InstrumentLedgerGuardCases =
    [
        new(
            AssetClass: "Equity",
            Symbol: "AAPL",
            SecurityId: Guid.Parse("A1111111-1111-4111-8111-111111111111"),
            AccountName: "Securities",
            OffsetAccountName: "Cash",
            LedgerMappingReference: "ledger-map:aapl-equity-gaap",
            Debit: 100m),
        new(
            AssetClass: "Option",
            Symbol: "AAPL260117C00150000",
            SecurityId: Guid.Parse("A2222222-2222-4222-8222-222222222222"),
            AccountName: "Option Premium Asset",
            OffsetAccountName: "Cash",
            LedgerMappingReference: "ledger-map:aapl260117c00150000-option-gaap",
            Debit: 12.50m),
        new(
            AssetClass: "Future",
            Symbol: "ESZ6",
            SecurityId: Guid.Parse("A3333333-3333-4333-8333-333333333333"),
            AccountName: "Futures MTM Settlement",
            OffsetAccountName: "Variation Margin",
            LedgerMappingReference: "ledger-map:esz6-future-gaap",
            Debit: 25m),
        new(
            AssetClass: "FxSpot",
            Symbol: "EURUSD",
            SecurityId: Guid.Parse("A4444444-4444-4444-8444-444444444444"),
            AccountName: "FxSpot Position",
            OffsetAccountName: "Cash",
            LedgerMappingReference: "ledger-map:eurusd-fxspot-gaap",
            Debit: 75m),
        new(
            AssetClass: "Bond",
            Symbol: "US91282CJT89",
            SecurityId: Guid.Parse("A5555555-5555-4555-8555-555555555555"),
            AccountName: "Accrued Interest Receivable",
            OffsetAccountName: "Interest Income",
            LedgerMappingReference: "ledger-map:us91282cjt89-bond-gaap",
            Debit: 9.25m),
        new(
            AssetClass: "DirectLoan",
            Symbol: "DL-ACME-2026",
            SecurityId: Guid.Parse("A6666666-6666-4666-8666-666666666666"),
            AccountName: "LoanPrincipal",
            OffsetAccountName: "Cash",
            LedgerMappingReference: "ledger-map:dl-acme-2026-directloan-gaap",
            Debit: 250m),
        new(
            AssetClass: "CustomAsset",
            Symbol: "CA-WIND-01",
            SecurityId: Guid.Parse("A7777777-7777-4777-8777-777777777777"),
            AccountName: "Securities",
            OffsetAccountName: "Cash",
            LedgerMappingReference: "ledger-map:ca-wind-01-customasset-gaap",
            Debit: 150m),
        new(
            AssetClass: "OtherSecurity",
            Symbol: "OS-SIDEPOCKET-01",
            SecurityId: Guid.Parse("A8888888-8888-4888-8888-888888888888"),
            AccountName: "Securities",
            OffsetAccountName: "Cash",
            LedgerMappingReference: "ledger-map:os-sidepocket-01-othersecurity-gaap",
            Debit: 60m)
    ];

    public sealed record InstrumentLedgerGuardCase(
        string AssetClass,
        string Symbol,
        Guid SecurityId,
        string AccountName,
        string OffsetAccountName,
        string LedgerMappingReference,
        decimal Debit);

    private static LedgerAccountingPeriod BuildAccountingPeriod(string status) =>
        new(
            PeriodId: Guid.NewGuid(),
            LedgerBookId: Guid.NewGuid(),
            FiscalYear: 2026,
            PeriodNo: 1,
            Label: "2026-P01",
            StartDate: new DateOnly(2026, 1, 1),
            EndDate: new DateOnly(2026, 1, 31),
            Status: status,
            OpenedAt: DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            ClosedAt: null,
            Version: 1);

    private static LedgerAdjustmentApprovalMetadataDto BuildApprovedAdjustmentApproval() =>
        new(
            ApprovalId: "approval-ledger-adjustment-1",
            Status: LedgerAdjustmentApprovalStatusDto.Approved,
            ApprovedBy: "fund-controller",
            ApprovedAt: DateTimeOffset.Parse("2026-01-31T22:00:00Z"),
            ReasonCode: "month-end-true-up",
            GovernanceCaseId: "case-ledger-close-1",
            EvidenceLink: "evidence://ledger/adjustment/approval-1",
            Notes: "Controller approved soft-close true-up.");

    private static JournalEntry BuildUnbalancedJournalEntry()
    {
        var journalEntryId = Guid.NewGuid();
        var timestamp = DateTimeOffset.Parse("2026-01-31T21:00:00Z");
        const string description = "Unbalanced month-end test posting";
        return new JournalEntry(
            journalEntryId,
            timestamp,
            description,
            [
                new LedgerEntry(
                    Guid.NewGuid(),
                    journalEntryId,
                    timestamp,
                    new LedgerAccount("Cash", LedgerAccountType.Asset),
                    debit: 100m,
                    credit: 0m,
                    description),
            ]);
    }

    private static string ReadMigration(string fileName)
    {
        var root = FindRepoRoot();
        var path = Path.Combine(root, "src", "Meridian.Storage", "Ledger", "Migrations", fileName);
        return File.ReadAllText(path);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "src", "Meridian.Storage")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate Meridian repository root.");
    }
}
