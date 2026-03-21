namespace Meridian.Application.Subscriptions.Models;

/// <summary>
/// Predefined template for bulk symbol subscriptions.
/// Supports equity groups, sectors, and indices.
/// </summary>
public sealed record SymbolTemplate(
    string Id,

    string Name,

    string Description,

    TemplateCategory Category,

    string[] Symbols,

    TemplateSubscriptionDefaults Defaults
);

/// <summary>
/// Default subscription settings applied when using a template.
/// </summary>
public sealed record TemplateSubscriptionDefaults(
    bool SubscribeTrades = true,
    bool SubscribeDepth = true,
    int DepthLevels = 10,
    string SecurityType = "STK",
    string Exchange = "SMART",
    string Currency = "USD"
);

/// <summary>
/// Categories for organizing subscription templates.
/// </summary>
public enum TemplateCategory : byte
{
    Sector,

    Index,

    MarketCap,

    Custom
}

/// <summary>
/// Request to apply a template to the current subscriptions.
/// </summary>
public sealed record ApplyTemplateRequest(
    string TemplateId,
    bool ReplaceExisting = false,
    TemplateSubscriptionDefaults? OverrideDefaults = null
);
