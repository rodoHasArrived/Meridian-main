namespace Meridian.Ui.Services;

/// <summary>
/// Generic operation result with success/failure status and messages.
/// </summary>
public sealed class OperationResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? Error { get; set; }
}
