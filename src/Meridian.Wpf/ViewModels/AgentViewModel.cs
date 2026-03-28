using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Meridian.Wpf.Services;

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
    private static readonly SolidColorBrush OllamaOnlineBrush = CreateFrozenBrush(34, 197, 94);
    private static readonly SolidColorBrush OllamaOfflineBrush = CreateFrozenBrush(239, 68, 68);

    private readonly IAgentLoopService _agentLoopService;
    private readonly ILogger<AgentViewModel> _logger;
    private readonly RelayCommand _sendCommand;
    private readonly RelayCommand _clearCommand;

    private ObservableCollection<AgentMessageModel> _messages = [];
    private string _inputText = string.Empty;
    private string _selectedModel = string.Empty;
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
        set => SetProperty(ref _inputText, value);
    }

    public string SelectedModel
    {
        get => _selectedModel;
        set
        {
            if (SetProperty(ref _selectedModel, value))
            {
                _agentLoopService.SelectedModel = value;
            }
        }
    }

    public IReadOnlyList<string> AvailableModels => _agentLoopService.AvailableModels;

    public bool IsOllamaAvailable
    {
        get => _isOllamaAvailable;
        set => SetProperty(ref _isOllamaAvailable, value);
    }

    public bool IsSending
    {
        get => _isSending;
        set => SetProperty(ref _isSending, value);
    }

    public SolidColorBrush OllamaStatusBrush => IsOllamaAvailable ? OllamaOnlineBrush : OllamaOfflineBrush;

    public ICommand SendCommand => _sendCommand;
    public ICommand ClearCommand => _clearCommand;

    public AgentViewModel(IAgentLoopService agentLoopService, ILogger<AgentViewModel> logger)
    {
        _agentLoopService = agentLoopService ?? throw new ArgumentNullException(nameof(agentLoopService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _sendCommand = new RelayCommand(() => { _ = SendAsync(); }, () => CanSend);
        _clearCommand = new RelayCommand(() => Messages.Clear());

        _agentLoopService.MessageReceived += OnAgentMessageReceived;

        _ = InitializeAsync();
    }

    /// <summary>
    /// Initializes the view model by checking Ollama availability and listing models.
    /// </summary>
    private async Task InitializeAsync()
    {
        try
        {
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
                    RaisePropertyChanged(nameof(AvailableModels));
                }
            }

            _logger.LogInformation("AgentViewModel initialized. Ollama available: {Available}, Models: {Count}",
                isAvailable, _agentLoopService.AvailableModels.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing AgentViewModel");
            IsOllamaAvailable = false;
        }
    }

    /// <summary>
    /// Gets whether the send button should be enabled.
    /// </summary>
    private bool CanSend => !IsSending && IsOllamaAvailable && !string.IsNullOrWhiteSpace(InputText) && !string.IsNullOrWhiteSpace(SelectedModel);

    /// <summary>
    /// Sends the current input message to the agent.
    /// </summary>
    private async Task SendAsync()
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

            _logger.LogInformation("Sending message to agent: {MessageLength} chars", userMessage.Length);

            // Send to agent
            var response = await _agentLoopService.SendMessageAsync(userMessage).ConfigureAwait(false);

            // Assistant message is added via MessageReceived event
            _logger.LogInformation("Received response from agent: {ResponseLength} chars", response.Length);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Agent message send was cancelled");
            Messages.Add(new AgentMessageModel("assistant", "Request was cancelled.", DateTimeOffset.UtcNow));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending message to agent");
            Messages.Add(new AgentMessageModel("assistant", $"Error: {ex.Message}", DateTimeOffset.UtcNow));
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
    }

    private static SolidColorBrush CreateFrozenBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}

