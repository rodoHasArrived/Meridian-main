using Meridian.Contracts.Tenancy;
using Meridian.Application.UI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using System.Text.Json;

namespace Meridian.Application.Composition;

/// <summary>Registers the read posture and retained tenant authority for every host graph.</summary>
public static class TenantScopeServiceRegistration
{
    public static IServiceCollection AddFundScopeTenantServices(this IServiceCollection services)
    {
        if (!services.Any(descriptor => descriptor.ServiceType == typeof(TenantScopeEnforcementOptions)))
        {
            var environmentValue = CompositionConfiguration.Resolve(services)[TenantScopeEnforcementOptions.EnvironmentVariable];
            // Refuse a misspelled explicit override immediately. Resolve file configuration from
            // the final host graph, where ConfigStore/IConfiguration may be registered later.
            var environmentOptions = string.IsNullOrWhiteSpace(environmentValue) ? null :
                TenantScopeEnforcementOptions.FromEnvironmentValue(environmentValue, TenantScopeEnforcementOptions.FailClosed);
            services.AddSingleton(sp => environmentOptions ?? ResolveConfiguration(sp));
        }

        services.TryAddSingleton<IFundScopeTenantAccessor, RetainedAuthorityFundScopeTenantAccessor>();
        services.TryAddSingleton<ITenantCutoverReadinessCheck, PostgresTenantCutoverReadinessCheck>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, TenantCutoverGuardService>());
        return services;
    }

    private static TenantScopeEnforcementOptions ResolveConfiguration(IServiceProvider services)
    {
        var hostConfiguration = services.GetService<CompositionConfiguration>();
        var configured = hostConfiguration is { UsesEnvironment: false }
            ? hostConfiguration[TenantScopeEnforcementOptions.ConfigurationKey]
            : services.GetService<IConfiguration>()?[TenantScopeEnforcementOptions.ConfigurationKey];
        var path = services.GetService<ConfigStore>()?.ConfigPath;
        if (configured is null && path is not null && File.Exists(path))
        {
            // ConfigStore.Load intentionally falls back on malformed files. Security posture must
            // not use that recovery path: an unreadable explicit setting refuses startup.
            using var document = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Name.Equals(TenantScopeEnforcementOptions.ConfigurationKey, StringComparison.OrdinalIgnoreCase))
                {
                    if (property.Value.ValueKind == JsonValueKind.Null)
                        continue;
                    if (property.Value.ValueKind != JsonValueKind.String)
                        throw new ArgumentException("TenantScopeEnforcement must be 'fail-closed' or 'deployment-boundary'.");
                    configured = property.Value.GetString();
                }
            }
        }

        return TenantScopeEnforcementOptions.FromConfigurationValue(configured);
    }

    /// <summary>Replaces the worker fallback with the host adapter, preserving explicit registrations.</summary>
    public static IServiceCollection AddFundScopeTenantServices<TAccessor>(this IServiceCollection services)
        where TAccessor : class, IFundScopeTenantAccessor
    {
        services.AddFundScopeTenantServices();
        for (var index = services.Count - 1; index >= 0; index--)
        {
            var descriptor = services[index];
            if (descriptor.ServiceType == typeof(IFundScopeTenantAccessor)
                && descriptor.ImplementationType == typeof(RetainedAuthorityFundScopeTenantAccessor))
            {
                services.RemoveAt(index);
            }
        }

        services.TryAddSingleton<IFundScopeTenantAccessor, TAccessor>();
        return services;
    }
}

/// <summary>Resolves only explicitly established worker authority, without an HTTP dependency.</summary>
public sealed class RetainedAuthorityFundScopeTenantAccessor : IFundScopeTenantAccessor
{
    public string? ResolveCallerTenant() => FundScopeTenantAuthority.CurrentTenantId;
}
