using FluentAssertions;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Workstation;
using Meridian.Storage.Operations;
using Meridian.Strategies.Artifacts;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Live;
using Meridian.Strategies.Live.Designer;
using Meridian.Strategies.Models;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Xunit;

namespace Meridian.Tests.Strategies;

/// <summary>
/// Guards a reviewed equity design against edited executable inputs, foreign drafts and damaged
/// retained artifacts, while keeping a repeated experiment distinct from its strategy revision.
/// </summary>
public sealed class ScopedDesignerArtifactTests
{
    [Fact]
    public async Task SavedEquityDesign_RestartPreservesArtifactWhileNewReplayChangesRunIdentity()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var fixture = await Fixture.CreateAsync(timeout.Token);
        var artifact = StrategyArtifact.FromScopedDesigner(fixture.Document, fixture.Scope, fixture.Realism);
        var artifactId = await fixture.Artifacts.RetainAsync(artifact, timeout.Token);
        var first = fixture.CreateRun(artifactId, new string('a', 64));
        var second = fixture.CreateRun(artifactId, new string('b', 64));
        await fixture.Runs.RecordRunAsync(first, timeout.Token);
        await fixture.Runs.RecordRunAsync(second, timeout.Token);

        second.StrategyArtifactId.Should().Be(first.StrategyArtifactId);
        second.InputHashSha256.Should().NotBe(first.InputHashSha256,
            "a different historical snapshot requires its own exact-run review");
        var restartedRuns = new StrategyRunStore(new FileOperationalCaseHistoryStore(fixture.Root.FullName));
        (await restartedRuns.GetRunByIdAsync(first.RunId, timeout.Token))!.InputHashSha256.Should().Be(first.InputHashSha256);
        (await restartedRuns.GetRunByIdAsync(second.RunId, timeout.Token))!.StrategyArtifactId.Should().Be(artifactId);
        var restartedArtifacts = new StrategyArtifactRegistry(fixture.Root.FullName);
        (await restartedArtifacts.GetAsync(artifactId, timeout.Token))!.ComputeId().Should().Be(artifactId);
        var source = new DesignerDocumentLiveSource(null, fixture.DesignService,
            artifacts: restartedArtifacts,
            scopedDesigns: new ScopedStrategyDesignStore(new FileOperationalCaseHistoryStore(fixture.Root.FullName)));
        source.TryCreate(fixture.Context(artifactId), out var strategy, out var reason).Should().BeTrue(reason);
        strategy.Should().NotBeNull();

        var changedSizing = fixture.Document with
        {
            Cells = fixture.Document.Cells.Select(cell => cell.CellId == "fixed-shares"
                ? cell with { Parameters = new Dictionary<string, string>(cell.Parameters!) { ["sizingValue"] = "20" } }
                : cell).ToArray()
        };
        var sizingId = await fixture.Artifacts.RetainAsync(
            StrategyArtifact.FromScopedDesigner(changedSizing, fixture.Scope, fixture.Realism), timeout.Token);
        sizingId.Should().NotBe(artifactId);
        var costRequest = fixture.Request with { SlippageBasisPoints = 5m };
        var costId = await fixture.Artifacts.RetainAsync(StrategyArtifact.FromScopedDesigner(
            fixture.Document, fixture.Scope, costRequest.ToRealismDescriptor()), timeout.Token);
        costId.Should().NotBe(artifactId);

        // Cosmetic save metadata is not executable identity, but the run still pins its exact draft revision.
        (await fixture.Artifacts.RetainAsync(StrategyArtifact.FromScopedDesigner(
            fixture.Document with { Name = "Renamed design", Revision = "another-save" }, fixture.Scope, fixture.Realism),
            timeout.Token)).Should().Be(artifactId);
    }

    [Theory]
    [InlineData("missing-artifact")]
    [InlineData("tampered-artifact")]
    [InlineData("foreign-company")]
    [InlineData("changed-version")]
    [InlineData("changed-realism")]
    [InlineData("changed-draft")]
    [InlineData("artifact-from-another-scope")]
    [InlineData("missing-scoped-store")]
    [InlineData("removed-version")]
    public async Task ReviewedEquityDesign_ConflictingOrUnavailableIdentityRefusesActivation(string failure)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var fixture = await Fixture.CreateAsync(timeout.Token);
        var artifactId = await fixture.Artifacts.RetainAsync(
            StrategyArtifact.FromScopedDesigner(fixture.Document, fixture.Scope, fixture.Realism), timeout.Token);
        var context = fixture.Context(artifactId);
        var parameters = new Dictionary<string, string>(context.Parameters);
        switch (failure)
        {
            case "missing-artifact":
                File.Delete(Path.Combine(fixture.Root.FullName, "strategies", "artifacts", artifactId + ".json"));
                break;
            case "tampered-artifact":
                var path = Path.Combine(fixture.Root.FullName, "strategies", "artifacts", artifactId + ".json");
                var payload = await File.ReadAllTextAsync(path, timeout.Token);
                var altered = payload.Replace("\"sizingValue\":\"10\"", "\"sizingValue\":\"20\"", StringComparison.Ordinal);
                altered.Should().NotBe(payload);
                await File.WriteAllTextAsync(path, altered, timeout.Token);
                break;
            case "foreign-company":
                parameters[DesignerStrategyVersion.CompanyKey] = "foreign-company";
                break;
            case "changed-version":
                parameters[DesignerStrategyVersion.VersionKey] = new string('0', 64);
                break;
            case "changed-realism":
                parameters[DesignerStrategyVersion.RealismKey] = new string('0', 64);
                break;
            case "changed-draft":
                await fixture.Designs.SaveAsync(fixture.Scope, "operator", fixture.Document with { Name = "New saved revision" },
                    fixture.Document.Revision, timeout.Token);
                break;
            case "artifact-from-another-scope":
                parameters[DesignerDocumentLiveSource.RetainedArtifactParameterKey] = await fixture.Artifacts.RetainAsync(
                    StrategyArtifact.FromScopedDesigner(fixture.Document,
                        fixture.Scope with { CompanyId = "foreign-company" }, fixture.Realism), timeout.Token);
                break;
            case "removed-version":
                parameters.Remove(DesignerStrategyVersion.VersionKey);
                break;
        }

        var source = new DesignerDocumentLiveSource(null, fixture.DesignService, artifacts: fixture.Artifacts,
            scopedDesigns: failure == "missing-scoped-store" ? null : fixture.Designs);
        source.TryCreate(context with { Parameters = parameters }, out var strategy, out var reason).Should().BeFalse();
        strategy.Should().BeNull();
        reason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task LegacyScopedRun_RetainsOriginalHistoryAndCannotAcquireArtifactByLifecycleUpdate()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var fixture = await Fixture.CreateAsync(timeout.Token);
        var legacy = fixture.CreateRun(artifactId: null, new string('a', 64));
        await fixture.Runs.RecordRunAsync(legacy, timeout.Token);
        var source = new DesignerDocumentLiveSource(null, fixture.DesignService, scopedDesigns: fixture.Designs);
        source.TryCreate(fixture.Context(artifactId: null), out _, out var reason).Should().BeTrue(reason);

        var artifactId = await fixture.Artifacts.RetainAsync(
            StrategyArtifact.FromScopedDesigner(fixture.Document, fixture.Scope, fixture.Realism), timeout.Token);
        var rewrite = () => fixture.Runs.RecordRunAsync(legacy.WithStrategyArtifact(artifactId), timeout.Token);
        await rewrite.Should().ThrowAsync<InvalidOperationException>();
        var retained = await new StrategyRunStore(new FileOperationalCaseHistoryStore(fixture.Root.FullName))
            .GetRunByIdAsync(legacy.RunId, timeout.Token);
        retained!.StrategyArtifactId.Should().BeNull();
        retained.InputHashSha256.Should().Be(legacy.InputHashSha256);
    }

    private sealed class Fixture : IDisposable
    {
        public DirectoryInfo Root { get; } = Directory.CreateTempSubdirectory("meridian-scoped-artifact-");
        public StrategyRunRepositoryScope Scope { get; } = new("synthetic-tenant", "synthetic-company");
        public StrategyDesignService DesignService { get; } = new();
        public BacktestRequest Request { get; } = new(new DateOnly(2024, 1, 2), new DateOnly(2024, 1, 5),
            Symbols: ["SPY"], SlippageBasisPoints: 0m, AdjustForCorporateActions: false, FillTiming: FillTiming.NextBar);
        public ExecutionRealismDescriptor Realism => Request.ToRealismDescriptor();
        public StrategyDesignDocument Document { get; private set; } = null!;
        public ScopedStrategyDesignStore Designs { get; }
        public StrategyRunStore Runs { get; }
        public StrategyArtifactRegistry Artifacts { get; }

        private Fixture()
        {
            var history = new FileOperationalCaseHistoryStore(Root.FullName);
            Designs = new ScopedStrategyDesignStore(history);
            Runs = new StrategyRunStore(history);
            Artifacts = new StrategyArtifactRegistry(Root.FullName);
        }

        public static async Task<Fixture> CreateAsync(CancellationToken ct)
        {
            var fixture = new Fixture();
            try
            {
                var document = fixture.DesignService.GetTemplates().Single(template => template.TemplateId == "equity-price-filter").Document;
                fixture.Document = (await fixture.Designs.SaveAsync(fixture.Scope, "operator", document, null, ct))!;
                return fixture;
            }
            catch
            {
                fixture.Dispose();
                throw;
            }
        }

        public LiveStrategyCreationContext Context(string? artifactId)
        {
            var parameters = Parameters();
            if (artifactId is not null)
                parameters[DesignerDocumentLiveSource.RetainedArtifactParameterKey] = artifactId;
            return new(DesignerStrategyVersion.ComputeStrategyId(Scope, Document.DocumentId), parameters);
        }

        public StrategyRunEntry CreateRun(string? artifactId, string dataSnapshot)
        {
            var parameters = Parameters();
            parameters[DesignerStrategyVersion.DataSnapshotKey] = dataSnapshot;
            var run = StrategyRunEntry.StartForFundAccount(
                DesignerStrategyVersion.ComputeStrategyId(Scope, Document.DocumentId), Document.Name, RunType.Backtest,
                Guid.Parse("bc6a7728-54da-45d4-94c4-103d612d776a"),
                datasetReference: Document.DatasetReference, parameterSet: parameters, executionRealism: Realism) with
            {
                DataProvenanceToken = "simulated"
            };
            return artifactId is null ? run : run.WithStrategyArtifact(artifactId);
        }

        private Dictionary<string, string> Parameters()
        {
            var realismHash = DesignerStrategyVersion.ComputeRealismHash(Realism);
            return new(StringComparer.Ordinal)
            {
                [DesignerStrategyVersion.TenantKey] = Scope.TenantId!,
                [DesignerStrategyVersion.CompanyKey] = Scope.CompanyId!,
                [DesignerDocumentLiveSource.DesignerDocumentParameterKey] = Document.DocumentId,
                [DesignerStrategyVersion.DraftRevisionKey] = Document.Revision!,
                [DesignerStrategyVersion.VersionKey] = DesignerStrategyVersion.Compute(Document, realismHash),
                [DesignerStrategyVersion.RealismKey] = realismHash,
                [DesignerStrategyVersion.DataProvenanceKey] = "simulated",
                ["symbols"] = "SPY"
            };
        }

        public void Dispose() => Root.Delete(recursive: true);
    }
}
