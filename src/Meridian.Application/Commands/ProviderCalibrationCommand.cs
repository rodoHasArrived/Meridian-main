using System.Text.Json;
using Meridian.Application.Monitoring;
using Meridian.Application.ResultTypes;
using Serilog;

namespace Meridian.Application.Commands;

internal sealed class ProviderCalibrationCommand : ICliCommand
{
    private readonly string _dataRoot;
    private readonly ILogger _log;

    public ProviderCalibrationCommand(string dataRoot, ILogger log)
    {
        _dataRoot = dataRoot;
        _log = log;
    }

    public bool CanHandle(string[] args) => CliArguments.HasFlag(args, "--calibrate-provider-degradation");

    public async Task<CliResult> ExecuteAsync(string[] args, CancellationToken ct = default)
    {
        var inputPath = CliArguments.RequireValue(args, "--calibration-input", "--calibration-input ./incidents.json");
        if (inputPath is null)
        {
            return CliResult.Fail(ErrorCode.ConfigurationInvalid);
        }

        var candidateKernelVersion = CliArguments.GetValue(args, "--candidate-kernel-version") ?? $"kernel-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        var baselineKernelVersion = CliArguments.GetValue(args, "--baseline-kernel-version") ?? "default";
        var runBy = CliArguments.GetValue(args, "--run-by") ?? Environment.UserName;

        var baselineConfigPath = CliArguments.GetValue(args, "--baseline-config");
        var candidateConfigPath = CliArguments.GetValue(args, "--candidate-config");

        var baselineProfile = new ProviderDegradationKernelProfile(
            baselineKernelVersion,
            await LoadConfigOrDefaultAsync(baselineConfigPath, ct).ConfigureAwait(false),
            ProviderDegradationKernelProfile.Default().SeverityThresholds);

        var candidateProfile = new ProviderDegradationKernelProfile(
            candidateKernelVersion,
            await LoadConfigOrDefaultAsync(candidateConfigPath, ct).ConfigureAwait(false),
            ProviderDegradationKernelProfile.Default().SeverityThresholds);

        var dataset = await ProviderIncidentCalibrationDataset.LoadAsync(inputPath, ct).ConfigureAwait(false);
        var runner = new ProviderDegradationCalibrationRunner();
        var snapshot = runner.Run(dataset, baselineProfile, candidateProfile, runBy);

        var snapshotStore = new ProviderKernelCalibrationSnapshotStore(_dataRoot);
        var snapshotPath = await snapshotStore.SaveAsync(snapshot, ct).ConfigureAwait(false);

        var markdown = ProviderCalibrationReportWriter.BuildMarkdown(snapshot);
        var reportOutputPath = CliArguments.GetValue(args, "--calibration-report")
            ?? Path.Combine(_dataRoot, "calibration", "provider-degradation", "latest-report.md");

        Directory.CreateDirectory(Path.GetDirectoryName(reportOutputPath) ?? ".");
        await File.WriteAllTextAsync(reportOutputPath, markdown, ct).ConfigureAwait(false);

        var freshnessHours = CliArguments.GetInt(args, "--freshness-hours", 24 * 14);
        var minPrecision = LoadDouble(args, "--min-precision", 0.65);
        var minRecall = LoadDouble(args, "--min-recall", 0.65);

        var policy = new ProviderKernelCalibrationPolicy(
            TimeSpan.FromHours(freshnessHours),
            minPrecision,
            minRecall,
            IncidentSeverity.Critical);

        var governanceWorkflow = new KernelWeightGovernanceWorkflowService(policy);
        var promotionDecision = governanceWorkflow.EvaluatePromotion(candidateKernelVersion, snapshot, runBy);

        var governanceOutputPath = CliArguments.GetValue(args, "--governance-output")
            ?? Path.Combine(_dataRoot, "calibration", "provider-degradation", "latest-governance-decision.json");

        await File.WriteAllTextAsync(
            governanceOutputPath,
            JsonSerializer.Serialize(promotionDecision, new JsonSerializerOptions { WriteIndented = true }),
            ct).ConfigureAwait(false);

        _log.Information(
            "Provider calibration complete. Snapshot={SnapshotPath}; report={ReportPath}; governanceApproved={Approved}",
            snapshotPath,
            reportOutputPath,
            promotionDecision.Approved);

        Console.WriteLine(markdown);
        Console.WriteLine($"Governance calibration gate pass: {promotionDecision.CalibrationPass}");

        return promotionDecision.Approved
            ? CliResult.Ok()
            : CliResult.Fail(ErrorCode.ConfigurationInvalid);
    }

    private static async Task<ProviderDegradationConfig> LoadConfigOrDefaultAsync(string? path, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return ProviderDegradationConfig.Default;
        }

        await using var stream = File.OpenRead(path);
        var config = await JsonSerializer.DeserializeAsync<ProviderDegradationConfig>(stream, cancellationToken: ct)
            .ConfigureAwait(false);

        return config ?? ProviderDegradationConfig.Default;
    }

    private static double LoadDouble(string[] args, string key, double defaultValue)
    {
        var raw = CliArguments.GetValue(args, key);
        return double.TryParse(raw, out var value) ? value : defaultValue;
    }
}
