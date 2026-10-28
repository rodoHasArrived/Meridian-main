using FluentAssertions;
using Meridian.Core.Config;
using Xunit;

namespace Meridian.Tests.Core.Config;

/// <summary>
/// Guards the config hot-reload disposal path used during operator-session shutdown.
/// </summary>
public sealed class ConfigWatcherTests
{
    [Fact]
    public async Task DisposeAsync_SessionShutdown_PreventsPendingDebounceCallback()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-config-watcher-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var configPath = Path.Combine(root, "appsettings.json");
        await File.WriteAllTextAsync(configPath, "{}");

        try
        {
            var callbackCount = 0;
            await using var watcher = new ConfigWatcher(configPath, TimeSpan.FromMilliseconds(200));
            watcher.ConfigChanged += _ => Interlocked.Increment(ref callbackCount);
            watcher.Start();

            await File.WriteAllTextAsync(configPath, """{"DataSource":"Synthetic"}""");
            await watcher.DisposeAsync();

            await Task.Delay(350);

            callbackCount.Should().Be(0);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
