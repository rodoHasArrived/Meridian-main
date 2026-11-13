using System.Text;
using FluentAssertions;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Evidence;
using Microsoft.Extensions.Logging;

namespace Meridian.Tests.Ui.Evidence;

public sealed class FileEvidenceArtifactStoreLoggingTests : IDisposable
{
    private const string Tenant = "tenant-a";
    private const string Scope = "company-a";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-tests", "evidence-logs",
        Guid.NewGuid().ToString("N"), "path\u2028FORGED");

    [Fact]
    public async Task Intake_EscapesLogValuesWithoutChangingRetainedSubjectOrContent()
    {
        const string subjectId = "report\r\nFORGED\u0085NEXT\u2028LINE\u2029END";
        var bytes = Encoding.UTF8.GetBytes("exact retained evidence\r\nwith its original bytes");
        var logger = new RecordingLogger();
        var store = new FileEvidenceArtifactStore(_root, logger);

        var retained = await store.WriteIntakeArtifactAsync(Intake(subjectId, bytes));
        var loaded = await store.TryGetVaultIdentityAsync(retained.VaultIdentity.VaultId, Tenant, Scope);

        retained.SubjectId.Should().Be(subjectId);
        loaded.Should().NotBeNull();
        loaded!.SubjectId.Should().Be(subjectId);
        retained.ContentHashSha256.Should().Be(Sha256Digest.Compute(bytes));
        (await File.ReadAllBytesAsync(Path.Combine(_root, retained.RelativePath))).Should().Equal(bytes);
        (await store.VerifyRetainedContentAsync(retained.VaultIdentity.VaultId, Tenant, Scope)).Should().BeTrue();

        var entry = logger.Entries.Should().ContainSingle(item => item.Level == LogLevel.Information).Which;
        entry.Values["SubjectId"].Should().Be("report\\r\\nFORGED\\u0085NEXT\\u2028LINE\\u2029END");
        AssertSingleLine(entry);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CorruptRetainedFile_KeepsDiagnosticsSafeWithoutChangingEvidence(bool corruptIndex)
    {
        var bytes = Encoding.UTF8.GetBytes("retained original content");
        var logger = new RecordingLogger();
        var store = new FileEvidenceArtifactStore(_root, logger);
        var retained = await store.WriteIntakeArtifactAsync(Intake("report-a", bytes));
        var targetPath = corruptIndex
            ? Path.Combine(_root, "workstation", "evidence", "_vault", retained.VaultIdentity.VaultId + ".json")
            : Path.Combine(_root, retained.VaultIdentity.ManifestPath);
        const string corruptJson = "{ malformed retained json";
        await File.WriteAllTextAsync(targetPath, corruptJson);
        logger.Entries.Clear();

        if (corruptIndex)
            (await store.TryGetVaultIdentityAsync(retained.VaultIdentity.VaultId, Tenant, Scope)).Should().BeNull();
        else
            (await store.TryOpenManifestByVaultIdAsync(retained.VaultIdentity.VaultId, Tenant, Scope)).Should().BeNull();

        var warnings = logger.Entries.Where(item => item.Level == LogLevel.Warning).ToArray();
        warnings.Should().NotBeEmpty();
        foreach (var entry in warnings)
        {
            AssertSingleLine(entry);
            if (corruptIndex)
            {
                entry.Values["ErrorKind"].Should().Be(nameof(System.Text.Json.JsonException));
                entry.Values.Keys.Should().BeEquivalentTo(new[] { "ErrorKind", "{OriginalFormat}" });
                entry.Values.Should().NotContainKey("IndexPath").And.NotContainKey("Error");
                var diagnostics = string.Join(" ", entry.Values.Values.Select(value => value?.ToString()).Append(entry.Message));
                diagnostics.Should().NotContain(_root).And.NotContain("FORGED").And.NotContain(corruptJson)
                    .And.NotContain(typeof(System.Text.Json.JsonException).FullName!);
            }
            else
            {
                entry.Values["ManifestPath"]!.ToString().Should().Contain("path\\u2028FORGED");
                entry.Values["Error"]!.ToString().Should().Contain(nameof(System.Text.Json.JsonException));
            }
        }
        (await File.ReadAllTextAsync(targetPath)).Should().Be(corruptJson, "a failed read must not rewrite retained evidence");
        (await File.ReadAllBytesAsync(Path.Combine(_root, retained.RelativePath))).Should().Equal(bytes);
        retained.ContentHashSha256.Should().Be(Sha256Digest.Compute(bytes));
    }

    private static EvidenceVaultIntakeRequestDto Intake(string subjectId, byte[] bytes)
        => new("report-pack", subjectId, "api", "report.dat", Convert.ToBase64String(bytes))
        {
            TenantId = Tenant,
            Scope = Scope
        };

    private static void AssertSingleLine(LogEntry entry)
    {
        entry.Exception.Should().BeNull("unescaped exception text can repeat untrusted file paths");
        foreach (var text in entry.Values.Values.Select(value => value?.ToString()).Append(entry.Message))
        {
            text.Should().NotBeNull();
            text!.IndexOfAny(['\r', '\n', '\u0085', '\u2028', '\u2029']).Should().Be(-1);
        }
    }

    private sealed record LogEntry(LogLevel Level, string Message, Exception? Exception,
        IReadOnlyDictionary<string, object?> Values);

    private sealed class RecordingLogger : ILogger<FileEvidenceArtifactStore>
    {
        public List<LogEntry> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var values = ((IEnumerable<KeyValuePair<string, object?>>)state!)
                .ToDictionary(pair => pair.Key, pair => pair.Value);
            Entries.Add(new(logLevel, formatter(state, exception), exception, values));
        }
    }

    public void Dispose()
    {
        var testDirectory = Directory.GetParent(_root)!.FullName;
        if (Directory.Exists(testDirectory))
            Directory.Delete(testDirectory, recursive: true);
    }
}
