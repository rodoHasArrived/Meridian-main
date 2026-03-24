using System.Text.Json.Serialization;

namespace Meridian.Wpf.Models;

// =============================================================================
// WPF-Specific Models
// =============================================================================
// Most data models are now provided by Meridian.Contracts via shared
// source files (see SharedModelAliases.cs for type mappings).
//
// This file only contains WPF-specific types that don't exist in Contracts.
// =============================================================================

/// <summary>
/// Keyboard shortcut configuration for the WPF application.
/// </summary>
public sealed class KeyboardShortcut
{
    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;

    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    [JsonPropertyName("modifiers")]
    public string[]? Modifiers { get; set; } // Ctrl, Shift, Alt

    [JsonPropertyName("isEnabled")]
    public bool IsEnabled { get; set; } = true;

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}
