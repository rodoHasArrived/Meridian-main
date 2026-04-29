namespace Meridian.Storage.Export;

/// <summary>
/// Export-specific probe snapshot collected during the I/O phase.
/// </summary>
public sealed record ExportPreflightContext(
    ExportRequest Request,
    long EstimatedBytes,
    long AvailableDiskSpaceBytes,
    bool HasWritePermission,
    long RecordCount)
{
    public bool HasComplexEventTypes =>
        Request.EventTypes.Any(t =>
            t.Equals("LOBSnapshot", StringComparison.OrdinalIgnoreCase) ||
            t.Equals("OrderBook", StringComparison.OrdinalIgnoreCase));

    public double DiskSpaceMultiplier => Request.ValidationRules?.DiskSpaceMultiplier ?? 1.2;

    public long RequiredDiskBytes => (long)(EstimatedBytes * DiskSpaceMultiplier);

    public bool RequireData => Request.ValidationRules?.RequireData ?? false;

    public bool WarnCsvComplexTypes => Request.ValidationRules?.WarnCsvComplexTypes ?? true;
}

public static class ExportPreflightRules
{
    public static readonly IPreflightRule<ExportPreflightContext> DiskSpace =
        new DelegatePreflightRule<ExportPreflightContext>(
            "export.disk-space.v1",
            static context =>
            {
                if (context.AvailableDiskSpaceBytes < 0 || context.AvailableDiskSpaceBytes >= context.RequiredDiskBytes)
                    return null;

                return new PreflightIssue(
                    RuleId: "export.disk-space.v1",
                    Code: "DISK_SPACE",
                    Severity: PreflightSeverity.Error,
                    Message: $"Insufficient disk space. Need {context.RequiredDiskBytes / (1024.0 * 1024 * 1024):F2} GB " +
                             $"({context.DiskSpaceMultiplier:P0} safety margin), " +
                             $"have {context.AvailableDiskSpaceBytes / (1024.0 * 1024 * 1024):F2} GB available.",
                    Remediation: "Free disk capacity or choose an output path on a larger volume.",
                    Details: new Dictionary<string, object>
                    {
                        ["estimatedBytes"] = context.EstimatedBytes,
                        ["requiredBytes"] = context.RequiredDiskBytes,
                        ["availableBytes"] = context.AvailableDiskSpaceBytes,
                        ["multiplier"] = context.DiskSpaceMultiplier
                    });
            });

    public static readonly IPreflightRule<ExportPreflightContext> WritePermission =
        new DelegatePreflightRule<ExportPreflightContext>(
            "export.write-permission.v1",
            static context =>
            {
                if (string.IsNullOrEmpty(context.Request.OutputDirectory) || context.HasWritePermission)
                    return null;

                return new PreflightIssue(
                    RuleId: "export.write-permission.v1",
                    Code: "WRITE_PERMISSION",
                    Severity: PreflightSeverity.Error,
                    Message: $"No write permission for output path: {context.Request.OutputDirectory}",
                    Remediation: "Grant write access to the export directory or select a writable location.");
            });

    public static readonly IPreflightRule<ExportPreflightContext> DataPresence =
        new DelegatePreflightRule<ExportPreflightContext>(
            "export.data-presence.v1",
            static context =>
            {
                if (context.RecordCount > 0)
                    return null;

                var severity = context.RequireData ? PreflightSeverity.Error : PreflightSeverity.Warning;
                var remediation = context.RequireData
                    ? "Adjust symbols/date range/event types until matching records exist."
                    : "Broaden filters if you expected records; otherwise continue for an empty export.";

                return new PreflightIssue(
                    RuleId: "export.data-presence.v1",
                    Code: "NO_DATA",
                    Severity: severity,
                    Message: "No data found for the specified date range, symbols and event types.",
                    Remediation: remediation);
            });

    public static readonly IPreflightRule<ExportPreflightContext> CsvComplexTypes =
        new DelegatePreflightRule<ExportPreflightContext>(
            "export.csv-complex-types.v1",
            static context =>
            {
                if (!context.WarnCsvComplexTypes || context.Request.CustomProfile?.Format != ExportFormat.Csv || !context.HasComplexEventTypes)
                    return null;

                return new PreflightIssue(
                    RuleId: "export.csv-complex-types.v1",
                    Code: "CSV_COMPLEX_TYPES",
                    Severity: PreflightSeverity.Warning,
                    Message: "CSV format may lose nested data structures (e.g. order-book depth). Consider using Parquet or JSONL to preserve all fields.",
                    Remediation: "Switch export format to Parquet or JSONL when nested payload fidelity is required.");
            });

    public static IReadOnlyList<IPreflightRule<ExportPreflightContext>> DefaultRules { get; } = new[]
    {
        DiskSpace,
        WritePermission,
        DataPresence,
        CsvComplexTypes
    };

    private sealed class DelegatePreflightRule<TContext> : IPreflightRule<TContext>
    {
        private readonly Func<TContext, PreflightIssue?> _evaluate;

        public DelegatePreflightRule(string id, Func<TContext, PreflightIssue?> evaluate)
        {
            Id = id;
            _evaluate = evaluate;
        }

        public string Id { get; }

        public PreflightIssue? Evaluate(TContext context) => _evaluate(context);
    }
}
