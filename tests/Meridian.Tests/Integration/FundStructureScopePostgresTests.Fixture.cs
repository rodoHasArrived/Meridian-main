using Meridian.Application.FundStructure;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Tenancy;
using Meridian.Entities.FundStructure;
using Meridian.PortfolioRecords.FundAccounts;
using Meridian.Storage.FundAccounts;
using Meridian.Storage.FundStructure;
using Meridian.TestSupport;

namespace Meridian.Tests.Integration;

public sealed partial class FundStructureScopePostgresTests
{
    private sealed class ScopeData(PostgresTestServer server) : IAsyncDisposable
    {
        private FundStructureStoreOptions StructureOptions { get; } = new()
        {
            ConnectionString = server.ConnectionString,
            Schema = server.CreateSchemaName("fs_scope")
        };

        private FundAccountStoreOptions AccountOptions { get; } = new()
        {
            ConnectionString = server.ConnectionString,
            Schema = server.CreateSchemaName("fa_scope")
        };

        public Branch A { get; } = new("A", "tenant-a");
        public Branch B { get; } = new("B", "tenant-a");
        public Branch Foreign { get; } = new("FOREIGN", "tenant-b");
        public PostgresFundStructureStore Store => new(StructureOptions);
        private PostgresFundAccountStore AccountStore(string? tenant) =>
            new(AccountOptions, new TenantAccessor(tenant), TenantScopeEnforcementOptions.FailClosed);
        public PostgresFundAccountService Accounts(string? tenant) => new(AccountStore(tenant));
        public PostgresFundStructureService Service(string? tenant) => new(
            Store, Accounts(tenant), new FundStructurePolicyService(),
            tenantAccessor: new TenantAccessor(tenant), tenantScope: TenantScopeEnforcementOptions.FailClosed);

        public static async Task<ScopeData> CreateAsync(CancellationToken ct)
        {
            var server = await PostgresTestServer.CreateAsync("MERIDIAN_FUND_ACCOUNTS_CONNECTION_STRING", ct: ct);
            var data = new ScopeData(server);
            try
            {
                await new FundStructureMigrationRunner(data.StructureOptions).EnsureMigratedAsync(ct);
                await new FundAccountMigrationRunner(data.AccountOptions).EnsureMigratedAsync(ct);
                foreach (var branch in new[] { data.A, data.B, data.Foreign })
                    await data.SeedBranchAsync(branch, ct);
                return data;
            }
            catch
            {
                await data.DisposeAsync();
                throw;
            }
        }

        private async Task SeedBranchAsync(Branch branch, CancellationToken ct)
        {
            var service = Service(branch.Tenant);
            var accounts = Accounts(branch.Tenant);
            await service.CreateOrganizationAsync(new CreateOrganizationRequest(
                branch.OrganizationId, $"ORG-{branch.Tag}", $"Organization {branch.Tag}", "USD", EffectiveFrom, "scope-test"), ct);
            await service.CreateBusinessAsync(new CreateBusinessRequest(
                branch.BusinessId, branch.OrganizationId, BusinessKindDto.FundManager,
                $"BUS-{branch.Tag}", $"Business {branch.Tag}", "USD", EffectiveFrom, "scope-test"), ct);
            await service.CreateClientAsync(new CreateClientRequest(
                branch.ClientId, branch.BusinessId, $"CLI-{branch.Tag}", $"Client {branch.Tag}", "USD", EffectiveFrom, "scope-test"), ct);
            await service.CreateFundAsync(new CreateFundRequest(
                branch.FundId, $"FND-{branch.Tag}", $"Fund {branch.Tag}", "USD", EffectiveFrom, "scope-test", BusinessId: branch.BusinessId), ct);
            await service.CreateSleeveAsync(new CreateSleeveRequest(
                branch.SleeveId, branch.FundId, $"SLV-{branch.Tag}", "Sleeve", EffectiveFrom, "scope-test"), ct);
            foreach (var entityId in new[] { branch.EntityId, branch.AccountEntityId })
                await service.CreateLegalEntityAsync(new CreateLegalEntityRequest(
                    entityId, LegalEntityTypeDto.Vehicle, $"ENT-{entityId:N}", "Entity", "US", "USD", EffectiveFrom, "scope-test"), ct);
            await service.CreateVehicleAsync(new CreateVehicleRequest(
                branch.VehicleId, branch.FundId, branch.EntityId, $"VEH-{branch.Tag}", "Vehicle", "USD", EffectiveFrom, "scope-test"), ct);
            await service.CreateInvestmentPortfolioAsync(new CreateInvestmentPortfolioRequest(
                branch.PortfolioId, branch.BusinessId, $"PF-{branch.Tag}", "Portfolio", "USD", EffectiveFrom, "scope-test", FundId: branch.FundId), ct);

            // The account-derived entity has no fund/vehicle/portfolio link. It must survive for
            // its selected branch, but must not reseed a branch whose selectors intersect to empty.
            await accounts.CreateAccountAsync(new CreateAccountRequest(
                branch.DirectAccountId, AccountTypeDto.Custody, $"DIRECT-{branch.Tag}", "Direct account", "USD",
                EffectiveFrom, "scope-test", EntityId: branch.AccountEntityId, FundId: branch.FundId,
                LedgerReference: $"BOOK-{branch.Tag}"), ct);
            await accounts.CreateAccountAsync(new CreateAccountRequest(
                branch.EntityAccountId, AccountTypeDto.Bank, $"ENTITY-{branch.Tag}", "Entity account", "USD",
                EffectiveFrom, "scope-test", EntityId: branch.EntityId, LedgerReference: $"BOOK-{branch.Tag}"), ct);
            var linked = await accounts.CreateAccountAsync(new CreateAccountRequest(
                branch.LinkedAccountId, AccountTypeDto.Custody, $"LINKED-{branch.Tag}", "Linked account", "USD",
                EffectiveFrom, "scope-test", PortfolioId: branch.PortfolioId.ToString("D"), LedgerReference: "FALLBACK"), ct);
            await service.LinkNodesAsync(new LinkFundStructureNodesRequest(
                branch.AccountLinkId, branch.PortfolioId, branch.LinkedAccountId,
                OwnershipRelationshipTypeDto.Operates, EffectiveFrom, "scope-test"), ct);
            await service.AssignNodeAsync(new AssignFundStructureNodeRequest(
                branch.AssignmentId, branch.LinkedAccountId, "LedgerGroup", $"ASSIGNED-{branch.Tag}",
                EffectiveFrom, "scope-test", IsPrimary: true), ct);
            // Retained accounts may rely on their separately stamped ownership link. Remove the
            // redundant pointer so the read must use that persisted link, not a DTO shortcut.
            await AccountStore(branch.Tenant).UpsertAccountAsync(linked with { PortfolioId = null }, ct);
        }

        public async Task<Views> QueryAsync(CancellationToken ct, string? tenant = "tenant-a",
            Guid? organizationId = null, Guid? businessId = null, bool activeOnly = true, DateTimeOffset? asOf = null)
        {
            var service = Service(tenant);
            return new Views(
                await service.GetOrganizationStructureAsync(new OrganizationStructureQuery(
                    OrganizationId: organizationId, BusinessId: businessId, ActiveOnly: activeOnly, AsOf: asOf ?? AsOf), ct),
                await service.GetAccountingViewAsync(new AccountingStructureQuery(
                    OrganizationId: organizationId, BusinessId: businessId, ActiveOnly: activeOnly, AsOf: asOf ?? AsOf), ct));
        }

        public ValueTask DisposeAsync() => server.DisposeAsync();
    }

    private sealed class TenantAccessor(string? tenant) : IFundScopeTenantAccessor
    {
        public string? ResolveCallerTenant() => tenant;
    }

    private sealed class Branch(string tag, string tenant)
    {
        public string Tag { get; } = tag;
        public string Tenant { get; } = tenant;
        public Guid OrganizationId { get; } = Guid.NewGuid();
        public Guid BusinessId { get; } = Guid.NewGuid();
        public Guid ClientId { get; } = Guid.NewGuid();
        public Guid FundId { get; } = Guid.NewGuid();
        public Guid SleeveId { get; } = Guid.NewGuid();
        public Guid VehicleId { get; } = Guid.NewGuid();
        public Guid EntityId { get; } = Guid.NewGuid();
        public Guid AccountEntityId { get; } = Guid.NewGuid();
        public Guid PortfolioId { get; } = Guid.NewGuid();
        public Guid DirectAccountId { get; } = Guid.NewGuid();
        public Guid EntityAccountId { get; } = Guid.NewGuid();
        public Guid LinkedAccountId { get; } = Guid.NewGuid();
        public Guid AccountLinkId { get; } = Guid.NewGuid();
        public Guid AssignmentId { get; } = Guid.NewGuid();
        public IEnumerable<Guid> AccountIds => [DirectAccountId, EntityAccountId, LinkedAccountId];
        public IEnumerable<Guid> DescendantNodeIds =>
            [BusinessId, ClientId, FundId, SleeveId, VehicleId, EntityId, AccountEntityId, PortfolioId, .. AccountIds];
    }
}
