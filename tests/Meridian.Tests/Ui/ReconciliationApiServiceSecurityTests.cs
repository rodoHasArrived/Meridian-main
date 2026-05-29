using System.Security.Cryptography;
using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Domain.Reconciliation;
using Meridian.Infrastructure.Reconciliation;
using Meridian.Ui.Services.Services.Reconciliation;

namespace Meridian.Tests.Ui;

public sealed class ReconciliationApiServiceSecurityTests
{
    [Fact]
    public async Task CreateStatementRunAsync_SourcePathOutsideImportRoot_ShouldRejectWithoutUsingCallerHash()
    {
        var importRoot = CreateTempDirectory("statement-import-root");
        var outsideRoot = CreateTempDirectory("statement-outside-root");
        var outsideFile = Path.Combine(outsideRoot, "statement.csv");
        await File.WriteAllTextAsync(outsideFile, "outside statement content");
        var service = CreateService(importRoot);

        var act = async () => await service.CreateStatementRunAsync(CreateRequest(outsideFile, sourceFileHash: "CALLERHASH"));

        await act.Should()
            .ThrowAsync<ArgumentException>()
            .WithMessage("*configured reconciliation import root*");
    }

    [Fact]
    public async Task CreateStatementRunAsync_StagedSourcePath_ShouldComputeHashAndPersistCanonicalPath()
    {
        var importRoot = CreateTempDirectory("statement-import-root");
        var sourceFile = Path.Combine(importRoot, "statement.csv");
        await File.WriteAllTextAsync(sourceFile, "account,symbol,quantity\nfund-1,MSFT,10\n");
        var store = new RecordingCanonicalStatementStore();
        var service = CreateService(importRoot, store);

        var run = await service.CreateStatementRunAsync(CreateRequest(sourceFile, importedBy: "ops-user", sourceFileHash: "CALLERHASH"));

        var expectedHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(sourceFile)));
        run.Should().NotBeNull();
        run!.SourceFileHash.Should().Be(expectedHash);
        store.SavedImports.Should().ContainSingle();
        store.SavedImports[0].SourcePath.Should().Be(Path.GetFullPath(sourceFile));
        store.SavedImports[0].SourceFileHash.Should().Be(expectedHash);
        store.SavedImports[0].ImportedBy.Should().Be("ops-user");
    }

    [Fact]
    public async Task CreateStatementRunAsync_OversizedStagedSourcePath_ShouldRejectBeforeHashing()
    {
        var importRoot = CreateTempDirectory("statement-import-root");
        var sourceFile = Path.Combine(importRoot, "oversized.csv");
        await using (var stream = new FileStream(sourceFile, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            stream.SetLength(100L * 1024L * 1024L + 1L);
        }
        var service = CreateService(importRoot);

        var act = async () => await service.CreateStatementRunAsync(CreateRequest(sourceFile));

        await act.Should()
            .ThrowAsync<ArgumentException>()
            .WithMessage("*workstation import limit*");
    }

    private static ReconciliationApiService CreateService(
        string importRoot,
        RecordingCanonicalStatementStore? store = null)
        => new(
            store ?? new RecordingCanonicalStatementStore(),
            new EmptyReconciliationCaseStore(),
            new EmptyReconciliationBreakStore(),
            importRoot);

    private static StatementRunCreateDto CreateRequest(
        string sourcePath,
        string importedBy = "operator",
        string? sourceFileHash = null)
        => new(
            Broker: "samplebroker",
            SourceInstitution: "Sample Custodian",
            FundAccountId: "fund-1",
            ExternalAccountId: "ext-1",
            StatementPeriodStart: new DateOnly(2026, 5, 1),
            StatementPeriodEnd: new DateOnly(2026, 5, 27),
            SourcePath: sourcePath,
            OriginalFileName: "statement.csv",
            MappingProfileId: "mapping-v1",
            ToleranceProfileId: "tolerance-v1",
            ImportedBy: importedBy,
            SourceFileHash: sourceFileHash);

    private static string CreateTempDirectory(string prefix)
    {
        var path = Path.Combine(Path.GetTempPath(), "meridian-tests", prefix, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class RecordingCanonicalStatementStore : ICanonicalStatementStore
    {
        public List<CanonicalStatementImport> SavedImports { get; } = [];

        public Task<bool> ImportExistsByChecksumAsync(string checksum, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<bool> ImportExistsByDuplicateKeyAsync(string duplicateKey, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task SaveImportAsync(CanonicalStatementImport import, IReadOnlyList<CanonicalStatementRow> rows, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            SavedImports.Add(import);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<CanonicalStatementImport>> ListImportsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CanonicalStatementImport>>(SavedImports);
    }

    private sealed class EmptyReconciliationCaseStore : IReconciliationCaseStore
    {
        public Task SaveAsync(ReconciliationCase reconciliationCase, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<ReconciliationCase?> GetAsync(string caseId, CancellationToken ct = default)
            => Task.FromResult<ReconciliationCase?>(null);

        public Task<IReadOnlyList<ReconciliationCase>> ListAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ReconciliationCase>>([]);
    }

    private sealed class EmptyReconciliationBreakStore : IReconciliationBreakStore
    {
        public Task WriteAsync(IReadOnlyList<ReconciliationBreakRecord> records, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<ReconciliationBreakRecord>> ListOpenAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ReconciliationBreakRecord>>([]);
    }
}
