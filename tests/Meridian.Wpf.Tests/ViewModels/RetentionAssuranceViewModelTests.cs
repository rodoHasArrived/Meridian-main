#if WINDOWS
using System.Windows;
using FluentAssertions;
using Meridian.Ui.Services;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class RetentionAssuranceViewModelTests
{
    [Fact]
    public void SaveGuardrailsAsync_WithValidInputs_ShouldPersistGuardrails()
    {
        WpfTestThread.Run(async () =>
        {
            var client = new FakeRetentionAssuranceClient
            {
                Configuration =
                {
                    Guardrails = new RetentionGuardrails
                    {
                        MinTickDataDays = 14,
                        MinBarDataDays = 45,
                        MinQuoteDataDays = 10,
                        MaxDailyDeletedFiles = 500,
                        RequireChecksumVerification = false,
                        RequireDryRunPreview = true,
                        AllowDeleteDuringTradingHours = true
                    }
                }
            };
            var viewModel = new RetentionAssuranceViewModel(client);

            viewModel.LoadConfiguration(client.Configuration);

            viewModel.MinTickDataDaysText.Should().Be("14");
            viewModel.MinBarDataDaysText.Should().Be("45");
            viewModel.MinQuoteDataDaysText.Should().Be("10");
            viewModel.MaxDailyDeletesText.Should().Be("500");
            viewModel.RequireChecksumVerification.Should().BeFalse();
            viewModel.AllowDeleteDuringTradingHours.Should().BeTrue();

            viewModel.MaxDailyDeletesText = "2500";
            viewModel.RequireChecksumVerification = true;
            await viewModel.SaveGuardrailsAsync();

            client.SaveCalls.Should().Be(1);
            client.Configuration.Guardrails.MaxDailyDeletedFiles.Should().Be(2500);
            client.Configuration.Guardrails.RequireChecksumVerification.Should().BeTrue();
            viewModel.StatusText.Should().Be("Guardrails saved successfully.");
            viewModel.StatusVisibility.Should().Be(Visibility.Visible);
        });
    }

    [Fact]
    public void ValidatePolicy_WithInvalidInput_ShouldBlockServiceCallAndExplainRecovery()
    {
        WpfTestThread.Run(() =>
        {
            var client = new FakeRetentionAssuranceClient();
            var viewModel = new RetentionAssuranceViewModel(client)
            {
                TickDataDaysText = "abc"
            };

            viewModel.ValidatePolicyCommand.Execute(null);

            client.ValidateCalls.Should().Be(0);
            viewModel.ResultsHeader.Should().Be("Policy Input Required");
            viewModel.ResultsText.Should().Contain("tick data retention");
            viewModel.Results.Should().ContainSingle(item => item.Severity == "ERROR");
            viewModel.ResultsVisibility.Should().Be(Visibility.Visible);
            viewModel.CanExecuteCleanup.Should().BeFalse();
            viewModel.CleanupReadinessTitle.Should().Be("Fix policy inputs");
        });
    }

    [Fact]
    public void DryRunAsync_WithCandidates_ShouldExposeReadinessAndInlineConfirmation()
    {
        WpfTestThread.Run(async () =>
        {
            var dryRun = CreateDryRun();
            dryRun.SkippedFiles.Add(new SkippedFileInfo
            {
                Path = "data/SPY/held.jsonl",
                Symbol = "SPY",
                Reason = "Legal hold active",
                Size = 512
            });

            var client = new FakeRetentionAssuranceClient { DryRunResult = dryRun };
            var viewModel = new RetentionAssuranceViewModel(client);

            viewModel.RequestCleanupCommand.CanExecute(null).Should().BeFalse();

            await viewModel.DryRunAsync();

            client.DryRunCalls.Should().Be(1);
            client.LastDryRunPolicy.Should().NotBeNull();
            client.LastDryRunPolicy!.CompressBeforeDelete.Should().BeTrue();
            viewModel.ResultsHeader.Should().Be("Dry Run Results");
            viewModel.ResultsText.Should().Contain("Found 2 file(s) to delete");
            viewModel.Results.Should().ContainSingle(item => item.Severity == "WARN");
            viewModel.CleanupReadinessTitle.Should().Be("Cleanup preview ready");
            viewModel.CleanupReadinessScope.Should().Contain("2 file(s) staged");
            viewModel.CanExecuteCleanup.Should().BeTrue();
            viewModel.RequestCleanupCommand.CanExecute(null).Should().BeTrue();

            viewModel.RequestCleanupCommand.Execute(null);

            viewModel.IsCleanupConfirmationVisible.Should().BeTrue();
            viewModel.CleanupConfirmationDetail.Should().Contain("2 file(s)");
            viewModel.ConfirmCleanupCommand.CanExecute(null).Should().BeTrue();

            viewModel.CancelCleanupCommand.Execute(null);

            viewModel.IsCleanupConfirmationVisible.Should().BeFalse();
            viewModel.CleanupReadinessDetail.Should().Contain("cancelled");
        });
    }

    [Fact]
    public void ConfirmCleanupAsync_AfterDryRun_ShouldExecuteWithChecksumChoiceAndResetPreview()
    {
        WpfTestThread.Run(async () =>
        {
            var client = new FakeRetentionAssuranceClient
            {
                DryRunResult = CreateDryRun(),
                CleanupReport = new RetentionAuditReport
                {
                    Status = CleanupStatus.Success,
                    ActualBytesDeleted = 2048,
                    DeletedFiles =
                    [
                        new DeletedFileInfo { Path = "data/AAPL/old-trades.jsonl", Symbol = "AAPL", Size = 1024 },
                        new DeletedFileInfo { Path = "data/MSFT/old-quotes.jsonl", Symbol = "MSFT", Size = 1024 }
                    ]
                }
            };
            var cleanupCompletedCount = 0;
            var viewModel = new RetentionAssuranceViewModel(client)
            {
                RequireChecksumVerification = false
            };
            viewModel.CleanupCompleted += (_, _) => cleanupCompletedCount++;

            await viewModel.DryRunAsync();
            viewModel.RequestCleanupCommand.Execute(null);
            await viewModel.ConfirmCleanupAsync();

            client.ExecuteCalls.Should().Be(1);
            client.LastVerifyChecksums.Should().BeFalse();
            cleanupCompletedCount.Should().Be(1);
            viewModel.CanExecuteCleanup.Should().BeFalse();
            viewModel.IsCleanupConfirmationVisible.Should().BeFalse();
            viewModel.CleanupReadinessTitle.Should().Be("Cleanup audit recorded");
            viewModel.ResultsText.Should().Contain("Files deleted: 2");
        });
    }

    [Fact]
    public void RetentionAssurancePageSource_ShouldBindCleanupFlowThroughViewModel()
    {
        var xaml = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\RetentionAssurancePage.xaml"));
        var codeBehind = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\RetentionAssurancePage.xaml.cs"));

        xaml.Should().Contain("AutomationProperties.AutomationId=\"RetentionAssuranceCleanupReadinessCard\"");
        xaml.Should().Contain("Text=\"{Binding CleanupReadinessTitle}\"");
        xaml.Should().Contain("Text=\"{Binding CleanupReadinessDetail}\"");
        xaml.Should().Contain("Text=\"{Binding CleanupReadinessScope}\"");
        xaml.Should().Contain("Command=\"{Binding SaveGuardrailsCommand}\"");
        xaml.Should().Contain("Command=\"{Binding ValidatePolicyCommand}\"");
        xaml.Should().Contain("Command=\"{Binding DryRunCommand}\"");
        xaml.Should().Contain("Command=\"{Binding RequestCleanupCommand}\"");
        xaml.Should().Contain("Command=\"{Binding ConfirmCleanupCommand}\"");
        xaml.Should().Contain("Command=\"{Binding CancelCleanupCommand}\"");
        xaml.Should().Contain("ItemsSource=\"{Binding Results}\"");
        xaml.Should().Contain("Visibility=\"{Binding CleanupConfirmationVisibility}\"");
        xaml.Should().NotContain("Click=\"SaveGuardrails_Click\"");
        xaml.Should().NotContain("Click=\"ValidatePolicy_Click\"");
        xaml.Should().NotContain("Click=\"DryRun_Click\"");
        xaml.Should().NotContain("Click=\"ExecuteCleanup_Click\"");

        codeBehind.Should().Contain("new RetentionAssuranceViewModel");
        codeBehind.Should().Contain("DataContext = _viewModel");
        codeBehind.Should().Contain("CleanupCompleted");
        codeBehind.Should().NotContain("BuildPolicyFromUI");
        codeBehind.Should().NotContain("ExecuteCleanup_Click");
    }

    private static RetentionDryRunResult CreateDryRun()
    {
        var dryRun = new RetentionDryRunResult
        {
            ExecutedAt = DateTime.UtcNow,
            FilesToDelete =
            [
                new FileToDelete
                {
                    Path = "data/AAPL/old-trades.jsonl",
                    Symbol = "AAPL",
                    Size = 1024,
                    DataType = "Tick",
                    LastModified = DateTime.UtcNow.AddDays(-60)
                },
                new FileToDelete
                {
                    Path = "data/MSFT/old-quotes.jsonl",
                    Symbol = "MSFT",
                    Size = 1024,
                    DataType = "Quote",
                    LastModified = DateTime.UtcNow.AddDays(-60)
                }
            ],
            TotalBytesToDelete = 2048
        };
        dryRun.GroupBySymbol();
        return dryRun;
    }

    private sealed class FakeRetentionAssuranceClient : IRetentionAssuranceClient
    {
        public RetentionConfiguration Configuration { get; } = new();
        public RetentionDryRunResult DryRunResult { get; set; } = new();
        public RetentionAuditReport CleanupReport { get; set; } = new() { Status = CleanupStatus.Success };
        public int SaveCalls { get; private set; }
        public int ValidateCalls { get; private set; }
        public int DryRunCalls { get; private set; }
        public int ExecuteCalls { get; private set; }
        public RetentionPolicy? LastDryRunPolicy { get; private set; }
        public bool? LastVerifyChecksums { get; private set; }

        public Task SaveConfigurationAsync(CancellationToken ct = default)
        {
            SaveCalls++;
            return Task.CompletedTask;
        }

        public RetentionValidationResult ValidateRetentionPolicy(RetentionPolicy policy)
        {
            ValidateCalls++;
            return new RetentionValidationResult { IsValid = true };
        }

        public Task<RetentionDryRunResult> PerformDryRunAsync(RetentionPolicy policy, CancellationToken ct = default)
        {
            DryRunCalls++;
            LastDryRunPolicy = policy;
            return Task.FromResult(DryRunResult);
        }

        public Task<RetentionAuditReport> ExecuteRetentionCleanupAsync(
            RetentionDryRunResult dryRun,
            bool verifyChecksums,
            CancellationToken ct = default)
        {
            ExecuteCalls++;
            LastVerifyChecksums = verifyChecksums;
            return Task.FromResult(CleanupReport);
        }
    }
}
#endif
