using FluentAssertions;
using Meridian.Application.Commands;
using Meridian.Application.FundStructure;
using Meridian.Storage.FundStructure;
using Meridian.Testing;
using System.Text.Json;

namespace Meridian.Tests.Application.FundStructure;

/// <summary>Operator invocation remains explicit and connection failures never echo credentials.</summary>
public sealed class FundStructureTenantBackfillCommandTests
{
    [Fact]
    public async Task Execute_MissingExplicitMode_RefusesBeforeCreatingDatabaseRunner()
    {
        using var output = new StringWriter();
        var creates = 0;
        var command = new FundStructureTenantBackfillCommand(() => { creates++; throw new InvalidOperationException(); }, output);

        var result = await command.ExecuteAsync(["--fund-tenant-backfill", "--output", "unused.json"]);

        result.Success.Should().BeFalse();
        creates.Should().Be(0);
        output.ToString().Should().Contain("--action preview|apply");
    }

    [Fact]
    public async Task Execute_ConnectionError_RedactsExceptionAndRawArguments()
    {
        using var output = new StringWriter();
        var command = new FundStructureTenantBackfillCommand(
            () => throw new InvalidOperationException("Host=private;Password=do-not-echo"), output);

        var result = await command.ExecuteAsync(["--fund-tenant-backfill", "--action", "preview", "--output", "unused.json"]);

        result.Success.Should().BeFalse();
        output.ToString().Should().Contain("InvalidOperationException");
        output.ToString().Should().NotContain("Password").And.NotContain("do-not-echo").And.NotContain("private");
    }
    [Theory]
    [InlineData("preview")]
    [InlineData("apply")]
    public async Task Execute_EvidenceOutput_RestrictsNewAndReplacedFiles(string action)
    {
        using var artifacts = TestArtifactDirectory.Create(nameof(FundStructureTenantBackfillCommandTests));
        var path = Path.Combine(artifacts.RootPath, "tenant-evidence.json");
        var store = new EvidenceStore();
        using var output = new StringWriter();
        var command = new FundStructureTenantBackfillCommand(() => new(store), output);
        string[] args = ["--fund-tenant-backfill", "--action", action, "--output", path,
            "--run-id", store.Receipt.RunId.ToString(), "--plan-hash", store.Receipt.PlanHash,
            "--operator", "operator", "--review-reference", "review/123"];

        (await command.ExecuteAsync(args)).Success.Should().BeTrue();
        File.Exists(path).Should().BeTrue();
        if (!OperatingSystem.IsWindows())
        {
            File.GetUnixFileMode(path).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
            (await command.ExecuteAsync(args)).Success.Should().BeTrue();
            File.GetUnixFileMode(path).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private sealed class EvidenceStore : IFundStructureTenantBackfillStore
    {
        public FundStructureTenantBackfillReceipt Receipt { get; } = new(Guid.NewGuid(), new string('a', 64),
            "operator", "review/123", DateTimeOffset.UtcNow, 0, 0, JsonSerializer.SerializeToElement(new { }));
        public Task<FundStructureTenantBackfillReceipt?> FindReceiptAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult<FundStructureTenantBackfillReceipt?>(id == Receipt.RunId ? Receipt : null);
        public Task<IFundStructureTenantBackfillSession> OpenSessionAsync(CancellationToken ct = default)
            => Task.FromResult<IFundStructureTenantBackfillSession>(new EvidenceSession());
        private sealed class EvidenceSession : IFundStructureTenantBackfillSession
        {
            public FundStructureTenantBackfillSnapshot Snapshot { get; } = new("source", "schema", [], [], []);
            public Task<FundStructureTenantBackfillReceipt?> FindReceiptAsync(Guid id, CancellationToken ct)
                => Task.FromResult<FundStructureTenantBackfillReceipt?>(null);
            public Task<FundStructureTenantBackfillReceipt> CommitAsync(Guid id, string hash, string actor, string review,
                JsonElement plan, IReadOnlyList<FundStructureTenantBackfillStamp> stamps,
                IReadOnlyList<FundStructureTenantBackfillException> exceptions, CancellationToken ct)
                => throw new InvalidOperationException("This fixture must never mutate.");
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
