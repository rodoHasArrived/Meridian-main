using System.IO;
using Meridian.Wpf.Tests.Support;

namespace Meridian.Wpf.Tests.Views;

public sealed class RunMatWorkflowSmokeTests
{
    [Fact]
    public void RunMatPage_SaveLoadWorkflow_ShouldPersistAndRestoreScriptContent()
    {
        WpfTestThread.Run(async () =>
        {
            using var facade = new RunMatUiAutomationFacade();
            await facade.InitializeAsync();

            facade.SetText(facade.ScriptNameTextBox, "smoke_save_load.m");
            facade.SetText(facade.ScriptSourceTextBox, "disp(123);");

            await facade.SaveAsync();
            await facade.RefreshAsync();

            facade.SavedScriptsListBox.Items.Count.Should().BeGreaterThan(0);
            var savedScript = facade.ViewModel.Scripts.Single(script => script.Name == "smoke_save_load.m");
            facade.ViewModel.SelectedScript = savedScript;
            RunMatUiAutomationFacade.DrainDispatcher();

            facade.SetText(facade.ScriptSourceTextBox, "disp(999);");
            await facade.LoadAsync();

            facade.ViewModel.ScriptSource.Should().Contain("disp(123);");
            facade.ViewModel.StatusText.Should().Be("Loaded smoke_save_load.m.");
            facade.AutomationStatusText.Text.Should().Be("Loaded smoke_save_load.m.");

            var savedPath = Path.Combine(facade.RunMatService.ScriptsDirectory, "smoke_save_load.m");
            File.ReadAllText(savedPath).Should().Contain("disp(123);");
        });
    }

    [Fact]
    public void RunMatPage_RunWorkflow_ShouldInvokeConfiguredExecutableAndCaptureOutput()
    {
        WpfTestThread.Run(async () =>
        {
            using var facade = new RunMatUiAutomationFacade();
            await facade.InitializeAsync();

            // Use a built-in Windows executable so the smoke test stays deterministic
            // and doesn't depend on a machine-local RunMat installation.
            var executablePath = Path.Combine(Environment.SystemDirectory, "more.com");
            File.Exists(executablePath).Should().BeTrue("the RunMat workflow smoke test relies on more.com being available on Windows");

            var workingDirectory = Path.Combine(facade.RootDirectory, "workspace");
            Directory.CreateDirectory(workingDirectory);

            facade.SetText(facade.ExecutablePathTextBox, executablePath);
            facade.SetText(facade.WorkingDirectoryTextBox, workingDirectory);
            facade.SetText(facade.ScriptNameTextBox, "smoke_run.m");
            facade.SetText(facade.ScriptSourceTextBox, "disp(12);");

            await facade.RunAsync();

            facade.ViewModel.StatusText.Should().Be("Run completed successfully.");
            facade.ViewModel.LastRunSummary.Should().Contain("Exit code 0");
            facade.OutputLinesListBox.Items.Count.Should().BeGreaterThan(0);
            facade.ViewModel.OutputLines.Should().Contain(line => line.Text.Contains("12", StringComparison.Ordinal));
            facade.AutomationStatusText.Text.Should().Be("Run completed successfully.");
            facade.AutomationLastRunText.Text.Should().Contain("Exit code 0");
            facade.AutomationResolvedExecutableText.Text.Should().Be(executablePath);

            var savedScript = Path.Combine(facade.RunMatService.ScriptsDirectory, "smoke_run.m");
            File.Exists(savedScript).Should().BeTrue();
        });
    }
}
