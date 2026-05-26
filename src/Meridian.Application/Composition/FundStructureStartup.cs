using System.Text.Json;
using Meridian.Contracts.FundStructure;
using Meridian.Storage;
using Meridian.Storage.FundStructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.Composition;

internal static class FundStructureStartup
{
    internal const string ConnectionStringVariable = "MERIDIAN_FUND_STRUCTURE_CONNECTION_STRING";
    internal const string SchemaVariable = "MERIDIAN_FUND_STRUCTURE_SCHEMA";
    internal const string DefaultSchema = "fund_structure";

    public static bool IsConfigured()
        => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionStringVariable));

    public static void EnsureEnvironmentDefaults()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SchemaVariable)))
        {
            Environment.SetEnvironmentVariable(SchemaVariable, DefaultSchema);
        }
    }

    public static void EnsureDatabaseReady(IServiceProvider serviceProvider, ILogger? logger = null)
    {
        EnsureEnvironmentDefaults();
        if (!IsConfigured())
        {
            logger?.LogDebug(
                "Skipping Fund Structure database readiness because {ConnectionStringVariable} is not configured.",
                ConnectionStringVariable);
            return;
        }

        var options = serviceProvider.GetRequiredService<FundStructureStoreOptions>();
        var runner = new FundStructureMigrationRunner(options);
        runner.EnsureMigratedAsync(CancellationToken.None).GetAwaiter().GetResult();
        logger?.LogInformation(
            "Fund structure schema '{Schema}' is ready.",
            options.Schema);

        // On first startup, import any existing JSON snapshot so local data carries over.
        var store = serviceProvider.GetService<IFundStructureStore>();
        var storageRoot = serviceProvider.GetService<StorageOptions>();
        if (store is null || storageRoot is null)
            return;

        var snapshotPath = Path.Combine(storageRoot.RootPath, "governance", "fund-structure.json");
        if (!File.Exists(snapshotPath))
            return;

        var isEmpty = Task.Run(() => store.IsEmptyAsync(CancellationToken.None)).GetAwaiter().GetResult();
        if (!isEmpty)
            return;

        logger?.LogInformation("Fund structure store is empty — importing existing JSON snapshot from {Path}.", snapshotPath);
        ImportSnapshotAsync(snapshotPath, store, logger).GetAwaiter().GetResult();
        var importedPath = snapshotPath + ".imported";
        File.Move(snapshotPath, importedPath, overwrite: true);
        logger?.LogInformation("Snapshot import complete. Original file renamed to {ImportedPath}.", importedPath);
    }

    // Mirrors the internal PersistedState record in InMemoryFundStructureService.
    private sealed record PersistedState(
        int Version,
        List<OrganizationSummaryDto>? Organizations,
        List<BusinessSummaryDto>? Businesses,
        List<ClientSummaryDto>? Clients,
        List<FundSummaryDto>? Funds,
        List<SleeveSummaryDto>? Sleeves,
        List<VehicleSummaryDto>? Vehicles,
        List<LegalEntitySummaryDto>? Entities,
        List<InvestmentPortfolioSummaryDto>? InvestmentPortfolios,
        List<OwnershipLinkDto>? OwnershipLinks,
        List<FundStructureAssignmentDto>? Assignments,
        List<Guid>? LinkedAccountIds);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static async Task ImportSnapshotAsync(string snapshotPath, IFundStructureStore store, ILogger? logger)
    {
        var json = await File.ReadAllTextAsync(snapshotPath).ConfigureAwait(false);
        var state = JsonSerializer.Deserialize<PersistedState>(json, JsonOptions);
        if (state is null)
        {
            logger?.LogWarning("Fund structure snapshot at {Path} could not be deserialized; skipping import.", snapshotPath);
            return;
        }

        // Import in parent-before-child order to respect any FK-like constraints.
        foreach (var org in state.Organizations ?? [])
            await store.UpsertOrganizationAsync(org).ConfigureAwait(false);
        foreach (var biz in state.Businesses ?? [])
            await store.UpsertBusinessAsync(biz).ConfigureAwait(false);
        foreach (var client in state.Clients ?? [])
            await store.UpsertClientAsync(client).ConfigureAwait(false);
        foreach (var fund in state.Funds ?? [])
            await store.UpsertFundAsync(fund).ConfigureAwait(false);
        foreach (var sleeve in state.Sleeves ?? [])
            await store.UpsertSleeveAsync(sleeve).ConfigureAwait(false);
        foreach (var vehicle in state.Vehicles ?? [])
            await store.UpsertVehicleAsync(vehicle).ConfigureAwait(false);
        foreach (var entity in state.Entities ?? [])
            await store.UpsertLegalEntityAsync(entity).ConfigureAwait(false);
        foreach (var portfolio in state.InvestmentPortfolios ?? [])
            await store.UpsertInvestmentPortfolioAsync(portfolio).ConfigureAwait(false);
        foreach (var link in state.OwnershipLinks ?? [])
            await store.UpsertOwnershipLinkAsync(link).ConfigureAwait(false);
        foreach (var assignment in state.Assignments ?? [])
            await store.UpsertAssignmentAsync(assignment).ConfigureAwait(false);

        logger?.LogInformation(
            "Fund structure import: orgs={Orgs}, funds={Funds}, links={Links}.",
            state.Organizations?.Count ?? 0,
            state.Funds?.Count ?? 0,
            state.OwnershipLinks?.Count ?? 0);
    }
}
