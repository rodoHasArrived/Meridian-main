using Meridian.Contracts.Ledger;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Tenancy;
using Meridian.FinancialOperations.Intercompany;
using Meridian.Storage.FundStructure;
using Meridian.Storage.Ledger;
using Moq;

namespace Meridian.Tests.Ui;

public sealed partial class AccountingSystemIntegrationServiceTests
{
    /// <summary>Uses the same immutable ledger rows as reconciliation; zero IC exposure is established from its actual chart.</summary>
    private sealed class ActualLedgerFixtureSchedule : IIntercompanyBalanceScheduleSource
    {
        private readonly IntercompanyBalanceScheduleService _source;
        public ActualLedgerFixtureSchedule(ILedgerJournalStore ledger)
        {
            var bindings = new Mock<IIntercompanyAccountBindingStore>();
            bindings.Setup(x => x.AcquireAuthorityLeaseAsync(It.IsAny<CancellationToken>())).ReturnsAsync(EmptyLedgerAuthorityLease.Instance);
            bindings.Setup(x => x.HistoryAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
            var agreements = new Mock<IIntercompanyAccountingArrangementStore>();
            agreements.Setup(x => x.AcquireAuthorityLeaseAsync(It.IsAny<CancellationToken>())).ReturnsAsync(EmptyLedgerAuthorityLease.Instance);
            var configuration = new Mock<IAccountingConfigurationStore>();
            configuration.Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>(), It.IsAny<string?>(), It.IsAny<string?>()))
                .Returns(async (string fund, Guid? book, CancellationToken ct, string? tenant, string? company) =>
                {
                    var records = await ledger.QueryAsync(new(LedgerBookId: book), ct);
                    var chart = records.SelectMany(x => x.Entry.Lines).Select(x => x.Account).Distinct()
                        .Select(account => new ChartOfAccountsNodeDto(account.Name, account.Name, account.Name, account.AccountType.ToString())).ToArray();
                    return new AccountingConfigurationWorkspaceDto(fund, book, AccountingConfigurationStatusDto.Active, "synthetic-chart-v1",
                        DateTimeOffset.UnixEpoch, [], chart, [], [], [], [], TenantId: tenant, CompanyId: company);
                });
            var owner = new Mock<IFundProfileTenancyRegistry>();
            owner.Setup(x => x.ResolveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string fund, CancellationToken _) => Task.FromResult<FundProfileOwnership?>(new(fund, "fixture-tenant", "fixture-company")));
            var service = new IntercompanyAccountingArrangementService(agreements.Object, Mock.Of<IIntercompanyAccountingArrangementScopeAuthority>(), Mock.Of<IIntercompanyAccountingArrangementLegalAuthority>());
            _source = new(ledger, service, bindings.Object, configuration.Object, owner.Object, agreements.Object);
        }
        public Task<IntercompanyBalanceScheduleDto> GetAsync(string? tenantId, string? companyId, string? fundProfileId,
            Guid? ledgerBookId, DateOnly asOf, CancellationToken ct = default)
            => _source.GetAsync(tenantId ?? "fixture-tenant", companyId ?? "fixture-company", fundProfileId, ledgerBookId, asOf, ct);
        public Task<IntercompanyBalanceScheduleDto> GetForCloseAsync(string? tenant, string? company, string? fund,
            Guid? book, string? periodId, CancellationToken ct = default)
            => _source.GetForCloseAsync(tenant ?? "fixture-tenant", company ?? "fixture-company", fund, book, periodId, ct);
    }
}
