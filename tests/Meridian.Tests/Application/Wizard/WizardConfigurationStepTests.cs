using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Config;
using Meridian.Application.Services;
using Meridian.Application.UI;
using Meridian.Application.Wizard.Core;
using Meridian.Application.Wizard.Steps;

namespace Meridian.Tests.Application.Wizard;

public sealed class WizardConfigurationStepTests
{
    [Fact]
    public async Task ReviewConfigurationStep_ExecuteAsync_RendersJsonUsingSharedAppConfigOptions()
    {
        var output = new StringWriter();
        var input = new StringReader(Environment.NewLine);
        var step = new ReviewConfigurationStep(output, input);
        var context = new WizardContext
        {
            DataSource = new DataSourceSelection { DataSource = DataSourceKind.Alpaca }
        };

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        result.Status.Should().Be(WizardStepStatus.Success);
        context.FinalConfig.Should().NotBeNull();

        var expectedJson = JsonSerializer.Serialize(context.FinalConfig!, AppConfigJsonOptions.Write);
        output.ToString().Should().Contain(expectedJson);
    }

    [Fact]
    public async Task SaveConfigurationStep_ExecuteAsync_UsesResolvedConfigStorePath()
    {
        using var tempDir = new TempDirectory("wizard-save-path");
        var configPath = Path.Combine(tempDir.Path, "config", "appsettings.json");
        var output = new StringWriter();
        var input = new StringReader(string.Empty);
        var step = new SaveConfigurationStep(output, input, path => new ConfigStore(path ?? configPath));
        var context = new WizardContext
        {
            FinalConfig = new AppConfig { DataSource = DataSourceKind.Alpaca }
        };

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        result.Status.Should().Be(WizardStepStatus.Success);
        context.SavedConfigPath.Should().Be(configPath);
        File.Exists(configPath).Should().BeTrue();

        var expectedJson = JsonSerializer.Serialize(context.FinalConfig!, AppConfigJsonOptions.Write);
        var savedJson = await File.ReadAllTextAsync(configPath);
        savedJson.Should().Be(expectedJson);
    }

    [Fact]
    public async Task WriteConfigAsync_WhenCancelled_DoesNotWriteFile()
    {
        using var tempDir = new TempDirectory("wizard-save-cancel");
        var configPath = Path.Combine(tempDir.Path, "config", "appsettings.json");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        Func<Task> act = () => SaveConfigurationStep.WriteConfigAsync(
            new AppConfig { DataSource = DataSourceKind.Polygon },
            configPath,
            cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        File.Exists(configPath).Should().BeFalse();
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory(string prefix)
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"{prefix}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch
            {
            }
        }
    }
}
