using Meridian.Application.Config;
using Meridian.Application.Commands;
using Meridian.Application.Monitoring;
using Meridian.Application.ResultTypes;
using Meridian.Application.Services;
using Meridian.Storage.Services;
using Serilog;

namespace Meridian.Application.Composition.Startup;

internal static class StartupValidationRunner
{
    public static int? ValidateConfiguration(AppConfig cfg, ConfigurationService configService, ILogger log)
    {
        if (configService.ValidateConfig(cfg, out _))
        {
            return null;
        }

        log.Error("Exiting due to configuration errors (ExitCode={ExitCode})",
            ErrorCode.ConfigurationInvalid.ToExitCode());
        return ErrorCode.ConfigurationInvalid.ToExitCode();
    }

    public static int? EnsureDataDirectoryPermissions(AppConfig cfg, ILogger log)
    {
        var permissionsService = new FilePermissionsService(new FilePermissionsOptions
        {
            DirectoryMode = "755",
            FileMode = "644",
            ValidateOnStartup = true
        });

        var permissionsResult = permissionsService.EnsureDirectoryPermissions(cfg.DataRoot);
        if (permissionsResult.Success)
        {
            log.Information("Data directory permissions configured: {Message}", permissionsResult.Message);
            return null;
        }

        log.Error("Failed to configure data directory permissions: {Message} (ExitCode={ExitCode}). " +
            "Troubleshooting: 1) Check that the application has write access to the parent directory. " +
            "2) On Linux/macOS, ensure the user has appropriate permissions. " +
            "3) On Windows, run as administrator if needed.",
            permissionsResult.Message, ErrorCode.FileAccessDenied.ToExitCode());
        return ErrorCode.FileAccessDenied.ToExitCode();
    }

    public static async Task<int?> ValidateSchemasAsync(
        CliArguments cliArgs,
        AppConfig cfg,
        ILogger log,
        CancellationToken ct = default)
    {
        if (!cliArgs.ValidateSchemas)
        {
            return null;
        }

        log.Information("Running startup schema compatibility check...");
        await using var schemaService = new SchemaValidationService(
            new SchemaValidationOptions { EnableVersionTracking = true },
            cfg.DataRoot);

        var schemaCheckResult = await schemaService.PerformStartupCheckAsync(ct);
        if (schemaCheckResult.Success)
        {
            log.Information("Schema compatibility check passed: {Message}", schemaCheckResult.Message);
            return null;
        }

        log.Warning("Schema compatibility check found issues: {Message}", schemaCheckResult.Message);
        if (!cliArgs.StrictSchemas)
        {
            return null;
        }

        log.Error("Exiting due to schema incompatibilities (--strict-schemas enabled, ExitCode={ExitCode})",
            ErrorCode.SchemaMismatch.ToExitCode());
        return ErrorCode.SchemaMismatch.ToExitCode();
    }
}
