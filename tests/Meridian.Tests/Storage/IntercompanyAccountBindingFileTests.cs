using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.Storage.Ledger;
using Moq;

namespace Meridian.Tests.Storage;

public sealed class IntercompanyAccountBindingFileTests
{
    [Fact]
    public async Task LocalBindingSurvivesRestartAndExactRetryDoesNotDuplicateHistory()
    {
        using var fixture = new LocalFixture();
        var binding = fixture.Binding;
        await fixture.Store.AppendAsync(binding, null);
        var restarted = fixture.Restart();
        Assert.Equal(binding.RevisionId, (await restarted.AppendAsync(binding, null)).RevisionId);
        Assert.Single(await restarted.HistoryAsync(binding.LedgerBookId));
        Assert.Equal(binding.RevisionId, Assert.Single(await restarted.ListAsync(binding.LedgerBookId, binding.EffectiveFrom)).RevisionId);
        var next = IntercompanyAccountBindingRulesTests.Successor(binding);
        await restarted.AppendAsync(next, binding.RevisionId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => restarted.AppendAsync(
            IntercompanyAccountBindingRulesTests.Successor(binding), binding.RevisionId));
        Assert.Equal(2, (await fixture.Restart().HistoryAsync(binding.LedgerBookId)).Count);
    }

    [Fact]
    public async Task ForeignTenantCannotReadOwnedBookBindingsOrBorrowRevisionIdentity()
    {
        using var fixture = new LocalFixture();
        await fixture.Store.AppendAsync(fixture.Binding, null);
        fixture.CallerTenant = "foreign-tenant";
        await Assert.ThrowsAsync<TenantScopeRejectedException>(() => fixture.Store.HistoryAsync(fixture.Binding.LedgerBookId));
        Assert.Null(await fixture.Store.GetRevisionAsync(fixture.Binding.RevisionId));
    }

    [Fact]
    public async Task LocalMappingCannotAssertDifferentRoleOrLedgerAccountFromScopedChart()
    {
        using var fixture = new LocalFixture();
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Store.AppendAsync(
            fixture.Binding with { AccountName = "Unconfigured account" }, null));
        Assert.Empty(await fixture.Store.HistoryAsync(fixture.Binding.LedgerBookId));
    }

    private sealed class LocalFixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "meridian-binding-" + Guid.NewGuid().ToString("N"));
        private readonly Mock<IFundScopeTenantAccessor> _tenants = new();
        private readonly Mock<IAccountingConfigurationStore> _configuration = new(MockBehavior.Strict);
        private readonly Mock<ILedgerJournalStore> _journals = new(MockBehavior.Strict);
        private readonly Mock<IFundProfileTenancyRegistry> _ownership = new(MockBehavior.Strict);
        public IntercompanyAccountBindingDto Binding { get; } = IntercompanyAccountBindingRulesTests.Binding();
        public string CallerTenant { get; set; }
        public FileIntercompanyAccountBindingStore Store { get; }
        public LocalFixture()
        {
            CallerTenant = Binding.TenantId;
            _tenants.Setup(x => x.ResolveCallerTenant()).Returns(() => CallerTenant);
            _journals.Setup(x => x.GetLedgerBookAsync(Binding.LedgerBookId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new LedgerBookRecord(Binding.LedgerBookId, "local-fund", Guid.NewGuid(),
                    FundStructureNodeKindDto.Entity, "Local book", "USD", Binding.RecordedAt, Binding.RecordedAt));
            _ownership.Setup(x => x.ResolveAsync("local-fund", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FundProfileOwnership("local-fund", Binding.TenantId, Binding.CompanyId));
            var chart = new ChartOfAccountsNodeDto(Binding.ChartAccountNodeId, Binding.AccountPath,
                Binding.AccountName, Binding.AccountType)
            { IntercompanyRole = Binding.Role };
            _configuration.Setup(x => x.GetAsync("local-fund", Binding.LedgerBookId, It.IsAny<CancellationToken>(),
                Binding.TenantId, Binding.CompanyId)).ReturnsAsync(new AccountingConfigurationWorkspaceDto(
                    "local-fund", Binding.LedgerBookId, AccountingConfigurationStatusDto.Draft, "v1", Binding.RecordedAt,
                    [], [chart], [], [], [], [], TenantId: Binding.TenantId, CompanyId: Binding.CompanyId));
            Store = Restart();
        }
        public FileIntercompanyAccountBindingStore Restart() => new(Path.Combine(_directory, "bindings.json"),
            _tenants.Object, _configuration.Object, _journals.Object, _ownership.Object);
        public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
    }
}
