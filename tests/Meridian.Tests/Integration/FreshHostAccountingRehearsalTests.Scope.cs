using System.Text.Json;
using Meridian.Application.FundStructure;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Operations;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Services;
using Meridian.Contracts.Tenancy;
using Meridian.Entities.FundStructure;
using Meridian.Execution.Events;
using Meridian.FinancialOperations.Intercompany;
using Meridian.FinancialOperations.Ledger;
using Meridian.Ledger;
using Meridian.PortfolioRecords.FundAccounts;
using Meridian.Storage.AssetOperations;
using Meridian.Storage.FundStructure;
using Meridian.Storage.Ledger;
using Meridian.Storage.SecurityMaster;
using Meridian.Tests.Storage;
using Meridian.Ui.Shared.Services;
using Moq;

namespace Meridian.Tests.Integration;

public sealed partial class FreshHostAccountingRehearsalTests
{
    private const string Tenant = "fresh-host-synthetic-tenant";
    private const string Company = "fresh-host-synthetic-company";
    private const string Fund = "fresh-host-synthetic-fund";
    private const string Symbol = "AAPL";

    // Only configuration and persisted identities cross a host boundary. No store, portfolio,
    // ledger, authority cache, strategy instance or service is retained in this record.
    private sealed record Scope(string Root, LedgerJournalStoreOptions Ledger,
        FundStructureStoreOptions Structure, SecurityMasterOptions Security, AssetOperationsOptions Positions,
        Guid AccountId, Guid SecurityId, LedgerAccountingPeriod Period,
        AutomatedTradeFillAccountingOptions Options, TradeFillLedgerPostingContext Context);

    private static async Task<Scope> SeedScopeAsync(LedgerPostgresTestDatabase database, string root, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var start = new DateOnly(now.Year, now.Month, 1);
        var formed = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var account = Guid.NewGuid();
        var entity = Guid.NewGuid();
        var organization = Guid.NewGuid();
        var business = Guid.NewGuid();
        var fundNode = Guid.NewGuid();
        var structureOptions = new FundStructureStoreOptions
        { ConnectionString = database.Options.ConnectionString, Schema = database.CreateSchemaName("rehearsal_structure") };
        await new FundStructureMigrationRunner(structureOptions).EnsureMigratedAsync(ct);
        var graph = new PostgresFundStructureStore(structureOptions);
        await graph.UpsertOrganizationAsync(new(organization, "REHEARSAL", "Synthetic organization", "USD", true,
            formed, null, [business]), ct);
        await graph.UpsertBusinessAsync(new(business, organization, BusinessKindDto.FundManager, "MANAGER",
            "Synthetic manager", "USD", true, formed, null, [], [fundNode], []), ct);
        await graph.UpsertFundAsync(new(fundNode, business, "FUND", "Synthetic fund", "USD", true,
            formed, null, [], [], [entity], [], []), ct);
        await graph.UpsertLegalEntityAsync(new(entity, LegalEntityTypeDto.Vehicle, "ENTITY", "Synthetic owner",
            "US", "USD", true, formed, null), ct);
        foreach (var node in new[] { organization, business, fundNode, entity })
            await graph.StampNodeTenantAsync(node, Tenant, ct);
        await new PostgresEntityLifecycleStore(structureOptions).AppendAsync(EntityLifecycleRevisionIntegrity.Seal(
            new(Tenant, entity, 1, formed, "synthetic-lifecycle-reviewer", "Reviewed synthetic formation",
                "document://rehearsal/formation", "", "",
                [new(Guid.NewGuid(), formed, null, LegalEntityLifecycleStatusDto.Active, LegalEntityLifecycleEventKindDto.Formation)])), 0, ct);

        await new PostgresFundProfileTenancyRegistry(database.Options).BindAsync(Fund, Tenant, Company, ct);
        // These operations establish configuration; no journal is posted through the fixture's
        // historical permissive posting-authority helper.
        var setup = new PostgresLedgerJournalStore(database.Options, new RehearsalTenant());
        var book = new LedgerBookRecord(Guid.NewGuid(), Fund, entity, FundStructureNodeKindDto.Entity,
            "Synthetic canonical rehearsal", "USD", now, now,
            AccountingPolicyId: "rehearsal-fill-policy", AccountingPolicyVersion: "v1");
        await setup.SaveLedgerBookAsync(book, ct);
        var period = await setup.SavePeriodAsync(new(Guid.NewGuid(), book.LedgerBookId, now.Year, now.Month,
            "Synthetic current month", start, start.AddMonths(1).AddDays(-1), "Open", now, null, 0), 0, ct);
        var chart = new ChartOfAccountsNodeDto[]
        {
            new("cash", "Assets:Cash", "Cash", "Asset"),
            new("securities", "Assets:Securities", "Securities", "Asset"),
            new("gains", "Revenue:Gains", "Realized Gain", "Revenue"),
            new("losses", "Expenses:Losses", "Realized Loss", "Expense"),
            new("commissions", "Expenses:Commissions", "Commissions", "Expense"),
            new("fees", "Expenses:Fees", "Fees", "Expense")
        };
        await new PostgresAccountingConfigurationStore(database.Options).SaveAsync(new AccountingConfigurationWorkspaceDto(
            Fund, book.LedgerBookId, AccountingConfigurationStatusDto.Active, "mapping-v1", now,
            [], chart, [], [], [], [], TenantId: Tenant, CompanyId: Company), ct);
        await setup.SaveTaxLotPolicyAsync(new(Guid.NewGuid(), book.LedgerBookId,
            new LedgerAccount("Securities", LedgerAccountType.Asset, Symbol, account.ToString("D")),
            LedgerTaxLotReliefMethod.Fifo, "fifo-v1", start, now, now), ct);
        var securityId = Guid.NewGuid();
        var reference = await database.SeedLotReferenceAuthorityAsync(book.LedgerBookId, securityId, Guid.NewGuid(),
            financialAccountId: account.ToString("D"));
        await reference.Securities.UpsertProjectionAsync(reference.Security with
        {
            DisplayName = "Synthetic AAPL common equity",
            PrimaryIdentifierKind = "Ticker", PrimaryIdentifierValue = Symbol,
            Identifiers = [new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, Symbol, true, formed)],
            CommonTerms = JsonSerializer.SerializeToElement(new { pricingSource = "Synthetic", accountingClassification = "Trading" }),
            AssetSpecificTerms = SecurityTermsEnvelope.StampDeclaredFormat(JsonSerializer.SerializeToElement(new { schemaVersion = 1 })),
            Provenance = JsonSerializer.SerializeToElement(new { sourceSystem = "SyntheticRehearsal", updatedBy = "synthetic-preparer", asOf = now })
        }, ct);

        var grantReference = new AutomatedPostingGrantReferenceDto(Guid.NewGuid(), 1, Tenant, account.ToString("D"),
            book.LedgerBookId, period.PeriodId, book.AccountingPolicyId, book.AccountingPolicyVersion,
            "mapping-v1", "Synthetic", Company);
        var options = new AutomatedTradeFillAccountingOptions
        {
            Grant = grantReference, CashAccountPath = chart[0].Path, SecuritiesAccountPath = chart[1].Path,
            RealizedGainAccountPath = chart[2].Path, RealizedLossAccountPath = chart[3].Path,
            CommissionAccountPath = chart[4].Path, FeeAccountPath = chart[5].Path
        };
        var context = new TradeFillLedgerPostingContext("fresh-host-canonical/v2", book.LedgerBookId,
            period.PeriodId, book.LedgerBookId, book.AccountingPolicyId, book.AccountingPolicyVersion, period.Version, 2);
        var scope = new Scope(root, database.Options, structureOptions, reference.SecurityOptions,
            reference.PositionOptions, account, securityId, period, options, context);
        var journals = CreateJournals(scope);
        await journals.AppendAsync(Opening(scope, now), ct);
        var policies = new AccountingPolicyService();
        var policy = await policies.CreatePolicyAsync(new CreateAccountingPolicyRequest(AccountingBasisKindDto.Primary,
            grantReference.PolicyId, grantReference.PolicyVersion, "Reviewed synthetic fill policy", start,
            RulePack: new AccountingPolicyRulePackDto("synthetic-rehearsal", "v1",
            [
                new("execution.trade-fill", AccountingTreatmentKindDto.General, SourceEventType: "execution.trade-fill", RequiresApproval: false, AllowsAutoPosting: true),
                new("execution.trade-commission", AccountingTreatmentKindDto.Expense, SourceEventType: "execution.trade-commission", RequiresApproval: false, AllowsAutoPosting: true),
                new("execution.trade-fee", AccountingTreatmentKindDto.Expense, SourceEventType: "execution.trade-fee", RequiresApproval: false, AllowsAutoPosting: true)
            ])), ct);
        var grant = await journals.ActivateWithPolicyAsync(new(grantReference.GrantId, grantReference.FinancialAccountId,
            grantReference.LedgerBookId, grantReference.PeriodId, grantReference.PolicyId, grantReference.PolicyVersion,
            grantReference.MappingRevision, grantReference.SourceMode, Company), policy, "synthetic-operator", Tenant, ct);
        options.Grant = grant.Reference;
        return scope;
    }

    private static PostgresLedgerJournalStore CreateJournals(Scope scope)
    {
        var tenant = new RehearsalTenant();
        var graph = new PostgresFundStructureStore(scope.Structure);
        var history = new PostgresEntityLifecycleStore(scope.Structure);
        // No fund-account node participates in the entity-owned book. A strict read-only adapter
        // supplies the empty account inventory; it cannot approve a posting or manufacture a node.
        var accounts = new Mock<IFundAccountService>(MockBehavior.Strict);
        accounts.Setup(x => x.QueryAccountsAsync(It.IsAny<AccountStructureQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<AccountSummaryDto>());
        var structure = new PostgresFundStructureService(graph, accounts.Object, new FundStructurePolicyService(),
            tenantAccessor: tenant, entityLifecycleStore: history);
        var agreements = new PostgresIntercompanyAgreementStore(scope.Structure);
        var arrangements = new PostgresIntercompanyAccountingArrangementStore(scope.Structure);
        // Ordinary cash-equity accounts have no intercompany bindings. If an arrangement ever
        // needs approval/scope resolution, fail this test instead of silently approving it.
        var arrangementScope = new Mock<IIntercompanyAccountingArrangementScopeAuthority>(MockBehavior.Strict);
        var profiles = new IntercompanyAccountingArrangementService(arrangements, arrangementScope.Object,
            new IntercompanyAccountingArrangementLegalAuthority(agreements));
        var plain = new PostgresLedgerJournalStore(scope.Ledger, tenant);
        var intercompany = new IntercompanyLedgerPostingAuthority(arrangements, profiles,
            new PostgresIntercompanyAccountBindingStore(scope.Ledger, tenant),
            new PostgresAccountingConfigurationStore(scope.Ledger), plain, new PostgresFundProfileTenancyRegistry(scope.Ledger));
        PostgresLedgerJournalStore? journals = null;
        var lifecycle = new LifecycleLedgerPostingAuthority(structure, history, () => journals,
            graph.AcquireConsolidationAuthorityLeaseAsync);
        journals = new(scope.Ledger, tenant,
            backfillSecurityMaster: () => new PostgresSecurityMasterStore(scope.Security),
            backfillPositions: () => new PostgresAssetOperationsProjectionStore(scope.Positions),
            postingAuthorities: () => [lifecycle, intercompany], controlPreflights: () => [lifecycle, intercompany],
            intercompanyAgreementAuthority: () => new PostgresIntercompanyAgreementPostingAuthority(agreements));
        return journals;
    }

    private static LedgerJournalEntryWrite Opening(Scope scope, DateTimeOffset now)
    {
        var grant = scope.Options.Grant!;
        var id = Guid.NewGuid();
        var commandId = Guid.NewGuid();
        var date = DateOnly.FromDateTime(now.UtcDateTime);
        const string description = "Reviewed synthetic opening cash";
        var entry = new JournalEntry(id, now, description,
        [
            new(Guid.NewGuid(), id, now, new LedgerAccount("Cash", LedgerAccountType.Asset, FinancialAccountId: grant.FinancialAccountId),
                100000m, 0m, description, currency: new LedgerEntryCurrency("USD", "USD", 100000m, 0m, 1m)),
            new(Guid.NewGuid(), id, now, new LedgerAccount("Capital", LedgerAccountType.Equity, FinancialAccountId: grant.FinancialAccountId),
                0m, 100000m, description, currency: new LedgerEntryCurrency("USD", "USD", 0m, 100000m, 1m))
        ], new JournalEntryMetadata(ActivityType: "opening-balance", FinancialAccountId: grant.FinancialAccountId,
            LedgerBook: grant.LedgerBookId.ToString("D"), EffectiveDate: date, IdempotencyKey: "rehearsal-opening:" + id));
        var command = new AccountingPostingCommandDto(commandId, grant.LedgerBookId, grant.PeriodId, date, now,
            entry.Metadata.IdempotencyKey!, SourceEventId: id, ExpectedVersion: scope.Period.Version,
            SourceEventType: "synthetic.reviewed-opening", ApprovalState: AccountingPostingApprovalStateDto.Approved,
            ApprovalId: "synthetic-opening-review", Evidence: [new("synthetic-opening-review", "evidence://rehearsal/opening",
                AccountingPostingEvidenceKindDto.Source, "Synthetic", now, "synthetic-preparer")], LedgerBookId: grant.LedgerBookId)
        { Actor = "synthetic-opening-poster", Provenance = DataProvenance.Simulated };
        return new(entry, grant.LedgerBookId, grant.PeriodId, commandId, AccountingPolicyId: grant.PolicyId,
            AccountingPolicyVersion: grant.PolicyVersion, SourceEventId: id, PostingCommand: command, LedgerBookId: grant.LedgerBookId);
    }

    private sealed class RehearsalTenant : IFundScopeTenantAccessor
    {
        public string? ResolveCallerTenant() => Tenant;
    }
}
