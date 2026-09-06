using System.Diagnostics;
using Meridian.ProcessTestHelper;
using Meridian.Storage.Archival;

namespace Meridian.Tests.Storage;

public sealed class WriteAheadLogProcessTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task IdleDelayedFlush_SurvivesWriterTerminationWithoutDispose()
    {
        var root = Path.Combine(Path.GetTempPath(), $"meridian-wal-process-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var ready = Path.Combine(root, "writer.ready");
        var marker = $"retained-{Guid.NewGuid():N}";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        };
        foreach (var argument in new[]
        {
            "exec", "--depsfile", Path.Combine(AppContext.BaseDirectory, "Meridian.Tests.deps.json"),
            "--runtimeconfig", Path.Combine(AppContext.BaseDirectory, "Meridian.Tests.runtimeconfig.json"),
            typeof(ProcessTestHelperMarker).Assembly.Location, "wal-append-and-wait", root, ready, marker
        })
            start.ArgumentList.Add(argument);
        using var writer = Process.Start(start) ?? throw new InvalidOperationException("WAL writer did not start.");
        var errors = writer.StandardError.ReadToEndAsync();
        try
        {
            while (!File.Exists(ready))
            {
                Assert.False(writer.HasExited, writer.HasExited ? await errors : "");
                await Task.Delay(20, timeout.Token);
            }

            // Observe a bounded idle flush while the producer is still alive. No later append,
            // explicit flush, commit, or graceful shutdown is allowed to make the record visible.
            using var flushDeadline = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
            flushDeadline.CancelAfter(TimeSpan.FromSeconds(5));
            var visible = false;
            while (!visible)
            {
                Assert.False(writer.HasExited);
                foreach (var walPath in Directory.GetFiles(root, "*.wal"))
                {
                    await using var stream = new FileStream(walPath, FileMode.Open, FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete);
                    using var reader = new StreamReader(stream);
                    visible |= (await reader.ReadToEndAsync(flushDeadline.Token)).Contains(marker, StringComparison.Ordinal);
                }
                if (!visible)
                    await Task.Delay(20, flushDeadline.Token);
            }

            writer.Kill(entireProcessTree: true);
            await writer.WaitForExitAsync(timeout.Token);

            await using var recovered = new WriteAheadLog(root);
            await recovered.InitializeAsync(timeout.Token);
            var records = new List<WalRecord>();
            await foreach (var retained in recovered.GetUncommittedRecordsAsync(timeout.Token))
                records.Add(retained);
            var record = Assert.Single(records);
            Assert.Equal("PROCESS-RECOVERY", record.RecordType);
            Assert.Equal(marker, record.DeserializePayload<string>());
        }
        finally
        {
            if (!writer.HasExited)
                writer.Kill(entireProcessTree: true);
            await writer.WaitForExitAsync();
            Directory.Delete(root, recursive: true);
        }
    }
}
