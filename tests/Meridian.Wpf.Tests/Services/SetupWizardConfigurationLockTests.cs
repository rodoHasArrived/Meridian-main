using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Application.UI;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.Tests.Services;

/// <summary>
/// The setup wizard writes the same configuration file the browser host mutates through
/// <see cref="ConfigStore.UpdateAsync"/>; its save must hold the same cross-process lock (PRD-002).
/// </summary>
public sealed class SetupWizardConfigurationLockTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Meridian.Wpf.Tests", "SetupWizardLock", Guid.NewGuid().ToString("N"));
    private string ConfigPath => Path.Combine(_root, "appsettings.json");

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public async Task WizardSave_WaitsWhileTheHostHoldsTheConfigurationLock()
    {
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(ConfigPath, "{\"DataSource\":\"alpaca\",\"Symbols\":[\"SPY\"]}");
        var store = new ConfigStore(ConfigPath);

        Task save;
        using (await store.AcquireWriteLeaseAsync())
        {
            save = SetupWizardStateService.WriteConfigurationUnderLeaseAsync(ConfigPath, "polygon", _root, CancellationToken.None);
            var finishedWhileLocked = await Task.WhenAny(save, Task.Delay(TimeSpan.FromMilliseconds(400)));
            finishedWhileLocked.Should().NotBeSameAs(save, "the wizard must not write while the host holds the lock");
        }

        await save.WaitAsync(TimeSpan.FromSeconds(30));

        var root = JsonNode.Parse(await File.ReadAllTextAsync(ConfigPath))!.AsObject();
        root["DataSource"]!.GetValue<string>().Should().Be("polygon");
        root["Backfill"]!["DefaultProvider"]!.GetValue<string>().Should().Be("polygon");
        root["Symbols"]!.AsArray().Should().ContainSingle(node => node!.GetValue<string>() == "SPY", "unrelated sections survive the wizard save");
    }

    [Fact]
    public async Task WizardSave_LeavesNoPartialDocumentBehind()
    {
        Directory.CreateDirectory(_root);

        await SetupWizardStateService.WriteConfigurationUnderLeaseAsync(ConfigPath, "alpaca", _root, CancellationToken.None);

        Directory.GetFiles(_root, "*.tmp").Should().BeEmpty("the document is replaced atomically, never written in place");
        JsonNode.Parse(await File.ReadAllTextAsync(ConfigPath))!["DataRoot"]!.GetValue<string>().Should().Be(_root);
    }
}
