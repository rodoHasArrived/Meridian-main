using System.Diagnostics;
using System.Text.Json;
using Meridian.ProcessTestHelper;
using Meridian.Storage.Services;

namespace Meridian.Tests.Storage;

public sealed class AuditChainProcessTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task IndependentProcesses_AppendEveryFileToOneVerifiableChain()
    {
        var root = Path.Combine(Path.GetTempPath(), $"meridian-audit-process-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(75));
        var processes = new List<(Process Process, Task<string> Error)>();
        try
        {
            var gate = Path.Combine(root, "start.gate");
            var readiness = new List<string>();
            for (var index = 0; index < 3; index++)
            {
                var ready = Path.Combine(root, $"writer-{index}.ready");
                readiness.Add(ready);
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
                    typeof(ProcessTestHelperMarker).Assembly.Location,
                    "audit-append-batch", root, $"writer-{index}", ready, gate, "12"
                })
                    start.ArgumentList.Add(argument);
                var process = Process.Start(start) ?? throw new InvalidOperationException("Audit writer did not start.");
                processes.Add((process, process.StandardError.ReadToEndAsync()));
            }

            while (!readiness.All(File.Exists))
            {
                foreach (var child in processes.Where(child => child.Process.HasExited))
                    Assert.Fail($"Audit writer exited before readiness: {await child.Error}");
                await Task.Delay(20, timeout.Token);
            }
            Assert.Equal(3, processes.Select(child => child.Process.Id).Distinct().Count());
            await File.WriteAllTextAsync(gate, "start", timeout.Token);
            await Task.WhenAll(processes.Select(child => child.Process.WaitForExitAsync(timeout.Token)));
            foreach (var child in processes)
                Assert.True(child.Process.ExitCode == 0, await child.Error);

            var chainPath = Path.Combine(root, "chain.log");
            var verified = await new AuditChainService().VerifyChainAsync(chainPath, timeout.Token);
            Assert.True(verified.IsValid);
            Assert.Equal(36, verified.EntriesChecked);
            var paths = new List<string>();
            foreach (var line in await File.ReadAllLinesAsync(chainPath, timeout.Token))
            {
                using var record = JsonDocument.Parse(line);
                paths.Add(record.RootElement.GetProperty("path").GetString()!);
            }
            Assert.Equal(36, paths.Distinct(StringComparer.Ordinal).Count());
            Assert.Equal(Directory.GetFiles(root, "*.jsonl").Order(), paths.Order());
        }
        finally
        {
            foreach (var child in processes)
            {
                if (!child.Process.HasExited)
                    child.Process.Kill(entireProcessTree: true);
                await child.Process.WaitForExitAsync();
                child.Process.Dispose();
            }
            Directory.Delete(root, recursive: true);
        }
    }
}
