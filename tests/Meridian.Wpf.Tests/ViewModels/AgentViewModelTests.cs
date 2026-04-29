using Meridian.Wpf.Services;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class AgentViewModelTests
{
    [Fact]
    public async Task Initialization_WhenOllamaUnavailable_ProjectsRecoveryGuidance()
    {
        var service = new FakeAgentLoopService { CheckResult = false };
        var viewModel = CreateViewModel(service);

        await viewModel.InitializationTask.WaitAsync(TimeSpan.FromSeconds(5));

        viewModel.IsOllamaAvailable.Should().BeFalse();
        viewModel.IsAgentInputEnabled.Should().BeFalse();
        viewModel.AgentReadinessTitle.Should().Be("Local agent unavailable");
        viewModel.AgentReadinessDetail.Should().Contain("Start Ollama");
        viewModel.ModelScopeText.Should().Be("Model catalog unavailable");
        viewModel.SendCommand.CanExecute(null).Should().BeFalse();
        viewModel.ClearCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task Initialization_WithModels_SelectsFirstModelAndEnablesPromptWhenInputExists()
    {
        var service = new FakeAgentLoopService("llama3", "codellama");
        var viewModel = CreateViewModel(service);

        await viewModel.InitializationTask.WaitAsync(TimeSpan.FromSeconds(5));

        viewModel.SelectedModel.Should().Be("llama3");
        viewModel.AgentReadinessTitle.Should().Be("Agent ready");
        viewModel.ModelScopeText.Should().Be("2 models available");
        viewModel.IsAgentInputEnabled.Should().BeTrue();
        viewModel.SendCommand.CanExecute(null).Should().BeFalse();

        viewModel.InputText = "summarize provider setup";

        viewModel.CanSend.Should().BeTrue();
        viewModel.SendCommand.CanExecute(null).Should().BeTrue();
        viewModel.InputStateText.Should().Be("Ready to send with llama3.");
    }

    [Fact]
    public async Task SendAsync_AddsConversationMessagesAndClearCommandRecoversEmptyState()
    {
        var service = new FakeAgentLoopService("llama3") { ResponseText = "Provider setup summarized." };
        var viewModel = CreateViewModel(service);
        await viewModel.InitializationTask.WaitAsync(TimeSpan.FromSeconds(5));
        viewModel.InputText = "summarize provider setup";

        await viewModel.SendAsync();

        service.LastUserMessage.Should().Be("summarize provider setup");
        viewModel.Messages.Should().HaveCount(2);
        viewModel.Messages[0].Role.Should().Be("user");
        viewModel.Messages[1].Role.Should().Be("assistant");
        viewModel.HasMessages.Should().BeTrue();
        viewModel.IsConversationEmpty.Should().BeFalse();
        viewModel.ClearCommand.CanExecute(null).Should().BeTrue();

        viewModel.ClearCommand.Execute(null);

        viewModel.Messages.Should().BeEmpty();
        viewModel.IsConversationEmpty.Should().BeTrue();
        viewModel.EmptyConversationTitle.Should().Be("No conversation yet");
        viewModel.ClearCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task SendAsync_WhenAgentFails_AddsVisibleErrorAndRestoresSendState()
    {
        var service = new FakeAgentLoopService("llama3")
        {
            SendException = new InvalidOperationException("model process exited")
        };
        var viewModel = CreateViewModel(service);
        await viewModel.InitializationTask.WaitAsync(TimeSpan.FromSeconds(5));
        viewModel.InputText = "check this";

        await viewModel.SendAsync();

        viewModel.IsSending.Should().BeFalse();
        viewModel.Messages.Should().Contain(message => message.Content.Contains("model process exited"));
        viewModel.InputStateText.Should().Be("Type a prompt to start a local workstation conversation.");
    }

    [Fact]
    public void AgentPageSource_BindsReadinessAndEmptyConversationStateThroughViewModel()
    {
        var xaml = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\AgentPage.xaml"));
        var codeBehind = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\AgentPage.xaml.cs"));

        xaml.Should().Contain("AgentReadinessCard");
        xaml.Should().Contain("{Binding AgentReadinessTitle}");
        xaml.Should().Contain("{Binding AgentReadinessDetail}");
        xaml.Should().Contain("{Binding ModelScopeText}");
        xaml.Should().Contain("{Binding InputStateText}");
        xaml.Should().Contain("AgentConversationEmptyState");
        xaml.Should().Contain("{Binding EmptyConversationTitle}");
        xaml.Should().Contain("{Binding EmptyConversationDetail}");
        xaml.Should().Contain("IsEnabled=\"{Binding IsAgentInputEnabled}\"");
        xaml.Should().Contain("Command=\"{Binding ClearCommand}\"");
        xaml.Should().Contain("AutomationProperties.AutomationId=\"AgentSendButton\"");

        codeBehind.Should().NotContain("Click");
        codeBehind.Should().Contain("DataContext = viewModel");
    }

    private static AgentViewModel CreateViewModel(FakeAgentLoopService service) =>
        new(service, NullLogger<AgentViewModel>.Instance);

    private sealed class FakeAgentLoopService : IAgentLoopService
    {
        private readonly List<string> _availableModels;

        public FakeAgentLoopService(params string[] availableModels)
        {
            _availableModels = availableModels.ToList();
        }

        public bool CheckResult { get; init; } = true;

        public string ResponseText { get; init; } = "Assistant response.";

        public Exception? SendException { get; init; }

        public string? LastUserMessage { get; private set; }

        public bool IsOllamaAvailable => CheckResult;

        public string SelectedModel { get; set; } = string.Empty;

        public IReadOnlyList<string> AvailableModels => _availableModels;

        public event EventHandler<AgentMessageEventArgs>? MessageReceived;

        public Task<bool> CheckOllamaAsync(CancellationToken ct = default) =>
            Task.FromResult(CheckResult);

        public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>>(_availableModels);

        public Task<string> SendMessageAsync(
            string userMessage,
            IReadOnlyList<AgentToolCall>? toolCalls = null,
            CancellationToken ct = default)
        {
            LastUserMessage = userMessage;

            if (SendException is not null)
            {
                throw SendException;
            }

            MessageReceived?.Invoke(this, new AgentMessageEventArgs("assistant", ResponseText, DateTimeOffset.UtcNow));
            return Task.FromResult(ResponseText);
        }

        public Task CancelCurrentAsync() => Task.CompletedTask;
    }
}
