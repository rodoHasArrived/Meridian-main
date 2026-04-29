using CommunityToolkit.Mvvm.Input;
using Meridian.Wpf.Services;
using Microsoft.Extensions.Logging;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// Represents a message in the agent conversation UI.
/// </summary>
public sealed record AgentMessageModel(string Role, string Content, DateTimeOffset Timestamp)
{
    /// <summary>
    /// Gets the horizontal alignment based on message role (right for user, left for assistant).
    /// </summary>
    public HorizontalAlignment Alignment => Role == "user" ? HorizontalAlignment.Right : HorizontalAlignment.Left;
}

/// <summary>
/// ViewModel for the agent loop page.
/// Manages conversation state, model selection, and message handling.
/// </summary>
public sealed class AgentViewModel : BindableBase
{
    private static readonly SolidColorBrush OllamaCheckingBrush = CreateFrozenBrush(234, 179, 8);
    private static readonly SolidColorBrush OllamaOnlineBrush = CreateFrozenBrush(34, 197, 94);
    private static readonly SolidColorBrush OllamaOfflineBrush = CreateFrozenBrush(239, 68, 68);

    private readonly IAgentLoopService _agentLoopService;
    private readonly ILogger<AgentViewModel> _logger;
    private readonly RelayCommand _sendCommand;
    private readonly RelayCommand _clearCommand;

    private ObservableCollection<AgentMessageModel> _messages = [];
    private string _inputText = string.Empty;
    private string _selectedModel = string.Empty;
    private bool _isInitializingAgent = true;
    private bool _isSending;
    private bool _isOllamaAvailable;

    public ObservableCollection<AgentMessageModel> Messages
    {
        get => _messages;
        set => SetProperty(ref _messages, value);
    }

    public string InputText
    {
        get => _inputText;
        set
        {
            var normalized = value ?? string.Empty;
            if (SetProperty(ref _inputText, normalized))
            {
                RefreshAgentPresentation();
            }
        }
    }

    public string SelectedModel
    {
        get => _selectedModel;
        set
        {
            var normalized = value ?? string.Empty;
            if (SetProperty(ref _selectedModel, normalized))
            {
                _agentLoopService.SelectedModel = normalized;
                RefreshAgentPresentation();
            }
        }
    }

    public IReadOnlyList<string> AvailableModels => _agentLoopService.AvailableModels;

    public bool IsOllamaAvailable
    {
        get => _isOllamaAvailable;
        set
        {
            if (SetProperty(ref _isOllamaAvailable, value))
            {
                RaisePropertyChanged(nameof(OllamaStatusBrush));
                RefreshAgentPresentation();
            }
        }
    }

    public bool IsInitializingAgent
    {
        get => _isInitializingAgent;
        private set
        {
            if (SetProperty(ref _isInitializingAgent, value))
            {
                RaisePropertyChanged(nameof(OllamaStatusBrush));
                RefreshAgentPresentation();
            }
        }
    }

    public bool IsSending
    {
        get => _isSending;
        set
        {
            if (SetProperty(ref _isSending, value))
            {
                RefreshAgentPresentation();
            }
        }
    }

    public bool IsAgentInputEnabled => !IsInitializingAgent && IsOllamaAvailable && AvailableModels.Count > 0;

    public bool HasMessages => Messages.Count > 0;

    public bool IsConversationEmpty => !HasMessages;

    public bool CanClearMessages => HasMessages;

    public string AgentReadinessTitle
    {
        get
        {
            if (IsInitializingAgent)
                return "Checking local agent service";
            if (IsSending)
                return "Agent is responding";
            if (!IsOllamaAvailable)
                return "Local agent unavailable";
            if (AvailableModels.Count == 0)
                return "No local models installed";
            if (string.IsNullOrWhiteSpace(SelectedModel))
                return "Choose a local model";

            return "Agent ready";
        }
    }

    public string AgentReadinessDetail
    {
        get
        {
            if (IsInitializingAgent)
                return "Checking Ollama availability and installed model metadata before enabling chat.";
            if (IsSending)
                return $"Waiting for {SelectedModel} to return a response; keep the prompt focused on one workflow.";
            if (!IsOllamaAvailable)
                return "Start Ollama locally, then reopen or refresh this page before using the workstation agent.";
            if (AvailableModels.Count == 0)
                return "Install or pull at least one Ollama model so Meridian can route prompts to a local runtime.";
            if (string.IsNullOrWhiteSpace(SelectedModel))
                return "Select one of the detected local models before sending a prompt.";

            return $"Using {SelectedModel} for local workstation assistance. Conversations stay in the current desktop session.";
        }
    }

    public string ModelScopeText
    {
        get
        {
            if (IsInitializingAgent)
                return "Checking installed models";
            if (!IsOllamaAvailable)
                return "Model catalog unavailable";

            var count = AvailableModels.Count;
            return count switch
            {
                0 => "No models available",
                1 => $"1 model available: {AvailableModels[0]}",
                _ => $"{count} models available"
            };
        }
    }

    public string InputStateText
    {
        get
        {
            if (IsInitializingAgent)
                return "Waiting for the local agent check.";
            if (!IsOllamaAvailable)
                return "Input is disabled until Ollama is available.";
            if (AvailableModels.Count == 0)
                return "Input is disabled until a local model is installed.";
            if (string.IsNullOrWhiteSpace(SelectedModel))
                return "Choose a model before sending a prompt.";
            if (IsSending)
                return "Generating response.";
            if (string.IsNullOrWhiteSpace(InputText))
                return "Type a prompt to start a local workstation conversation.";

            return $"Ready to send with {SelectedModel}.";
        }
    }

    public string EmptyConversationTitle => IsAgentInputEnabled
        ? "No conversation yet"
        : "Agent waiting on setup";

    public string EmptyConversationDetail => IsAgentInputEnabled
        ? "Ask for help with the current workstation workflow, a provider setup step, or a command you want to run."
        : AgentReadinessDetail;

    public SolidColorBrush OllamaStatusBrush => IsInitializingAgent || IsSending
        ? OllamaCheckingBrush
        : IsOllamaAvailable ? OllamaOnlineBrush : OllamaOfflineBrush;

    public ICommand SendCommand => _sendCommand;
    public ICommand ClearCommand => _clearCommand;

    public Task InitializationTask { get; }

    public AgentViewModel(IAgentLoopService agentLoopService, ILogger<AgentViewModel> logger)
    {
        _agentLoopService = agentLoopService ?? throw new ArgumentNullException(nameof(agentLoopService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _sendCommand = new RelayCommand(() => { _ = SendAsync(); }, () => CanSend);
        _clearCommand = new RelayCommand(ClearMessages, () => CanClearMessages);

        _agentLoopService.MessageReceived += OnAgentMessageReceived;

        InitializationTask = InitializeAsync();
    }

    /// <summary>
    /// Initializes the view model by checking Ollama availability and listing models.
    /// </summary>
    private async Task InitializeAsync()
    {
        try
        {
            IsInitializingAgent = true;
            _logger.LogInformation("Initializing AgentViewModel");

            // Check Ollama availability
            var isAvailable = await _agentLoopService.CheckOllamaAsync().ConfigureAwait(false);
            IsOllamaAvailable = isAvailable;

            if (isAvailable)
            {
                // List available models
                var models = await _agentLoopService.ListModelsAsync().ConfigureAwait(false);
                if (models.Count > 0)
                {
                    SelectedModel = models[0];
                }

                RaiseAvailableModelsChanged();
            }
            else
            {
                SelectedModel = string.Empty;
                RaiseAvailableModelsChanged();
            }

            _logger.LogInformation("AgentViewModel initialized. Ollama available: {Available}, Models: {Count}",
                isAvailable, _agentLoopService.AvailableModels.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing AgentViewModel");
            IsOllamaAvailable = false;
            SelectedModel = string.Empty;
            RaiseAvailableModelsChanged();
        }
        finally
        {
            IsInitializingAgent = false;
        }
    }

    /// <summary>
    /// Gets whether the send button should be enabled.
    /// </summary>
    public bool CanSend => !IsSending
        && IsAgentInputEnabled
        && !string.IsNullOrWhiteSpace(InputText)
        && !string.IsNullOrWhiteSpace(SelectedModel);

    /// <summary>
    /// Sends the current input message to the agent.
    /// </summary>
    public async Task SendAsync(CancellationToken ct = default)
    {
        if (!CanSend)
            return;

        try
        {
            IsSending = true;
            var userMessage = InputText.Trim();
            InputText = string.Empty;

            // Add user message to UI
            Messages.Add(new AgentMessageModel("user", userMessage, DateTimeOffset.UtcNow));
            RefreshConversationPresentation();

            _logger.LogInformation("Sending message to agent: {MessageLength} chars", userMessage.Length);

            // Send to agent
            var response = await _agentLoopService.SendMessageAsync(userMessage, ct: ct).ConfigureAwait(false);

            // Assistant message is added via MessageReceived event
            _logger.LogInformation("Received response from agent: {ResponseLength} chars", response.Length);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Agent message send was cancelled");
            Messages.Add(new AgentMessageModel("assistant", "Request was cancelled.", DateTimeOffset.UtcNow));
            RefreshConversationPresentation();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending message to agent");
            Messages.Add(new AgentMessageModel("assistant", $"Error: {ex.Message}", DateTimeOffset.UtcNow));
            RefreshConversationPresentation();
        }
        finally
        {
            IsSending = false;
        }
    }

    /// <summary>
    /// Handles messages received from the agent service.
    /// </summary>
    private void OnAgentMessageReceived(object? sender, AgentMessageEventArgs e)
    {
        Messages.Add(new AgentMessageModel(e.Role, e.Content, e.Timestamp));
        RefreshConversationPresentation();
    }

    private void ClearMessages()
    {
        Messages.Clear();
        RefreshConversationPresentation();
    }

    private void RaiseAvailableModelsChanged()
    {
        RaisePropertyChanged(nameof(AvailableModels));
        RefreshAgentPresentation();
    }

    private void RefreshConversationPresentation()
    {
        RaisePropertyChanged(nameof(HasMessages));
        RaisePropertyChanged(nameof(IsConversationEmpty));
        RaisePropertyChanged(nameof(CanClearMessages));
        RaisePropertyChanged(nameof(EmptyConversationTitle));
        RaisePropertyChanged(nameof(EmptyConversationDetail));
        _clearCommand.NotifyCanExecuteChanged();
    }

    private void RefreshAgentPresentation()
    {
        RaisePropertyChanged(nameof(CanSend));
        RaisePropertyChanged(nameof(IsAgentInputEnabled));
        RaisePropertyChanged(nameof(AgentReadinessTitle));
        RaisePropertyChanged(nameof(AgentReadinessDetail));
        RaisePropertyChanged(nameof(ModelScopeText));
        RaisePropertyChanged(nameof(InputStateText));
        RaisePropertyChanged(nameof(EmptyConversationTitle));
        RaisePropertyChanged(nameof(EmptyConversationDetail));
        _sendCommand.NotifyCanExecuteChanged();
    }

    private static SolidColorBrush CreateFrozenBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
