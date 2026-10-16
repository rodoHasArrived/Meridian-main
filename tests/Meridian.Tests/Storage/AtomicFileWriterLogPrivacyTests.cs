using System.Collections.Concurrent;
using System.Text;
using FluentAssertions;
using Meridian.Storage.Archival;
using Meridian.Tests.Infrastructure;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace Meridian.Tests.Storage;

[Collection("Sequential")]
public sealed class AtomicFileWriterLogPrivacyTests : TempDirectoryTestBase
{
    [Fact]
    public async Task WriteReplaceAndChecksumDiagnostics_DoNotRetainAccountPathsOrContents()
    {
        const string accountId = "private-financial-account-f270c071";
        const string privateContent = "confidential-broker-account-balance";
        var path = Path.Combine(TestDataRoot, accountId, "portfolio.json");
        using var capture = new LogCapture();

        AtomicFileWriter.Write(path, privateContent);
        await AtomicFileWriter.WriteAsync(path, privateContent);
        await AtomicFileWriter.WriteAsync(path, Encoding.UTF8.GetBytes(privateContent));
        await AtomicFileWriter.ReplaceAsync(path, privateContent, keepBackup: false);
        await AtomicFileWriter.WriteWithChecksumAsync(path, Encoding.UTF8.GetBytes(privateContent));
        (await AtomicFileWriter.VerifyChecksumAsync(path)).Should().BeTrue();
        (await File.ReadAllTextAsync(path)).Should().Be(privateContent);

        // Sidecar data is not trusted logging content either: a corrupt sidecar can contain
        // financial identifiers instead of a digest. Verification still reports failure.
        await File.WriteAllTextAsync(path + ".sha256", accountId + "-" + privateContent);
        (await AtomicFileWriter.VerifyChecksumAsync(path)).Should().BeFalse();
        File.Delete(path + ".sha256");
        (await AtomicFileWriter.VerifyChecksumAsync(path)).Should().BeFalse();

        capture.Events.Should().Contain(entry => entry.Properties.ContainsKey("Bytes"));
        capture.Events.Should().Contain(entry => entry.MessageTemplate.Text == "Atomically replaced file");
        capture.Events.Should().Contain(entry => entry.MessageTemplate.Text == "File checksum verified");
        capture.Events.Should().Contain(entry => entry.MessageTemplate.Text == "File checksum mismatch");
        capture.Events.Should().Contain(entry => entry.MessageTemplate.Text == "Checksum sidecar file not found");
        AssertPrivateValuesAbsent(capture.Events, accountId, privateContent, TestDataRoot, path);
    }

    [Fact]
    public async Task DirectoryFsyncFailure_LogsErrnoWithoutAccountDirectory()
    {
        if (!OperatingSystem.IsLinux())
            return;
        const string accountId = "private-financial-account-fsync";
        var path = Path.Combine(TestDataRoot, accountId);
        // procfs permits directory opens but refuses fsync. A private named symlink exercises
        // the real failure diagnostic deterministically without depending on root permissions.
        Directory.CreateSymbolicLink(path, "/proc/self");
        try
        {
            using var capture = new LogCapture();
            await AtomicFileWriter.SyncDirectoryAsync(path);

            capture.Events.Should().ContainSingle(entry => entry.MessageTemplate.Text == "Directory fsync failed (errno {Errno})")
                .Which.Properties.Should().ContainKey("Errno");
            AssertPrivateValuesAbsent(capture.Events, accountId, TestDataRoot, path);
        }
        finally
        {
            Directory.Delete(path);
        }
    }

    private static void AssertPrivateValuesAbsent(IEnumerable<LogEvent> entries, params string[] privateValues)
    {
        foreach (var entry in entries)
        {
            entry.Exception.Should().BeNull("exception messages can include account paths");
            entry.Properties.Should().ContainKey("SourceContext");
            var emitted = entry.RenderMessage() + string.Join(";", entry.Properties.Select(pair => pair.Value.ToString()));
            foreach (var privateValue in privateValues)
                emitted.Should().NotContain(privateValue);
        }
    }

    private sealed class LogCapture : ILogEventSink, IDisposable
    {
        private readonly Serilog.ILogger _previous = Log.Logger;
        private readonly Logger _logger;
        private bool _disposed;
        public ConcurrentQueue<LogEvent> Events { get; } = new();

        public LogCapture()
        {
            _logger = new LoggerConfiguration().MinimumLevel.Debug().WriteTo.Sink(this).CreateLogger();
            Log.Logger = _logger;
        }

        public void Emit(LogEvent logEvent) => Events.Enqueue(logEvent);

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            Log.Logger = _previous;
            _logger.Dispose();
        }
    }
}
