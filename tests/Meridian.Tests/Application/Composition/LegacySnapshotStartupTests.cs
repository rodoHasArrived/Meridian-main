using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Composition;
using Meridian.Contracts.FundStructure;

namespace Meridian.Tests.Application.Composition;

public sealed class LegacySnapshotStartupTests
{
    [Fact]
    public async Task FundAccounts_ReadSnapshotAsync_MalformedJson_FailsClosed()
    {
        var root = CreateTempRoot();
        var path = Path.Combine(root, "fund-accounts.json");
        await File.WriteAllTextAsync(path, "{ this is not json");

        try
        {
            var act = () => FundAccountsStartup.ReadSnapshotAsync(path, logger: null, CancellationToken.None);

            await act.Should().ThrowAsync<JsonException>(
                "malformed legacy data must not be converted into an empty import and archived");
        }
        finally
        {
            CleanupTempRoot(root);
        }
    }

    [Fact]
    public async Task FundAccounts_ReadSnapshotAsync_PreCanceled_DoesNotDecodeSnapshot()
    {
        var root = CreateTempRoot();
        var path = Path.Combine(root, "fund-accounts.json");
        await File.WriteAllTextAsync(path, """{"version":1,"accounts":[]}""");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        try
        {
            var act = () => FundAccountsStartup.ReadSnapshotAsync(
                path,
                logger: null,
                cancellation.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();
        }
        finally
        {
            CleanupTempRoot(root);
        }
    }

    [Fact]
    public async Task FundAccounts_ReadSnapshotAsync_PreservesCustodianAndBankStatementsFromHashedBytes()
    {
        var root = CreateTempRoot();
        var path = Path.Combine(root, "fund-accounts.json");
        var account = MakeAccount();
        var asOfDate = new DateOnly(2026, 7, 24);
        var custodianBatch = new CustodianStatementBatchDto(
            Guid.NewGuid(),
            account.AccountId,
            asOfDate,
            "Custodian",
            "csv",
            1,
            DateTimeOffset.UtcNow,
            "legacy");
        var custodianLine = new CustodianPositionLineDto(
            Guid.NewGuid(),
            custodianBatch.BatchId,
            account.AccountId,
            asOfDate,
            "US0000000001",
            "ISIN",
            10m,
            100m,
            "USD",
            "Legacy security",
            "Equity",
            false);
        var bankBatch = new BankStatementBatchDto(
            Guid.NewGuid(),
            account.AccountId,
            asOfDate,
            "Bank",
            1,
            DateTimeOffset.UtcNow,
            "legacy");
        var bankLine = new BankStatementLineDto(
            Guid.NewGuid(),
            bankBatch.BatchId,
            account.AccountId,
            asOfDate,
            asOfDate,
            25m,
            "USD",
            "deposit",
            "Legacy cash",
            "ref-1",
            125m);
        var snapshot = new
        {
            Version = 1,
            Accounts = new[]
            {
                new
                {
                    Summary = account,
                    Snapshots = Array.Empty<AccountBalanceSnapshotDto>(),
                    CustodianBatches = new[] { custodianBatch },
                    CustodianPositions = new[] { custodianLine },
                    BankBatches = new[] { bankBatch },
                    BankLines = new[] { bankLine },
                    ReconciliationRuns = Array.Empty<AccountReconciliationRunDto>(),
                    ReconciliationResults = Array.Empty<AccountReconciliationResultDto>(),
                    SyncHistory = Array.Empty<AccountSyncHistoryEntryDto>(),
                    MarginSnapshots = Array.Empty<MarginSnapshotDto>()
                }
            }
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            snapshot,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await File.WriteAllBytesAsync(path, bytes);

        try
        {
            var request = await FundAccountsStartup.ReadSnapshotAsync(
                path,
                logger: null,
                CancellationToken.None);

            request.SourceHash.Should().Be(Hash(bytes));
            var importedAccount = request.Accounts.Should().ContainSingle().Which;
            importedAccount.CustodianStatements.Should().ContainSingle()
                .Which.Lines.Should().ContainSingle().Which.Should().Be(custodianLine);
            importedAccount.BankStatements.Should().ContainSingle()
                .Which.Lines.Should().ContainSingle().Which.Should().Be(bankLine);
        }
        finally
        {
            CleanupTempRoot(root);
        }
    }

    [Fact]
    public async Task FundStructure_ReadSnapshotAsync_UnsupportedVersion_FailsClosed()
    {
        var root = CreateTempRoot();
        var path = Path.Combine(root, "fund-structure.json");
        await File.WriteAllTextAsync(path, """{"version":2}""");

        try
        {
            var act = () => FundStructureStartup.ReadSnapshotAsync(path, logger: null, CancellationToken.None);

            await act.Should().ThrowAsync<InvalidDataException>()
                .WithMessage("*unsupported snapshot version 2*");
        }
        finally
        {
            CleanupTempRoot(root);
        }
    }

    [Fact]
    public async Task FundStructure_ReadSnapshotAsync_MissingRequiredCollections_FailsClosed()
    {
        var root = CreateTempRoot();
        var path = Path.Combine(root, "fund-structure.json");
        await File.WriteAllTextAsync(path, """{"version":1}""");

        try
        {
            var act = () => FundStructureStartup.ReadSnapshotAsync(
                path,
                logger: null,
                CancellationToken.None);

            await act.Should().ThrowAsync<InvalidDataException>()
                .WithMessage("*required field 'organizations' is missing*");
        }
        finally
        {
            CleanupTempRoot(root);
        }
    }

    [Fact]
    public async Task FundStructure_ReadSnapshotAsync_PreCanceled_DoesNotDecodeSnapshot()
    {
        var root = CreateTempRoot();
        var path = Path.Combine(root, "fund-structure.json");
        await File.WriteAllTextAsync(path, """{"version":1}""");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        try
        {
            var act = () => FundStructureStartup.ReadSnapshotAsync(
                path,
                logger: null,
                cancellation.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();
        }
        finally
        {
            CleanupTempRoot(root);
        }
    }

    [Fact]
    public async Task FundStructure_ReadSnapshotAsync_EmptyLinkedAccountId_FailsClosed()
    {
        var root = CreateTempRoot();
        var path = Path.Combine(root, "fund-structure.json");
        await File.WriteAllTextAsync(
            path,
            """
            {
              "version": 1,
              "organizations": [],
              "businesses": [],
              "clients": [],
              "funds": [],
              "sleeves": [],
              "vehicles": [],
              "entities": [],
              "investmentPortfolios": [],
              "ownershipLinks": [],
              "assignments": [],
              "linkedAccountIds": ["00000000-0000-0000-0000-000000000000"]
            }
            """);

        try
        {
            var act = () => FundStructureStartup.ReadSnapshotAsync(path, logger: null, CancellationToken.None);

            await act.Should().ThrowAsync<InvalidDataException>()
                .WithMessage("*empty account identifier*");
        }
        finally
        {
            CleanupTempRoot(root);
        }
    }

    [Fact]
    public async Task FundStructure_ReadSnapshotAsync_PreservesDisconnectedLinkedAccountIdentity()
    {
        var root = CreateTempRoot();
        var path = Path.Combine(root, "fund-structure.json");
        var linkedAccountId = Guid.NewGuid();
        var snapshot = new
        {
            Version = 1,
            Organizations = Array.Empty<OrganizationSummaryDto>(),
            Businesses = Array.Empty<BusinessSummaryDto>(),
            Clients = Array.Empty<ClientSummaryDto>(),
            Funds = Array.Empty<FundSummaryDto>(),
            Sleeves = Array.Empty<SleeveSummaryDto>(),
            Vehicles = Array.Empty<VehicleSummaryDto>(),
            Entities = Array.Empty<LegalEntitySummaryDto>(),
            InvestmentPortfolios = Array.Empty<InvestmentPortfolioSummaryDto>(),
            OwnershipLinks = Array.Empty<OwnershipLinkDto>(),
            Assignments = Array.Empty<FundStructureAssignmentDto>(),
            LinkedAccountIds = new[] { linkedAccountId }
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            snapshot,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await File.WriteAllBytesAsync(path, bytes);

        try
        {
            var request = await FundStructureStartup.ReadSnapshotAsync(
                path,
                logger: null,
                CancellationToken.None);

            request.SourceHash.Should().Be(Hash(bytes));
            request.LinkedAccountIds.Should().ContainSingle().Which.Should().Be(linkedAccountId);
        }
        finally
        {
            CleanupTempRoot(root);
        }
    }

    [Fact]
    public async Task ArchiveCommittedSnapshotAsync_ReplacementDoesNotMatchCommittedHash_RestoresSource()
    {
        var root = CreateTempRoot();
        var sourcePath = Path.Combine(root, "legacy.json");
        var committedBytes = """{"version":1,"value":"committed"}"""u8.ToArray();
        var replacementBytes = """{"version":1,"value":"replacement"}"""u8.ToArray();
        await File.WriteAllBytesAsync(sourcePath, replacementBytes);

        try
        {
            var act = () => LegacySnapshotArchiver.ArchiveCommittedSnapshotAsync(
                sourcePath,
                Hash(committedBytes),
                maximumBytes: 1024,
                CancellationToken.None);

            await act.Should().ThrowAsync<IOException>()
                .WithMessage("*changed after it was hashed*");
            (await File.ReadAllBytesAsync(sourcePath)).Should().Equal(replacementBytes);
            File.Exists(LegacySnapshotArchiver.GetPendingPath(sourcePath)).Should().BeFalse();
            File.Exists(LegacySnapshotArchiver.GetImportedPath(sourcePath)).Should().BeFalse();
        }
        finally
        {
            CleanupTempRoot(root);
        }
    }

    [Fact]
    public async Task ArchiveCommittedSnapshotAsync_PreCanceled_LeavesSourceUnclaimed()
    {
        var root = CreateTempRoot();
        var sourcePath = Path.Combine(root, "legacy.json");
        var bytes = """{"version":1}"""u8.ToArray();
        await File.WriteAllBytesAsync(sourcePath, bytes);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        try
        {
            var act = () => LegacySnapshotArchiver.ArchiveCommittedSnapshotAsync(
                sourcePath,
                Hash(bytes),
                maximumBytes: 1024,
                cancellation.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();
            (await File.ReadAllBytesAsync(sourcePath)).Should().Equal(bytes);
            File.Exists(LegacySnapshotArchiver.GetPendingPath(sourcePath)).Should().BeFalse();
            File.Exists(LegacySnapshotArchiver.GetImportedPath(sourcePath)).Should().BeFalse();
        }
        finally
        {
            CleanupTempRoot(root);
        }
    }

    [Fact]
    public async Task ArchiveCommittedSnapshotAsync_PendingClaimFromPriorProcess_CompletesArchive()
    {
        var root = CreateTempRoot();
        var sourcePath = Path.Combine(root, "legacy.json");
        var pendingPath = LegacySnapshotArchiver.GetPendingPath(sourcePath);
        var bytes = """{"version":1}"""u8.ToArray();
        await File.WriteAllBytesAsync(pendingPath, bytes);

        try
        {
            LegacySnapshotArchiver.ResolveReadableSnapshotPath(sourcePath).Should().Be(pendingPath);

            var result = await LegacySnapshotArchiver.ArchiveCommittedSnapshotAsync(
                sourcePath,
                Hash(bytes),
                maximumBytes: 1024,
                CancellationToken.None);

            result.Should().Be(LegacySnapshotArchiveResult.Archived);
            File.Exists(pendingPath).Should().BeFalse();
            (await File.ReadAllBytesAsync(LegacySnapshotArchiver.GetImportedPath(sourcePath)))
                .Should().Equal(bytes);
        }
        finally
        {
            CleanupTempRoot(root);
        }
    }

    [Fact]
    public async Task ArchiveCommittedSnapshotAsync_AlreadyArchivedExactBytes_IsIdempotent()
    {
        var root = CreateTempRoot();
        var sourcePath = Path.Combine(root, "legacy.json");
        var bytes = """{"version":1}"""u8.ToArray();
        await File.WriteAllBytesAsync(LegacySnapshotArchiver.GetImportedPath(sourcePath), bytes);

        try
        {
            var result = await LegacySnapshotArchiver.ArchiveCommittedSnapshotAsync(
                sourcePath,
                Hash(bytes),
                maximumBytes: 1024,
                CancellationToken.None);

            result.Should().Be(LegacySnapshotArchiveResult.AlreadyArchived);
        }
        finally
        {
            CleanupTempRoot(root);
        }
    }

    private static AccountSummaryDto MakeAccount() => new(
        AccountId: Guid.NewGuid(),
        AccountType: AccountTypeDto.Custody,
        EntityId: null,
        FundId: Guid.NewGuid(),
        SleeveId: null,
        VehicleId: null,
        AccountCode: "LEGACY-1",
        DisplayName: "Legacy account",
        BaseCurrency: "USD",
        Institution: "Custodian",
        IsActive: true,
        EffectiveFrom: DateTimeOffset.UtcNow,
        EffectiveTo: null,
        PortfolioId: null,
        LedgerReference: null,
        StrategyId: null,
        RunId: null);

    private static string Hash(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string CreateTempRoot()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "meridian-tests",
            "legacy-snapshot-startup",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void CleanupTempRoot(string root)
    {
        if (!Directory.Exists(root))
            return;

        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch
        {
            // Best-effort test cleanup.
        }
    }
}
