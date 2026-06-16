namespace Meridian.Modularity;

/// <summary>
/// A capability a module advertises. Generalizes the per-pattern capability descriptors
/// (provider capabilities, desktop feature-capability descriptors) into one shape for
/// diagnostics and capability gating.
/// </summary>
/// <param name="Key">Stable capability key (case-insensitive by convention).</param>
/// <param name="DisplayName">Human-readable capability name.</param>
/// <param name="Description">Optional longer description.</param>
public sealed record ModuleCapability(string Key, string DisplayName, string? Description = null);
