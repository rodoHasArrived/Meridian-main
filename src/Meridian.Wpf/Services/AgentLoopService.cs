using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Meridian.Wpf.Services;

/// <summary>
/// Event arguments for agent messages received from Ollama.
/// </summary>
public sealed record AgentMessageEventArgs(string Role, string Content, DateTimeOffset Timestamp);

/// <summary>
/// Represents a tool call parsed from agent response.
/// </summary>
public sealed record AgentToolCall(string ToolName, IReadOnlyDictionary<string, string> Parameters);

/// <summary>
/// Interface for the local AI agent loop service.
/// Connects to a local Ollama instance for conversational AI with MCP tool integration.
/// </summary>
public interface IAgentLoopService
{
    /// <summary>
    /// Gets whether the Ollama service is currently available.
    /// </summary>
    bool IsOllamaAvailable { get; }

    /// <summary>
    /// Gets or sets the currently selected Ollama model.
    /// </summary>
    string SelectedModel { get; set; }

    /// <summary>
    /// Gets the list of available models from Ollama.
    /// </summary>
    IReadOnlyList<string> AvailableModels { get; }

    /// <summary>
    /// Raised when a message is received from the agent.
    /// </summary>
    event EventHandler<AgentMessageEventArgs>? MessageReceived;

    /// <summary>
    /// Checks if Ollama is available and accessible.
    /// </summary>
    Task<bool> CheckOllamaAsync(CancellationToken ct = default);

    /// <summary>
    /// Retrieves the list of available models from Ollama.
    /// </summary>
    Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default);

    /// <summary>
    /// Sends a message to the agent and retrieves a response.
    /// </summary>
    Task<string> SendMessageAsync(string userMessage, IReadOnlyList<AgentToolCall>? toolCalls = null, CancellationToken ct = default);

    /// <summary>
    /// Cancels the current agent operation.
    /// </summary>
    Task CancelCurrentAsync();
}

/// <summary>
/// Local AI agent loop service implementation.
/// Communicates with Ollama for conversational AI without data egress.
/// </summary>
public sealed class AgentLoopService : IAgentLoopService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AgentLoopService> _logger;
    private readonly string _ollamaBaseUrl;
    private readonly List<OllamaMessage> _conversationHistory = [];
    private CancellationTokenSource? _currentCts;
    private bool _isOllamaAvailable;
    private string _selectedModel = string.Empty;
    private readonly ObservableCollection<string> _availableModels = [];

    public bool IsOllamaAvailable
    {
        get => _isOllamaAvailable;
        private set => _isOllamaAvailable = value;
    }

    public string SelectedModel
    {
        get => _selectedModel;
        set => _selectedModel = value;
    }

    public IReadOnlyList<string> AvailableModels => _availableModels.AsReadOnly();

    public event EventHandler<AgentMessageEventArgs>? MessageReceived;

    public AgentLoopService(IHttpClientFactory httpClientFactory, ILogger<AgentLoopService> logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _ollamaBaseUrl = Environment.GetEnvironmentVariable("OLLAMA_BASE_URL") ?? "http://localhost:11434";
        _logger.LogInformation("AgentLoopService initialized with Ollama base URL: {BaseUrl}", _ollamaBaseUrl);
    }

    /// <summary>
    /// Checks if Ollama is available by attempting to reach the /api/tags endpoint.
    /// </summary>
    public async Task<bool> CheckOllamaAsync(CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(5));

            using var client = _httpClientFactory.CreateClient();
            var response = await client.GetAsync($"{_ollamaBaseUrl}/api/tags", cts.Token).ConfigureAwait(false);

            IsOllamaAvailable = response.IsSuccessStatusCode;
            _logger.LogInformation("Ollama availability check: {IsAvailable}", IsOllamaAvailable);
            return IsOllamaAvailable;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Ollama availability check timed out");
            IsOllamaAvailable = false;
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error checking Ollama availability");
            IsOllamaAvailable = false;
            return false;
        }
    }

    /// <summary>
    /// Retrieves the list of available models from Ollama.
    /// </summary>
    public async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default)
    {
        try
        {
            _availableModels.Clear();

            using var client = _httpClientFactory.CreateClient();
            var response = await client.GetAsync($"{_ollamaBaseUrl}/api/tags", ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to list models from Ollama: {StatusCode}", response.StatusCode);
                return _availableModels.AsReadOnly();
            }

            var content = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(content);

            if (doc.RootElement.TryGetProperty("models", out var modelsArray))
            {
                foreach (var model in modelsArray.EnumerateArray())
                {
                    if (model.TryGetProperty("name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String)
                    {
                        var modelName = nameElement.GetString();
                        if (!string.IsNullOrWhiteSpace(modelName))
                        {
                            _availableModels.Add(modelName);
                        }
                    }
                }
            }

            _logger.LogInformation("Listed {Count} models from Ollama", _availableModels.Count);
            return _availableModels.AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing models from Ollama");
            return _availableModels.AsReadOnly();
        }
    }

    /// <summary>
    /// Sends a message to the agent and retrieves a response.
    /// </summary>
    public async Task<string> SendMessageAsync(string userMessage, IReadOnlyList<AgentToolCall>? toolCalls = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
            throw new ArgumentException("User message cannot be empty", nameof(userMessage));

        if (string.IsNullOrWhiteSpace(SelectedModel))
            throw new InvalidOperationException("No model selected");

        try
        {
            _currentCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var linkedCt = _currentCts.Token;

            // Add user message to conversation history
            _conversationHistory.Add(new OllamaMessage { Role = "user", Content = userMessage });

            // Build request
            var messages = _conversationHistory.Select(m => new { m.Role, m.Content }).ToList();

            var request = new
            {
                Model = SelectedModel,
                Messages = messages,
                Stream = false
            };

            var jsonContent = new StringContent(
                JsonSerializer.Serialize(request),
                Encoding.UTF8,
                "application/json"
            );

            using var client = _httpClientFactory.CreateClient();
            var response = await client.PostAsync($"{_ollamaBaseUrl}/api/chat", jsonContent, linkedCt).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var errorMessage = $"Ollama API error: {response.StatusCode}";
                _logger.LogError(errorMessage);
                throw new HttpRequestException(errorMessage);
            }

            var responseContent = await response.Content.ReadAsStringAsync(linkedCt).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(responseContent);

            var assistantMessage = string.Empty;
            if (doc.RootElement.TryGetProperty("message", out var messageElement) &&
                messageElement.TryGetProperty("content", out var contentElement) &&
                contentElement.ValueKind == JsonValueKind.String)
            {
                assistantMessage = contentElement.GetString() ?? string.Empty;
            }

            // Add assistant response to conversation history
            if (!string.IsNullOrWhiteSpace(assistantMessage))
            {
                _conversationHistory.Add(new OllamaMessage { Role = "assistant", Content = assistantMessage });

                // Raise event for UI update
                OnMessageReceived(new AgentMessageEventArgs("assistant", assistantMessage, DateTimeOffset.UtcNow));
            }

            _logger.LogInformation("Received response from Ollama model {Model}", SelectedModel);
            return assistantMessage;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Agent message send was cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending message to Ollama");
            throw;
        }
        finally
        {
            _currentCts?.Dispose();
            _currentCts = null;
        }
    }

    /// <summary>
    /// Cancels the current agent operation.
    /// </summary>
    public async Task CancelCurrentAsync()
    {
        _currentCts?.Cancel();
        await Task.CompletedTask.ConfigureAwait(false);
    }

    private void OnMessageReceived(AgentMessageEventArgs args)
    {
        MessageReceived?.Invoke(this, args);
    }

    /// <summary>
    /// Internal DTO for Ollama messages.
    /// </summary>
    private sealed class OllamaMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = string.Empty;

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;
    }
}
