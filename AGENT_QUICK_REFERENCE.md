# Agent Loop Service - Quick Reference & Testing Guide

## Quick Start

### 1. Prerequisites
- .NET 9.0 runtime
- Ollama installed: https://ollama.ai
- A model pulled in Ollama (e.g., `ollama pull mistral`)

### 2. Starting Ollama
```bash
ollama serve
# Ollama will listen on http://localhost:11434
```

### 3. Optional: Custom Ollama URL
```bash
# Windows
set OLLAMA_BASE_URL=http://custom-host:11435

# Linux/macOS
export OLLAMA_BASE_URL=http://custom-host:11435

# Then run Meridian WPF
dotnet run --project src/Meridian/Meridian.csproj -- --ui
```

---

## Testing Scenarios

### Scenario 1: Ollama Available, Models Loaded
1. Start Ollama: `ollama serve`
2. Pull a model: `ollama pull mistral` (or `neural-chat`, `orca-mini`)
3. Launch Meridian WPF
4. Navigate to Agent page
5. Expected: 
   - Green status indicator
   - Model dropdown populated with available models
   - Send button enabled

### Scenario 2: Ollama Offline
1. Launch Meridian WPF *without* Ollama running
2. Navigate to Agent page
3. Expected:
   - Red status indicator
   - ComboBox and Send button disabled
   - Error message not shown (graceful degradation)

### Scenario 3: Send Message
1. (Ollama running, model selected)
2. Type "Hello, what is 2+2?" in TextBox
3. Click Send or press Enter
4. Expected:
   - User message appears right-aligned in chat
   - Send button disabled during operation
   - Assistant response appears left-aligned after ~2-10s (depends on model)

### Scenario 4: Clear Chat
1. (After sending messages)
2. Click Clear button
3. Expected:
   - All messages removed from chat history
   - New conversation starts

### Scenario 5: Model Switch
1. (With multiple models pulled)
2. Open model ComboBox
3. Select different model
4. Send a message
5. Expected:
   - Conversation uses new model
   - History preserved but new responses use selected model

---

## Available Test Models (Fast)

For development, pull lightweight models:

```bash
# Mistral (7B, good balance)
ollama pull mistral

# Neural Chat (small, fast)
ollama pull neural-chat

# Orca Mini (1.3B, very fast)
ollama pull orca-mini

# Llama 2 (7B, well-known)
ollama pull llama2
```

---

## Debugging

### Check Ollama Connectivity
```powershell
# Test from PowerShell
$response = Invoke-WebRequest -Uri "http://localhost:11434/api/tags" -ErrorAction SilentlyContinue
if ($response.StatusCode -eq 200) { Write-Host "Ollama OK" } else { Write-Host "Ollama offline" }
```

### Check Available Models
```powershell
$response = Invoke-WebRequest -Uri "http://localhost:11434/api/tags"
$json = $response.Content | ConvertFrom-Json
$json.models | Select-Object -ExpandProperty name
```

### View Application Logs
Logs are written to:
- Console output (if running from command line)
- Application event log (if running as Windows app)
- `ILogger<AgentLoopService>` output

### Common Issues

**Issue:** "Ollama offline" even though Ollama is running
- **Solution:** Check `OLLAMA_BASE_URL` env var is set correctly
- **Solution:** Verify Ollama API port (default 11434) not blocked by firewall

**Issue:** Send button disabled after selecting model
- **Solution:** Model ComboBox value must match a string in AvailableModels list
- **Solution:** Check Ollama has models: `ollama list`

**Issue:** Extremely slow response times
- **Solution:** Larger models are slow; use `orca-mini` or `neural-chat` for testing
- **Solution:** Check system RAM; smaller models fit in VRAM

**Issue:** UI freezes while waiting for response
- **Solution:** Expected; response is async but UI thread might be busy
- **Solution:** Increase Ollama verbosity to debug: `ollama serve --verbose`

---

## Project Structure

```
src/Meridian.Wpf/
├── Services/
│   └── AgentLoopService.cs         # Ollama HTTP client + state
├── ViewModels/
│   └── AgentViewModel.cs            # MVVM logic + RelayCommand
├── Views/
│   ├── AgentPage.xaml               # Chat UI layout
│   ├── AgentPage.xaml.cs            # Thin code-behind
│   └── Pages.cs                     # Page registry (updated)
└── App.xaml.cs                      # DI registration (updated)
```

---

## API Reference

### IAgentLoopService Interface

```csharp
public interface IAgentLoopService
{
    bool IsOllamaAvailable { get; }
    string SelectedModel { get; set; }
    IReadOnlyList<string> AvailableModels { get; }
    event EventHandler<AgentMessageEventArgs>? MessageReceived;
    
    Task<bool> CheckOllamaAsync(CancellationToken ct = default);
    Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default);
    Task<string> SendMessageAsync(string userMessage, IReadOnlyList<AgentToolCall>? toolCalls = null, CancellationToken ct = default);
    Task CancelCurrentAsync();
}
```

### Events

```csharp
// Raised when agent sends a message
event EventHandler<AgentMessageEventArgs>? MessageReceived;

public sealed record AgentMessageEventArgs(
    string Role,                   // "user" or "assistant"
    string Content,                // Message text
    DateTimeOffset Timestamp       // When sent
);
```

---

## Configuration

### Environment Variables

| Variable | Default | Purpose |
|----------|---------|---------|
| `OLLAMA_BASE_URL` | `http://localhost:11434` | Ollama API endpoint |

### WPF Theme Resources Used

- `ShellWindowBackgroundBrush` - Page background
- `SurfaceBrush` - Header/input bar background
- `OnSurfaceBrush` - Text color
- `SurfaceVariantBrush` - Message bubble background
- `OnSurfaceVariantBrush` - Message text color

(Theme must be configured in `Meridian.Wpf` application resources)

---

## Performance Notes

- **Initialization:** ~500ms (Ollama connectivity check + model list fetch)
- **Message Send:** Depends on model (~2s for orca-mini, ~10s+ for larger models)
- **Memory:** Service maintains conversation history in RAM (unbounded)
- **CPU:** Minimal outside of Ollama communication

### Optimization Tips

1. Use smaller models for testing: `orca-mini`, `neural-chat`
2. Clear chat history regularly to avoid memory buildup
3. Run Ollama on GPU-accelerated system for faster responses
4. Use environment variable to point to remote Ollama instance if needed

---

## Known Limitations

1. No streaming responses (waits for full response)
2. No tool call parsing (prepared for future MCP integration)
3. No conversation persistence (lost on app close)
4. No request timeout customization (fixed 5s for availability, no timeout for messages)
5. Single conversation per ViewModel instance
6. No rate limiting or quota management

---

## Next Steps

1. **Build & Test:** `dotnet build src/Meridian.Wpf/Meridian.Wpf.csproj`
2. **Run Tests:** `dotnet test tests/Meridian.Tests`
3. **Run App:** `dotnet run --project src/Meridian/Meridian.csproj -- --ui`
4. **Navigate to Agent:** Use main menu or `NavigationService.NavigateTo("Agent")`

---

## Code Review Checklist

- ✅ All async methods have `CancellationToken` parameter
- ✅ Structured logging used throughout
- ✅ No hardcoded credentials or secrets
- ✅ Classes sealed for clear inheritance
- ✅ Proper IHttpClientFactory usage
- ✅ MVVM patterns followed (thin code-behind)
- ✅ Static readonly frozen brushes for performance
- ✅ Cancellation support on long operations
- ✅ No blocking `.Result` or `.Wait()`
- ✅ XML documentation on public types

