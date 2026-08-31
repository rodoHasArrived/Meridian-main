using System.Text.Json;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Integrity;
using Meridian.Storage;
using Meridian.Storage.FundStructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.Composition;

internal static class FundStructureStartup
{
    private const int MaxLegacySnapshotBytes = 64 * 1024 * 1024;

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

    public static async Task EnsureDatabaseReadyAsync(
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default,
        ILogger? logger = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureEnvironmentDefaults();
        if (!IsConfigured())
        {
            logger?.LogDebug(
                "Skipping Fund Structure database readiness because {ConnectionStringVariable} is not configured.",
                ConnectionStringVariable);
            return;
        }

        var options = serviceProvider.GetRequiredService<FundStructureStoreOptions>();
        var readiness = serviceProvider.GetRequiredService<DatabaseMigrationReadinessReceipt>();
        var runner = new FundStructureMigrationRunner(options);
        await runner.EnsureMigratedAsync(cancellationToken).ConfigureAwait(false);
        readiness.MarkFundStructureReady();
        logger?.LogInformation(
            "Fund structure schema '{Schema}' is ready.",
            options.Schema);

        // On first startup, import any existing JSON snapshot so local data carries over.
        var store = serviceProvider.GetService<IFundStructureStore>();
        var storageRoot = serviceProvider.GetService<StorageOptions>();
        if (store is null || storageRoot is null)
            return;

        var sourcePath = Path.Combine(storageRoot.RootPath, "governance", "fund-structure.json");
        var snapshotPath = LegacySnapshotArchiver.ResolveReadableSnapshotPath(sourcePath);
        if (snapshotPath is null)
            return;

        var request = await ReadSnapshotAsync(snapshotPath, logger, cancellationToken).ConfigureAwait(false);
        var result = await store.ImportLegacySnapshotIfEmptyAsync(request, cancellationToken).ConfigureAwait(false);
        if (result == FundStructureLegacyImportResult.StoreNotEmpty)
        {
            logger?.LogInformation(
                "Fund structure store is not empty and has no matching import receipt; leaving legacy snapshot at {Path}.",
                snapshotPath);
            return;
        }

        var archiveResult = await LegacySnapshotArchiver.ArchiveCommittedSnapshotAsync(
            sourcePath,
            request.SourceHash,
            MaxLegacySnapshotBytes,
            cancellationToken).ConfigureAwait(false);
        logger?.LogInformation(
            "Fund structure legacy snapshot state is {ImportResult}; archive state is {ArchiveResult} at {ImportedPath}.",
            result,
            archiveResult,
            LegacySnapshotArchiver.GetImportedPath(sourcePath));
    }

    // Mirrors the internal PersistedState record in InMemoryFundStructureService.
    private sealed record PersistedState(
        int Version,
        List<OrganizationSummaryDto?>? Organizations,
        List<BusinessSummaryDto?>? Businesses,
        List<ClientSummaryDto?>? Clients,
        List<FundSummaryDto?>? Funds,
        List<SleeveSummaryDto?>? Sleeves,
        List<VehicleSummaryDto?>? Vehicles,
        List<LegalEntitySummaryDto?>? Entities,
        List<InvestmentPortfolioSummaryDto?>? InvestmentPortfolios,
        List<OwnershipLinkDto?>? OwnershipLinks,
        List<FundStructureAssignmentDto?>? Assignments,
        List<Guid>? LinkedAccountIds);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal static async Task<FundStructureLegacyImportRequest> ReadSnapshotAsync(
        string snapshotPath,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        var snapshotBytes = await ReadBoundedSnapshotAsync(snapshotPath, cancellationToken).ConfigureAwait(false);
        var sourceHash = Sha256Digest.Compute(snapshotBytes);
        await using var snapshotStream = new MemoryStream(snapshotBytes, writable: false);
        var state = await JsonSerializer
            .DeserializeAsync<PersistedState>(snapshotStream, JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        if (state is null)
            throw InvalidSnapshot(snapshotPath, "the JSON document did not contain a snapshot");
        if (state.Version != 1)
            throw InvalidSnapshot(snapshotPath, $"unsupported snapshot version {state.Version}");

        var organizations = RequireItems(state.Organizations, snapshotPath, "organizations");
        var businesses = RequireItems(state.Businesses, snapshotPath, "businesses");
        var clients = RequireItems(state.Clients, snapshotPath, "clients");
        var funds = RequireItems(state.Funds, snapshotPath, "funds");
        var sleeves = RequireItems(state.Sleeves, snapshotPath, "sleeves");
        var vehicles = RequireItems(state.Vehicles, snapshotPath, "vehicles");
        var entities = RequireItems(state.Entities, snapshotPath, "entities");
        var portfolios = RequireItems(state.InvestmentPortfolios, snapshotPath, "investmentPortfolios");
        var ownershipLinks = RequireItems(state.OwnershipLinks, snapshotPath, "ownershipLinks");
        var assignments = RequireItems(state.Assignments, snapshotPath, "assignments");
        var linkedAccountIds = state.LinkedAccountIds
            ?.Distinct()
            .ToArray()
            ?? throw InvalidSnapshot(snapshotPath, "required field 'linkedAccountIds' is missing");
        if (linkedAccountIds.Contains(Guid.Empty))
            throw InvalidSnapshot(snapshotPath, "linkedAccountIds contains an empty account identifier");

        logger?.LogInformation(
            "Prepared fund structure import: orgs={Orgs}, funds={Funds}, links={Links}.",
            organizations.Count,
            funds.Count,
            ownershipLinks.Count);

        return new FundStructureLegacyImportRequest(
            sourceHash,
            organizations,
            businesses,
            clients,
            funds,
            sleeves,
            vehicles,
            entities,
            portfolios,
            ownershipLinks,
            assignments,
            linkedAccountIds);
    }

    private static List<T> RequireItems<T>(
        List<T?>? items,
        string snapshotPath,
        string fieldName)
        where T : class
    {
        if (items is null)
            throw InvalidSnapshot(snapshotPath, $"required field '{fieldName}' is missing");

        var result = new List<T>(items.Count);
        foreach (var item in items)
        {
            result.Add(item ?? throw InvalidSnapshot(
                snapshotPath,
                $"required field '{fieldName}' contains a null item"));
        }

        return result;
    }

    private static InvalidDataException InvalidSnapshot(string snapshotPath, string reason)
        => new($"Fund structure legacy snapshot '{snapshotPath}' is invalid: {reason}.");

    private static async Task<byte[]> ReadBoundedSnapshotAsync(
        string snapshotPath,
        CancellationToken cancellationToken)
    {
        var length = new FileInfo(snapshotPath).Length;
        if (length > MaxLegacySnapshotBytes)
        {
            throw new InvalidDataException(
                $"Fund structure legacy snapshot '{snapshotPath}' is {length} bytes; maximum supported size is {MaxLegacySnapshotBytes} bytes.");
        }

        var bytes = await File.ReadAllBytesAsync(snapshotPath, cancellationToken).ConfigureAwait(false);
        if (bytes.Length > MaxLegacySnapshotBytes)
        {
            throw new InvalidDataException(
                $"Fund structure legacy snapshot '{snapshotPath}' exceeded the {MaxLegacySnapshotBytes}-byte limit while being read.");
        }

        return bytes;
    }
}
