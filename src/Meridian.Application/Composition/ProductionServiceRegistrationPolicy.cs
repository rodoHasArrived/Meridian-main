using System.Reflection;
using System.Runtime.InteropServices;
using Meridian.Contracts.Operations;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Application.Composition;

/// <summary>
/// Marks an implementation that the production composition policy must reject.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class NonProductionOnlyImplementationAttribute : Attribute;

/// <summary>
/// Explicit, auditable opt-out for implementations whose type name matches a prohibited
/// production prefix (<c>InMemory*</c>, <c>Null*</c>, <c>NoOp*</c>, <c>Fake*</c>, <c>Stub*</c>,
/// <c>Sample*</c>) but are genuinely safe for a production role (ADR-019). Matched by attribute
/// type name so lower layers can declare an identically named attribute without an upward
/// project reference. Marker-based prohibitions (<see cref="INonProductionOnlyService"/> and
/// <see cref="NonProductionOnlyImplementationAttribute"/>) cannot be overridden.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ProductionSafeImplementationAttribute(string justification) : Attribute
{
    public string Justification { get; } = justification;
}

/// <summary>
/// Marks services whose implementations are prohibited in production compositions.
/// </summary>
public interface INonProductionOnlyService;

/// <summary>
/// The single production composition policy (ADR-019). A composition is production when the host
/// declared <see cref="MeridianDeploymentPosture.ProductionApi"/> on the collection or the
/// environment resolves to production; both paths share one prohibited-implementation matcher.
/// <see cref="ProductionRegistrationGuardService"/> re-runs this policy over the final graph at
/// host start so registrations added after the composition root cannot bypass it.
/// </summary>
public static class ProductionServiceRegistrationPolicy
{
    private static readonly string[] ProhibitedNamePrefixes =
        ["InMemory", "Null", "NoOp", "Fake", "Stub", "Sample"];

    public static void Validate(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (IsProductionComposition(services))
        {
            ThrowIfViolations(CollectStaticViolations(services), CollectPlatformViolation(services));
            return;
        }

        if (IsPlatformGatedLocalComposition(services))
        {
            ThrowIfViolations([], CollectPlatformViolation(services));
        }
    }

    internal static string[] CollectStaticViolations(IServiceCollection services)
        => services
            .Select(GetRegisteredImplementationType)
            .Where(type => type is not null)
            .Distinct()
            .Where(type => IsNonProductionOnlyImplementation(type!))
            .Select(type => type!.FullName ?? type!.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

    internal static void ThrowIfViolations(IReadOnlyCollection<string> violations)
        => ThrowIfViolations(violations, platformViolation: null);

    /// <summary>
    /// Refuses startup when either prohibited bindings or an unsupported host platform were found.
    /// Both findings are reported in one exception, binding text first, so an operator reading the
    /// refusal sees every reason the production graph was rejected rather than the first one only.
    /// </summary>
    internal static void ThrowIfViolations(IReadOnlyCollection<string> violations, string? platformViolation)
    {
        if (violations.Count == 0 && platformViolation is null)
        {
            return;
        }

        if (violations.Count == 0)
        {
            throw new StartupRefusedException(platformViolation!);
        }

        var message =
            "Production startup policy rejected non-production DI registrations: " +
            $"{string.Join(", ", violations)}. Replace each binding with a production implementation, " +
            "or scope the capability out of the supported envelope per ADR-019.";
        throw new StartupRefusedException(platformViolation is null ? message : message + " " + platformViolation);
    }

    // ── ADR-019 host platform fail-closed ───────────────────────────────────────────────────────
    //
    // The signed v1 production envelope is Windows 11 (.NET 10, installed via the desktop-installer
    // lane). Linux container and systemd postures are experimental and must be rejected, not
    // silently degraded, when a production posture selects them. The degradation this prevents is
    // concrete: off Windows the provider credential vault cannot use DPAPI and falls back to a raw
    // AES key stored beside the vault, and the lifecycle protected secret store is unsupported.
    // The gate applies to production compositions and to the shipped topology itself: the packaged
    // or customer local-workstation build that the lifecycle supervisor starts with
    // MDC_PACKAGED_BUILD=true. An unflagged LocalWorkstation composition is development and is not
    // gated, so work on Linux keeps composing. Architecture and Windows version are reported for the
    // diagnostic but not refused: PRD-014 certifies both x64 and ARM64 installer receipts.

    /// <summary>Production bindings that silently select a weaker implementation off Windows.</summary>
    private static readonly string[] PlatformBoundProductionBindings =
    [
        "provider credential vault protection 'dpapi-current-user' -> 'local-aes-gcm' (FileProviderCredentialStore)",
        "lifecycle protected secret store (LifecycleProtectedSecretStore.IsSupported)"
    ];

    internal readonly record struct HostPlatform(bool IsWindows, Architecture Architecture, string Description);

    /// <summary>
    /// The host platform the production policy evaluates: a <see cref="MeridianHostPlatformDeclaration"/>
    /// when one was declared (tests), otherwise the running operating system.
    /// </summary>
    internal static HostPlatform ResolveHostPlatform(IServiceCollection services)
    {
        for (var i = services.Count - 1; i >= 0; i--)
        {
            if (!services[i].IsKeyedService &&
                services[i].ImplementationInstance is MeridianHostPlatformDeclaration declaration)
            {
                return new HostPlatform(declaration.IsWindows, declaration.Architecture, declaration.Description);
            }
        }

        return new HostPlatform(
            OperatingSystem.IsWindows(),
            RuntimeInformation.OSArchitecture,
            RuntimeInformation.OSDescription);
    }

    /// <summary>
    /// Whether a supported local-workstation composition is the packaged or customer build, which is
    /// the shipped topology and therefore inside the ADR-019 platform gate. The same flags that
    /// <see cref="EnsureIsolatedQuantLabIsAllowed"/> treats as customer distribution.
    /// </summary>
    internal static bool IsPlatformGatedLocalComposition(IServiceCollection services)
        => IsSupportedLocalComposition(services) && ResolvePackagedBuildFlag(services) is not null;

    private static (string Name, string Value)? ResolvePackagedBuildFlag(IServiceCollection services)
    {
        var configuration = CompositionConfiguration.Resolve(services);
        foreach (var name in new[] { "MDC_PACKAGED_BUILD", "MERIDIAN_CUSTOMER_BUILD" })
        {
            var value = configuration[name];
            if (IsTruthy(value))
                return (name, value!);
        }

        return null;
    }

    /// <summary>
    /// The ADR-019 platform refusal, or <c>null</c> when the host is inside the supported envelope.
    /// Callers gate on <see cref="IsProductionComposition"/> or
    /// <see cref="IsPlatformGatedLocalComposition"/> first; an unflagged local-workstation or
    /// unspecified posture is never platform-gated so development on Linux keeps composing.
    /// </summary>
    internal static string? CollectPlatformViolation(IServiceCollection services)
    {
        var platform = ResolveHostPlatform(services);
        if (platform.IsWindows)
        {
            return null;
        }

        return
            $"Production startup policy rejected the host platform for posture '{DescribeGatedPosture(services)}': " +
            $"detected {platform.Description} ({platform.Architecture}), but ADR-019 " +
            "(docs/adr/019-production-support-matrix-and-deployment-posture.md, support matrix) restricts the v1 " +
            "supported envelope to Windows 11; this guard refuses every non-Windows host for production postures and " +
            "for packaged or customer local-workstation builds (architecture and Windows version are reported, not " +
            "refused). Linux container and systemd postures are experimental and must fail closed rather than " +
            "degrade. Platform-bound production bindings that would otherwise silently degrade: " +
            $"{string.Join("; ", PlatformBoundProductionBindings)}. Run the supported local-workstation posture on " +
            "Windows, or scope the host out of the supported envelope per ADR-019.";
    }

    private static string DescribeGatedPosture(IServiceCollection services)
    {
        var declared = ResolveDeclaredPosture(services);
        if (declared == MeridianDeploymentPosture.LocalWorkstation || IsLocalWorkstationEnvironment(CompositionConfiguration.Resolve(services)))
        {
            if (!IsProductionComposition(services) && ResolvePackagedBuildFlag(services) is { } flag)
                return $"{nameof(MeridianDeploymentPosture.LocalWorkstation)} (packaged build {flag.Name}={flag.Value})";
        }

        if (declared != MeridianDeploymentPosture.Unspecified)
        {
            return declared.ToString();
        }

        // Name the variable whose value actually selected production, using the same predicates and
        // precedence as IsProductionEnvironment, so ASPNETCORE_ENVIRONMENT=Development beside
        // MERIDIAN_API_DEPLOYMENT_MODE=ProductionApi is reported as the latter.
        var selected = ResolveProductionEnvironmentSelector(CompositionConfiguration.Resolve(services));
        return selected is null
            ? $"{nameof(MeridianDeploymentPosture.ProductionApi)} (environment)"
            : $"{nameof(MeridianDeploymentPosture.ProductionApi)} (environment {selected.Value.Name}={selected.Value.Value})";
    }

    /// <summary>
    /// The environment variable and value that make <see cref="IsProductionEnvironment"/> true, or
    /// <c>null</c> when none does. Mirrors that method's lookup order exactly.
    /// </summary>
    internal static (string Name, string Value)? ResolveProductionEnvironmentSelector(CompositionConfiguration configuration)
    {
        foreach (var name in EnvironmentNameVariables)
        {
            var value = configuration[name];
            if (value is null)
                continue;
            // IsProductionEnvironment coalesces these four: the first non-null value decides, and a
            // non-production value there still leaves the mode variables able to select production.
            if (string.Equals(value, "Production", StringComparison.OrdinalIgnoreCase))
                return (name, value);
            break;
        }

        return ResolveModeSelector(configuration);
    }

    private static (string Name, string Value)? ResolveModeSelector(CompositionConfiguration configuration)
    {
        var mode = configuration["MERIDIAN_MODE"];
        if (string.Equals(mode, "Production", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(mode, "Live", StringComparison.OrdinalIgnoreCase))
            return ("MERIDIAN_MODE", mode!);

        var apiDeploymentMode = configuration["MERIDIAN_API_DEPLOYMENT_MODE"];
        if (string.Equals(apiDeploymentMode, nameof(MeridianDeploymentPosture.ProductionApi), StringComparison.OrdinalIgnoreCase))
            return ("MERIDIAN_API_DEPLOYMENT_MODE", apiDeploymentMode!);

        return null;
    }

    private static readonly string[] EnvironmentNameVariables =
    [
        "ASPNETCORE_ENVIRONMENT",
        "DOTNET_ENVIRONMENT",
        "MERIDIAN_ENVIRONMENT",
        "MERIDIAN_DEPLOYMENT_ENVIRONMENT"
    ];

    internal static Type? GetRegisteredImplementationType(ServiceDescriptor descriptor)
        => descriptor.IsKeyedService
            ? descriptor.KeyedImplementationType ?? descriptor.KeyedImplementationInstance?.GetType()
            : descriptor.ImplementationType ?? descriptor.ImplementationInstance?.GetType();

    internal static bool IsNonProductionOnlyImplementation(Type implementationType)
    {
        if (implementationType is null)
        {
            return false;
        }

        if (typeof(INonProductionOnlyService).IsAssignableFrom(implementationType))
        {
            return true;
        }

        if (implementationType.GetCustomAttribute<NonProductionOnlyImplementationAttribute>() is not null)
        {
            return true;
        }

        return HasProhibitedNamePrefix(implementationType.Name)
               && !HasProductionSafeOptOut(implementationType);
    }

    private static bool HasProhibitedNamePrefix(string typeName)
        => ProhibitedNamePrefixes.Any(prefix =>
            typeName.Length > prefix.Length &&
            typeName.StartsWith(prefix, StringComparison.Ordinal) &&
            char.IsUpper(typeName[prefix.Length]));

    private static bool HasProductionSafeOptOut(Type implementationType)
        => implementationType
            .GetCustomAttributes(inherit: false)
            .Any(attribute => string.Equals(
                attribute.GetType().Name,
                nameof(ProductionSafeImplementationAttribute),
                StringComparison.Ordinal));

    /// <summary>
    /// Returns whether registrations added to <paramref name="services"/> are being composed for a
    /// production environment or an explicitly production API posture. Downstream feature modules use
    /// this to omit unsupported fallback capabilities before the final graph guard runs.
    /// </summary>
    public static bool IsProductionComposition(IServiceCollection services)
        => ResolveDeclaredPosture(services) == MeridianDeploymentPosture.ProductionApi
           || IsProductionEnvironment(CompositionConfiguration.Resolve(services));

    /// <summary>
    /// Rejects Quant Lab in every supported production or customer-distribution posture until the
    /// isolated worker's OS controls and deployment profile have production certification. The
    /// worker now provides killable process/resource containment, but it deliberately retains the
    /// launching identity's file and network permissions and is not a hostile-code sandbox.
    /// </summary>
    public static void EnsureIsolatedQuantLabIsAllowed(IServiceCollection services, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (!enabled)
        {
            return;
        }

        if (IsProductionComposition(services) ||
            IsTruthy(CompositionConfiguration.Resolve(services)["MDC_PACKAGED_BUILD"]) ||
            IsTruthy(CompositionConfiguration.Resolve(services)["MERIDIAN_CUSTOMER_BUILD"]))
        {
            throw new StartupRefusedException(
                "QuantLab:Enabled cannot be used in a production, packaged, or customer build. " +
                "The isolated worker's current OS controls do not remove the launching identity's " +
                "file/network permissions and are not certified as a hostile-code sandbox under ADR-019.");
        }
    }

    // ── Supported local-workstation data posture (ADR-019) ──────────────────────────────────────
    //
    // Production rejects every non-production binding outright. The supported *local* posture is
    // softer but still fail-closed about one thing: a money-path durable store must never run
    // in-memory while the operator is led to believe the data is real. So under the local posture we
    // either fail closed on an in-memory durable binding (when the host asserts durability), or force
    // the persistent simulated label so nothing fabricated is ever mistaken for real.

    private static readonly string[] DurableStoreServiceNameMarkers =
        ["Store", "Repository", "Sink", "Journal", "Ledger", "Archive", "Wal", "Persistence"];

    /// <summary>
    /// Whether registrations are being composed for the supported single-operator local-workstation
    /// posture. Distinct from <see cref="IsProductionComposition"/>: production owns its own outright
    /// rejection, so this returns <c>false</c> in a production composition.
    /// </summary>
    public static bool IsSupportedLocalComposition(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (IsProductionComposition(services))
        {
            return false;
        }

        return ResolveDeclaredPosture(services) == MeridianDeploymentPosture.LocalWorkstation
               || IsLocalWorkstationEnvironment(CompositionConfiguration.Resolve(services));
    }

    internal static bool IsLocalWorkstationEnvironment(CompositionConfiguration? configuration = null)
        => string.Equals(
            (configuration ?? new CompositionConfiguration())["MERIDIAN_API_DEPLOYMENT_MODE"],
            nameof(MeridianDeploymentPosture.LocalWorkstation),
            StringComparison.OrdinalIgnoreCase);

    internal static bool IsDurableStoreServiceType(Type? serviceType)
    {
        if (serviceType is null)
        {
            return false;
        }

        var name = serviceType.Name;
        return DurableStoreServiceNameMarkers.Any(marker => name.Contains(marker, StringComparison.Ordinal));
    }

    /// <summary>
    /// Durable-store service contracts bound to a non-production (in-memory/null/no-op/…)
    /// implementation, formatted as <c>Service -> Implementation</c> for diagnostics.
    /// </summary>
    internal static IReadOnlyList<string> CollectNonDurableStoreBindings(IServiceCollection services)
        => services
            .Where(descriptor => IsDurableStoreServiceType(descriptor.ServiceType))
            .Select(descriptor => (Service: descriptor.ServiceType, Implementation: GetRegisteredImplementationType(descriptor)))
            .Where(pair => pair.Implementation is not null && IsNonProductionOnlyImplementation(pair.Implementation!))
            .Select(pair => $"{pair.Service.FullName ?? pair.Service.Name} -> {pair.Implementation!.FullName ?? pair.Implementation!.Name}")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// The provenance a composed graph must surface. An explicit <see cref="MeridianDataProvenanceDeclaration"/>
    /// wins; otherwise a supported-local composition that bound any in-memory durable store forces
    /// <see cref="DataProvenance.Simulated"/> so fabricated data cannot pass for real. Real data (the
    /// only unlabeled state) requires either durable stores or no local fabrication at all.
    /// </summary>
    public static DataProvenance ResolveComposedDataProvenance(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (TryResolveForcedProvenance(services, out var forced))
        {
            return forced;
        }

        return IsSupportedLocalComposition(services) && CollectNonDurableStoreBindings(services).Count > 0
            ? DataProvenance.Simulated
            : DataProvenance.Real;
    }

    internal static bool TryResolveForcedProvenance(IServiceCollection services, out DataProvenance provenance)
    {
        for (var i = services.Count - 1; i >= 0; i--)
        {
            if (!services[i].IsKeyedService &&
                services[i].ImplementationInstance is MeridianDataProvenanceDeclaration declaration)
            {
                provenance = declaration.Provenance;
                return true;
            }
        }

        provenance = DataProvenance.Real;
        return false;
    }

    /// <summary>
    /// Fail-closed variant for the supported local posture. When <paramref name="requireDurableStores"/>
    /// is set (the host asserts real durability) any in-memory money-path store binding aborts
    /// composition with the offending bindings named. When durability is not required, callers force
    /// the persistent label via <see cref="ResolveComposedDataProvenance"/> instead of throwing.
    /// </summary>
    public static void ValidateSupportedLocal(IServiceCollection services, bool requireDurableStores)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (!requireDurableStores || !IsSupportedLocalComposition(services))
        {
            return;
        }

        ThrowIfNonDurableStoreBindings(CollectNonDurableStoreBindings(services));
    }

    internal static void ThrowIfNonDurableStoreBindings(IReadOnlyCollection<string> bindings)
    {
        if (bindings.Count == 0)
        {
            return;
        }

        throw new StartupRefusedException(
            "Supported local-workstation posture requires durable money-path stores but found in-memory " +
            $"bindings: {string.Join(", ", bindings)}. Bind a durable store, or drop the durability requirement " +
            "and force the persistent simulated-data label so fabricated data can never be mistaken for real.");
    }

    internal static MeridianDeploymentPosture ResolveDeclaredPosture(IServiceCollection services)
    {
        for (var i = services.Count - 1; i >= 0; i--)
        {
            if (!services[i].IsKeyedService &&
                services[i].ImplementationInstance is MeridianDeploymentPostureDeclaration declaration)
            {
                return declaration.Posture;
            }
        }

        return MeridianDeploymentPosture.Unspecified;
    }

    internal static bool IsProductionEnvironment(CompositionConfiguration? configuration = null)
    {
        configuration ??= new CompositionConfiguration();
        var environment = configuration["ASPNETCORE_ENVIRONMENT"]
                          ?? configuration["DOTNET_ENVIRONMENT"]
                          ?? configuration["MERIDIAN_ENVIRONMENT"]
                          ?? configuration["MERIDIAN_DEPLOYMENT_ENVIRONMENT"];

        var mode = configuration["MERIDIAN_MODE"];
        var apiDeploymentMode = configuration["MERIDIAN_API_DEPLOYMENT_MODE"];

        return string.Equals(environment, "Production", StringComparison.OrdinalIgnoreCase)
               || string.Equals(mode, "Production", StringComparison.OrdinalIgnoreCase)
               || string.Equals(mode, "Live", StringComparison.OrdinalIgnoreCase)
               || string.Equals(apiDeploymentMode, nameof(MeridianDeploymentPosture.ProductionApi), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTruthy(string? value)
        => value is not null &&
           (value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("yes", StringComparison.OrdinalIgnoreCase));
}
