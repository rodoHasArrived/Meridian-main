using Meridian.Contracts.SecurityMaster;
using Microsoft.Extensions.Options;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Validates <see cref="SecurityMasterOptions"/> at startup so misconfigured
/// connection strings or out-of-range tuning parameters are caught early.
/// </summary>
public sealed class SecurityMasterOptionsValidator : IValidateOptions<SecurityMasterOptions>
{
    public ValidateOptionsResult Validate(string? name, SecurityMasterOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            failures.Add(
                $"{nameof(SecurityMasterOptions.ConnectionString)} is required. " +
                "Set the MERIDIAN_SECURITY_MASTER_CONNECTION_STRING environment variable.");
        }

        if (string.IsNullOrWhiteSpace(options.Schema))
        {
            failures.Add($"{nameof(SecurityMasterOptions.Schema)} must not be empty.");
        }

        if (options.SnapshotIntervalVersions < 1)
        {
            failures.Add($"{nameof(SecurityMasterOptions.SnapshotIntervalVersions)} must be >= 1 (got {options.SnapshotIntervalVersions}).");
        }

        if (options.ProjectionReplayBatchSize < 1)
        {
            failures.Add($"{nameof(SecurityMasterOptions.ProjectionReplayBatchSize)} must be >= 1 (got {options.ProjectionReplayBatchSize}).");
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
