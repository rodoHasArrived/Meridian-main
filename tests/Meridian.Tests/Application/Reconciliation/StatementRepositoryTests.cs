using FluentAssertions;
using Meridian.FinancialOperations.Reconciliation;
using WorkstationReconciliationBreakSeverity = Meridian.Contracts.Workstation.ReconciliationBreakSeverity;
using Meridian.Domain.Reconciliation;

namespace Meridian.Tests.Application.Reconciliation;

public sealed class StatementRepositoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-statement-repository-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SaveAsync_persists_manifest_with_hash_profile_versions_and_raw_file_policy_reference()
    {
        var repository = new FileStatementRunRepository(_root);
        var request = new StatementRunCreateRequest(
            "SampleBroker",
            "Sample Custodian",
            "fund-1",
            "external-1",
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 31),
            "/secure/inbound/statement.csv",
            "statement.csv",
            "sample-broker-csv-v1",
            "cash-tolerance-v2",
            "ops@example.test",
            "ABC123HASH");
        var manifest = StatementRunManifest.FromRequest(
            "run-001",
            "import-001",
            request,
            mappingProfileVersion: "sample-broker-csv-v1@2026-05-28",
            toleranceProfileVersion: "cash-tolerance-v2@2026-05-28",
            createdAtUtc: new DateTimeOffset(2026, 5, 28, 10, 0, 0, TimeSpan.Zero),
            rawRowCount: 10,
            normalizedRowCount: 9);

        await repository.SaveAsync(manifest);

        var loaded = await repository.GetAsync("run-001");
        loaded.Should().BeEquivalentTo(manifest);
        loaded!.RawFile.RetentionMode.Should().Be(StatementRawFileRetentionMode.ExternalReferenceOnly);
        loaded.RawFile.SourcePath.Should().Be("/secure/inbound/statement.csv");
        loaded.RawFile.SourceFileHash.Should().Be("ABC123HASH");
        loaded.RawFile.PolicyNote.Should().Contain("not copied");
        File.Exists(Path.Combine(_root, "reconciliation", "statement-runs", "run-001", "manifest.json")).Should().BeTrue();
    }

    [Fact]
    public async Task Repositories_round_trip_validation_entities_matches_breaks_and_case_links()
    {
        var validationRepository = new FileStatementValidationIssueRepository(_root);
        var entityRepository = new FileStatementNormalizedEntityRepository(_root);
        var matchRepository = new FileStatementMatchResultRepository(_root);
        var issue = new StatementValidationIssueDto(
            StatementValidationIssueCode.UnresolvedSecurity,
            StatementValidationIssueSeverity.Soft,
            4,
            "symbol",
            "Ticker requires review.");
        var sourceRow = new StatementSourceRowReference(
            "import-001",
            4,
            "row-hash-4",
            new Dictionary<string, string> { ["sourcePath"] = "/secure/inbound/statement.csv" });
        var entities = new StatementNormalizedEntities(
            "run-001",
            "import-001",
            Positions:
            [
                new StatementPosition(
                    "import-001",
                    4,
                    "row-hash-4",
                    sourceRow.RawSnapshot,
                    "fund-1",
                    "external-1",
                    null,
                    "XYZ",
                    "USD",
                    100m,
                    12.34m,
                    1234m,
                    new DateOnly(2026, 5, 27),
                    new DateOnly(2026, 5, 29))
            ],
            CashBalances: [],
            Transactions: [],
            Securities: [],
            SourceRows: [sourceRow]);
        var matchResult = new StatementMatchResultArtifact(
            "run-001",
            "import-001",
            Matches:
            [
                new ReconciliationMatchLink(
                    "import-001:4",
                    "position-123",
                    null,
                    null,
                    null,
                    "evidence://session/123",
                    "evidence://run/001",
                    "high",
                    "Symbol and quantity aligned.")
            ],
            Breaks:
            [
                new StatementBreakRecord(
                    "break-001",
                    "run-001",
                    "import-001",
                    "row-hash-4",
                    StatementBreakClassificationType.SecurityMappingBreak,
                    WorkstationReconciliationBreakSeverity.Medium,
                    StatementBreakRecommendedAction.MapSecurity,
                    0m,
                    true,
                    true,
                    "Open",
                    "Security must be mapped before close.",
                    new DateTimeOffset(2026, 5, 28, 10, 5, 0, TimeSpan.Zero))
            ],
            CaseLinks:
            [
                new StatementCaseLink(
                    "case-001",
                    "run-001",
                    "import-001",
                    "break-001",
                    "row-hash-4",
                    "evidence://cases/case-001",
                    new DateTimeOffset(2026, 5, 28, 10, 6, 0, TimeSpan.Zero),
                    "Material unresolved break promoted to casework.")
            ]);

        await validationRepository.SaveAsync("run-001", [issue]);
        await entityRepository.SaveAsync("run-001", entities);
        await matchRepository.SaveAsync("run-001", matchResult);

        (await validationRepository.GetAsync("run-001")).Should().BeEquivalentTo([issue]);
        (await entityRepository.GetAsync("run-001")).Should().BeEquivalentTo(entities);
        (await matchRepository.GetAsync("run-001")).Should().BeEquivalentTo(matchResult);

        File.Exists(Path.Combine(_root, "reconciliation", "statement-runs", "run-001", "validation-issues.json")).Should().BeTrue();
        File.Exists(Path.Combine(_root, "reconciliation", "statement-runs", "run-001", "normalized-entities.json")).Should().BeTrue();
        File.Exists(Path.Combine(_root, "reconciliation", "statement-runs", "run-001", "match-results.json")).Should().BeTrue();
        File.Exists(Path.Combine(_root, "reconciliation", "statement-runs", "run-001", "breaks.json")).Should().BeTrue();
        File.Exists(Path.Combine(_root, "reconciliation", "statement-runs", "run-001", "case-links.json")).Should().BeTrue();
    }

    [Fact]
    public async Task SaveAsync_observes_cancellation_before_writing_artifacts()
    {
        var repository = new FileStatementRunRepository(_root);
        var manifest = new StatementRunManifest(
            "run-cancelled",
            "import-cancelled",
            "broker",
            "institution",
            "fund-1",
            "external-1",
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 31),
            DateTimeOffset.UtcNow,
            "ops",
            StatementRawFileEvidenceReference.ExternalReference("/secure/inbound/cancelled.csv", "cancelled.csv", "HASH"),
            new StatementProfileVersion("mapping", "mapping@v1"),
            new StatementProfileVersion("tolerance", "tolerance@v1"),
            "duplicate-key");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () => await repository.SaveAsync(manifest, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        File.Exists(Path.Combine(_root, "reconciliation", "statement-runs", "run-cancelled", "manifest.json")).Should().BeFalse();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
