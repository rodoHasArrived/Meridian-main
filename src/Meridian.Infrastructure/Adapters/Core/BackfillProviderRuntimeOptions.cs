using Meridian.Core.Config;

namespace Meridian.Infrastructure.Adapters.Core;

/// <summary>Validated construction-time limits shared by historical adapters.</summary>
public sealed record BackfillProviderRuntimeOptions(int Priority, int? Requests = null, TimeSpan? Window = null)
{
    public static BackfillProviderRuntimeOptions FromConfig(BackfillProvidersConfig? config, string providerId)
    {
        var edit = BackfillProviderCatalog.Read(config ?? new(), providerId);
        // Apply revalidates legacy or hand-edited config before it reaches a limiter.
        BackfillProviderCatalog.Apply(config ?? new(), providerId, edit);
        return new(edit.Priority, edit.RateLimitPerMinute ?? edit.RateLimitPerHour,
            edit.RateLimitPerMinute is not null ? TimeSpan.FromMinutes(1) :
            edit.RateLimitPerHour is not null ? TimeSpan.FromHours(1) : null);
    }
}
