using Meridian.Application.Composition;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.SecurityMaster.Schedules;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Application.SecurityMaster;

/// <summary>Verifies the persisted floor before a configured writer may serialize or persist terms.</summary>
public interface ISecurityMasterTermsCapabilityVerifier
{
    /// <summary>Performs a fresh check; no successful startup or earlier command is cached.</summary>
    Task VerifyAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// The shared startup and command-boundary verifier. Desktop windows can be visible before hosted
/// services start, so startup verification alone is not authority for a later canonical write.
/// </summary>
public sealed class SecurityMasterTermsCapabilityVerifier : ISecurityMasterTermsCapabilityVerifier
{
    private readonly Func<SecurityMasterOptions?> _options;
    private readonly Func<ISecurityMasterTermsCapabilityStore> _store;
    private readonly ILogger _logger;

    /// <summary>Uses the configured shared/server or desktop graph, resolving persistence lazily.</summary>
    public SecurityMasterTermsCapabilityVerifier(IServiceProvider services, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        _options = () => services.GetService<SecurityMasterOptions>();
        _store = () => services.GetRequiredService<ISecurityMasterTermsCapabilityStore>();
        _logger = logger ?? NullLogger.Instance;
    }

    // Direct/manual service construction retains in-memory compatibility. Configured writers
    // cannot bypass verification merely because their optional DI argument was omitted.
    internal SecurityMasterTermsCapabilityVerifier(SecurityMasterOptions options, ILogger logger)
    {
        _options = () => options;
        _store = () => new PostgresSecurityMasterTermsCapabilityStore(options);
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task VerifyAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // The WPF desktop and shared/server host both register their configured writer options
        // before the provider is built. Null/unconfigured graphs do no persistence work.
        SecurityMasterTermsCapabilityState? floorState = null;
        var verifyFloor = false;
        var writesDeclaredTerms = false;
        try
        {
            var options = _options();
            if (options is not null && !string.IsNullOrWhiteSpace(options.ConnectionString))
            {
                floorState = await _store()
                    .ReadAsync(cancellationToken).ConfigureAwait(false);
                writesDeclaredTerms = WritesDeclaredTerms();
                verifyFloor = true;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // A connection-string parser/provider may include credentials in its exception text.
            // Escalate the refusal through the shared WPF host predicate, without retaining that
            // exception as an inner exception that the shell would render or log in full.
            var sqlState = exception is Npgsql.PostgresException postgres
                && postgres.SqlState.Length == 5
                && postgres.SqlState.All(static character => character is >= 'A' and <= 'Z' or >= '0' and <= '9')
                    ? postgres.SqlState : "unavailable";
            _logger.LogError("Security Master capability-floor verification failed ({FailureType}, SQLSTATE {SqlState}); refusing configured writer startup.",
                exception.GetType().Name, sqlState);
            throw new StartupRefusedException(
                "Security Master minimum terms capability could not be verified; refusing configured writer startup. Verify connectivity, permissions and migration 057.");
        }
        if (verifyFloor)
            EnsureCompatible(floorState!, writesDeclaredTerms, _logger);
    }

    internal static bool WritesDeclaredTerms()
        => SecurityAssetClassCatalog.All.Any(descriptor =>
            SecurityAssetTermsSchema.Fields(descriptor.AssetClass).Any(field =>
                IsDeclaredContainer(field) && field.SinceTermsVersion <= descriptor.TermsVersion)
            || SecurityAssetTermsSchema.ElementFields(descriptor.AssetClass, "legs").Any(field =>
                IsDeclaredContainer(field) && field.SinceTermsVersion <= descriptor.TermsVersion));

    private static bool IsDeclaredContainer(SecurityAssetTermField field)
        => SecurityVersionedTerms.IsVersionedContainer(field)
           && field.Key is SecurityVersionedTerms.ScheduleConventionsKey
               or SecurityVersionedTerms.CallScheduleKey or SecurityVersionedTerms.PutScheduleKey;

    internal static void EnsureCompatible(
        SecurityMasterTermsCapabilityState state,
        bool writesDeclaredTerms,
        ILogger logger)
    {
        if (state is null)
            throw new StartupRefusedException("Security Master minimum terms capability state is missing; refusing writer startup.");
        if (!state.TableExists && state.Floor is not null)
            throw new StartupRefusedException("Security Master minimum terms capability state is inconsistent; refusing writer startup.");
        if (state.Floor is not { } floor)
        {
            if (writesDeclaredTerms)
                throw new StartupRefusedException(
                    "Security Master declared-term writes require an installed and activated minimum capability floor; refusing writer startup.");
            logger.LogWarning(
                "Security Master minimum terms capability floor is {FloorState}; this node writes only pre-contract terms. Migration 057 and reviewed floor activation must precede any declared-term write-version raise.",
                state.TableExists ? "not activated" : "not installed");
            return;
        }
        // Capability tokens are opaque contracts, not lexically ordered version strings. This
        // release supports only its frozen v1 mapping/kernel; a future release must explicitly
        // declare any older floor it still satisfies instead of guessing from a numeric suffix.
        if (!string.Equals(floor.MinimumDeclaredMappingVersion, SecurityVersionedTerms.DeclaredScheduleMappingVersion, StringComparison.Ordinal)
            || !string.Equals(floor.MinimumKernelVersion, ScheduleGenerator.KernelVersion, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(floor.ChangeRecord)
            || string.IsNullOrWhiteSpace(floor.UpdatedBy))
            throw new StartupRefusedException(
                "Security Master minimum terms capability is unknown, newer, or invalid for this node; refusing writer startup.");
        logger.LogInformation(
            "Security Master minimum terms capability accepted: declared mapping {DeclaredMappingVersion}, schedule kernel {KernelVersion}",
            floor.MinimumDeclaredMappingVersion, floor.MinimumKernelVersion);
    }

}
