using FluentAssertions;
using Meridian.Application.FundStructure;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Services;
using Meridian.Contracts.Tenancy;
using Meridian.Entities.FundStructure;
using Meridian.FinancialOperations.Intercompany;
using Meridian.Ledger;
using Meridian.Storage.FundStructure;
using Meridian.Storage.Ledger;
using Meridian.Tests.TestSupport;
using Moq;
using Npgsql;

namespace Meridian.Tests.Storage;

/// <summary>
/// One pre-controls entity book holding an intercompany receivable that was posted before agreement
/// attribution existed. The ledger, account binding, chart, book ownership, attribution and control
/// stores are the PostgreSQL ones; lifecycle and intercompany posting authorities are the production
/// classes. Only the organization graph, the lifecycle history and the legal agreement registry are
/// synthetic in-memory stand-ins, as in the neighbouring entity-control fixtures.
/// </summary>
internal sealed class LegacyIntercompanyControlsFixture : IAsyncDisposable
{
    internal const string Tenant = "tenant-a";
    internal const string Company = "company-a";
    internal const string Fund = "legacy-controls-fund";
    internal const string ReceivableNode = "ic-receivable";
    internal const string ReceivableAccount = "Intercompany Receivable";
    internal static readonly DateOnly SourceDate = new(2026, 5, 15);
    private static readonly DateTimeOffset Formed = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ArrangementStart = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Reviewed = new(2026, 5, 20, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgresLedgerJournalStore _plainJournals;
    private readonly IFundScopeTenantAccessor _tenants = new TenantAccessor(Tenant);

    private LegacyIntercompanyControlsFixture(LedgerPostgresTestDatabase database, PostgresLedgerJournalStore plainJournals)
    {
        Database = database;
        _plainJournals = plainJournals;
    }

    internal LedgerPostgresTestDatabase Database { get; }
    /// <summary>The ledger store composed with the real lifecycle and intercompany authorities and preflights.</summary>
    internal PostgresLedgerJournalStore Journals { get; private set; } = null!;
    internal PostgresLegacyIntercompanyAttributionStore Attributions { get; private set; } = null!;
    internal LegacyIntercompanyAttributionValidator Validator { get; private set; } = null!;
    internal InMemoryEntityLifecycleStore Lifecycle { get; } = new();
    internal EntityLifecycleRevisionDto Baseline { get; private set; } = null!;
    internal LedgerBookRecord Book { get; private set; } = null!;
    internal LedgerAccountingPeriod Period { get; private set; } = null!;
    internal IntercompanyAccountBindingDto Binding { get; private set; } = null!;
    internal ResolvedIntercompanyAccountingArrangementDto Agreement { get; private set; } = null!;
    internal Guid EntityId { get; } = Guid.NewGuid();
    internal Guid CounterpartyId { get; } = Guid.NewGuid();

    internal static async Task<LegacyIntercompanyControlsFixture> CreateAsync(CancellationToken ct = default)
    {
        var database = await LedgerPostgresTestDatabase.CreateAsync(ct);
        try
        {
            var tenants = new TenantAccessor(Tenant);
            var fixture = new LegacyIntercompanyControlsFixture(database, new PostgresLedgerJournalStore(database.Options, tenants));
            await fixture.SeedAsync(ct);
            return fixture;
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }
    }

    private async Task SeedAsync(CancellationToken ct)
    {
        var owners = new PostgresFundProfileTenancyRegistry(Database.Options);
        await owners.BindAsync(Fund, Tenant, Company, ct);
        Book = await _plainJournals.SaveLedgerBookAsync(new(Guid.NewGuid(), Fund, EntityId, FundStructureNodeKindDto.Entity,
            "Legacy entity book", "USD", Formed, Formed), ct);
        // Books created by the current store start Enforced. This book stands for one that existed
        // before the entity-control migration, which that migration seeds as LegacyWarning.
        (await ExecuteAsync($"""
            update "{Database.Options.SchemaName}".ledger_book_controls set mode='LegacyWarning'
            where ledger_book_id=@id and mode='Enforced' and version=1 and activated_at_utc is null and migration_receipt_id is null;
            """, Book.LedgerBookId, ct)).Should().Be(1);

        await Database.AccountingConfigurationStore.SaveAsync(new AccountingConfigurationWorkspaceDto(Fund, Book.LedgerBookId,
            AccountingConfigurationStatusDto.Active, "legacy-controls-v1", Reviewed, [],
            [new ChartOfAccountsNodeDto(ReceivableNode, "Assets:" + ReceivableAccount, ReceivableAccount, "Asset",
                IntercompanyRole: IntercompanyAccountRoleDto.Receivable)], [], [], [], [], TenantId: Tenant, CompanyId: Company), ct);
        var bindings = new PostgresIntercompanyAccountBindingStore(Database.Options, _tenants);
        Binding = await bindings.AppendAsync(new IntercompanyAccountBindingDto(Guid.NewGuid(), Guid.NewGuid(), 1, Book.LedgerBookId,
            ReceivableNode, IntercompanyAccountRoleDto.Receivable, "Assets:" + ReceivableAccount, ReceivableAccount, "Asset",
            new DateOnly(2026, 1, 1), null, [], Tenant, Company, "operator", Reviewed, "Reviewed legacy receivable mapping"), null, ct);

        Agreement = IntercompanyArrangementTestData.Create(Guid.NewGuid(), Guid.NewGuid(), IntercompanyAccountingTemplateKindDto.Loan,
            EntityId, CounterpartyId, "USD",
            [new(EntityId, Book.LedgerBookId, ReceivableNode, "ic-payable"), new(CounterpartyId, Guid.NewGuid(), "ar", "ap")],
            Guid.NewGuid(), ArrangementStart);
        var arrangements = new InMemoryIntercompanyAccountingArrangementStore();
        var approved = Agreement.Arrangement;
        var draft = approved with { Version = 1, Status = IntercompanyAccountingArrangementStatusDto.Draft, ApprovedBy = null, ApprovedAt = null };
        await arrangements.AppendAsync(Tenant, 0, new(Guid.NewGuid(), new string('a', 64),
            IntercompanyAccountingArrangementActionDto.Create, "mapping-preparer", ArrangementStart, null, draft), ct);
        await arrangements.AppendAsync(Tenant, 1, new(Guid.NewGuid(), new string('b', 64),
            IntercompanyAccountingArrangementActionDto.Submit, "mapping-preparer", ArrangementStart, null,
            draft with { Version = 2, Status = IntercompanyAccountingArrangementStatusDto.Submitted }), ct);
        await arrangements.AppendAsync(Tenant, 2, new(Guid.NewGuid(), new string('c', 64),
            IntercompanyAccountingArrangementActionDto.Approve, "mapping-reviewer", ArrangementStart, null, approved), ct);
        var legal = IntercompanyArrangementTestData.LegalAuthority(Agreement);
        var legalLease = new Mock<IIntercompanyAccountingArrangementLegalLease>();
        legalLease.Setup(lease => lease.ResolveExact(Agreement.Arrangement.CanonicalReference)).Returns(Agreement.LegalRevision);
        legalLease.Setup(lease => lease.ResolveEffective(Agreement.Arrangement.CanonicalReference, It.IsAny<DateOnly>()))
            .Returns(Agreement.LegalRevision);
        legal.Setup(authority => authority.AcquireLeaseAsync(It.IsAny<string>(),
                It.Is<IReadOnlyList<IntercompanyCanonicalAgreementReferenceDto>>(references =>
                    references.Count == 1 && references[0] == Agreement.Arrangement.CanonicalReference),
                It.IsAny<LegalEntityReadAuthorityProof?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(legalLease.Object);
        var service = new IntercompanyAccountingArrangementService(arrangements,
            Mock.Of<IIntercompanyAccountingArrangementScopeAuthority>(), legal.Object);

        Validator = new LegacyIntercompanyAttributionValidator(_plainJournals, bindings, arrangements, legal.Object);
        Attributions = new PostgresLegacyIntercompanyAttributionStore(Database.Options, _tenants, Validator);

        Baseline = await Lifecycle.AppendAsync(EntityLifecyclePolicy.Seal(new EntityLifecycleRevisionDto(Tenant, EntityId, 1,
            DateTimeOffset.UtcNow, "synthetic-owner", "Evidenced fixture formation", "fixture:formation", "", "",
            [new(Guid.NewGuid(), Formed, null, LegalEntityLifecycleStatusDto.Active, LegalEntityLifecycleEventKindDto.Formation)])), 0, ct);
        var entity = new LegalEntitySummaryDto(EntityId, LegalEntityTypeDto.Vehicle, "LEGACY", "Legacy controls entity",
            "US", "USD", true, Formed, null);
        var structure = new Mock<IFundStructureService>();
        structure.Setup(service => service.GetOrganizationStructureAsync(It.IsAny<OrganizationStructureQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrganizationStructureGraphDto([], [], [], [], [], [], [entity], [], [], [], [], []));
        var lifecycleAuthority = new LifecycleLedgerPostingAuthority(structure.Object, Lifecycle, () => null);
        var intercompanyAuthority = new IntercompanyLedgerPostingAuthority(arrangements, service, bindings,
            Database.AccountingConfigurationStore, _plainJournals, owners, Attributions);
        Journals = new PostgresLedgerJournalStore(Database.Options, _tenants,
            postingAuthorities: () => [lifecycleAuthority, intercompanyAuthority],
            controlPreflights: () => [lifecycleAuthority, intercompanyAuthority]);
        Period = await Journals.SavePeriodAsync(new(Guid.NewGuid(), Book.LedgerBookId, 2026, 5, "2026-05",
            new(2026, 5, 1), new(2026, 5, 31), "Open", Formed, null, 0), 0, ct: ct);
    }

    /// <summary>A receivable from the counterparty with no agreement, mapping or transaction identity on the line.</summary>
    internal LedgerJournalEntryWrite UnattributedReceivable(decimal amount = 100m)
    {
        var id = Guid.NewGuid();
        var at = new DateTimeOffset(SourceDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        const string description = "Legacy intercompany loan drawdown";
        var parties = new LedgerLineDimensionSet(EntityId: EntityId.ToString("D"), CounterpartyId: CounterpartyId.ToString("D"));
        var own = new LedgerLineDimensionSet(EntityId: EntityId.ToString("D"), BookId: Book.LedgerBookId.ToString("D"));
        var journal = new JournalEntry(id, at, description,
            [new(Guid.NewGuid(), id, at, new(ReceivableAccount, LedgerAccountType.Asset), amount, 0m, description, parties),
             new(Guid.NewGuid(), id, at, new("Assets:Cash", LedgerAccountType.Asset), 0m, amount, description, own)],
            new JournalEntryMetadata { EffectiveDate = SourceDate, IdempotencyKey = $"legacy-controls:{id:D}" });
        return new(journal, EntityId, Period.PeriodId, LedgerBookId: Book.LedgerBookId);
    }

    /// <summary>An ordinary capital contribution that touches no intercompany account.</summary>
    internal LedgerJournalEntryWrite OrdinaryContribution(decimal amount = 250m)
    {
        var id = Guid.NewGuid();
        var at = new DateTimeOffset(SourceDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        const string description = "Capital contribution";
        var own = new LedgerLineDimensionSet(EntityId: EntityId.ToString("D"), BookId: Book.LedgerBookId.ToString("D"));
        var journal = new JournalEntry(id, at, description,
            [new(Guid.NewGuid(), id, at, new("Assets:Cash", LedgerAccountType.Asset), amount, 0m, description, own),
             new(Guid.NewGuid(), id, at, new("Equity:Capital", LedgerAccountType.Equity), 0m, amount, description, own)],
            new JournalEntryMetadata { EffectiveDate = SourceDate, IdempotencyKey = $"legacy-controls:{id:D}" });
        return new(journal, EntityId, Period.PeriodId, LedgerBookId: Book.LedgerBookId);
    }

    /// <summary>Posts the legacy receivable while the book still only warns and returns its retained source line.</summary>
    internal async Task<(LedgerJournalEntryRecord Record, LedgerEntry Line)> PostUnattributedReceivableAsync(CancellationToken ct = default)
    {
        var write = UnattributedReceivable();
        await Journals.AppendAsync(write, ct);
        var record = (await ReadJournalsAsync(ct)).Single(item => item.Entry.JournalEntryId == write.Entry.JournalEntryId);
        return (record, record.Entry.Lines.Single(line => line.Account.Name == ReceivableAccount));
    }

    internal Task<IReadOnlyList<LedgerJournalEntryRecord>> ReadJournalsAsync(CancellationToken ct = default)
        => _plainJournals.QueryAsync(new LedgerJournalEntryQuery(LedgerBookId: Book.LedgerBookId), ct);

    /// <summary>The reviewer's assertion that the retained line belongs to the approved loan arrangement.</summary>
    internal LegacyIntercompanyAttributionDto Attribution(LedgerJournalEntryRecord record, LedgerEntry line) => new(
        Guid.NewGuid(), 1, Book.LedgerBookId, record.Entry.JournalEntryId, line.EntryId,
        LegacyIntercompanyAttributionRules.SourceDigest(Book, record, line), Agreement.ArrangementId, Agreement.RevisionId,
        Binding.RevisionId, Binding.ChartAccountNodeId, "legacy-loan-drawdown-1", "preparer", "reviewer", Reviewed,
        "evidence:reviewed-opening-schedule", Tenant, Company)
    {
        IntercompanyAgreementId = Agreement.CanonicalAgreementId,
        IntercompanyAgreementVersion = Agreement.CanonicalAgreementVersion,
        IntercompanyAgreementPayloadHash = Agreement.CanonicalPayloadHash
    };

    internal static LegacyIntercompanyAttributionDto Revoke(LegacyIntercompanyAttributionDto previous) => previous with
    {
        AttributionId = Guid.NewGuid(),
        Revision = previous.Revision + 1,
        PreviousAttributionId = previous.AttributionId,
        Revoked = true,
        ReviewMode = LegacyIntercompanyReviewModeDto.OperatorReviewed,
        PreparedBy = "controller",
        ApprovedBy = "controller",
        ApprovedAt = previous.ApprovedAt.AddMinutes(1),
        EvidenceReference = "evidence:reviewed-revocation",
        ReviewRationale = "Retract the exact retained assertion after review."
    };

    /// <summary>The same durable table and source validation, opened by another caller or after a restart.</summary>
    internal PostgresLegacyIntercompanyAttributionStore AttributionStoreFor(string? tenant)
        => new(Database.Options, new TenantAccessor(tenant), Validator);

    internal async Task<int> ExecuteAsync(string sql, Guid? id = null, CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(Database.Options.ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        if (id is { } value)
            command.Parameters.AddWithValue("id", value);
        return await command.ExecuteNonQueryAsync(ct);
    }

    internal async Task<long> CountAsync(string table, CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(Database.Options.ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand($"select count(*) from \"{Database.Options.SchemaName}\".\"{table}\"", connection);
        return (long)(await command.ExecuteScalarAsync(ct))!;
    }

    public ValueTask DisposeAsync() => Database.DisposeAsync();

    internal sealed class TenantAccessor(string? tenant) : IFundScopeTenantAccessor
    {
        public string? ResolveCallerTenant() => tenant;
    }
}
