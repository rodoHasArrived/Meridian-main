using System.Text.Json;
using Meridian.Contracts.Workstation;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Services;

public sealed record Dk1TrustGateReadinessOptions(string AutomationRoot)
{
    public static Dk1TrustGateReadinessOptions Default { get; } = new(
        Path.Combine("artifacts", "provider-validation", "_automation"));
}

/// <summary>
/// Projects the generated DK1 parity packet into the shared cockpit readiness model.
/// </summary>
public sealed class Dk1TrustGateReadinessService
{
    private static readonly string[] DefaultRequiredOwners =
    [
        "Data Operations",
        "Provider Reliability",
        "Trading"
    ];

    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    private readonly Dk1TrustGateReadinessOptions _options;
    private readonly ILogger<Dk1TrustGateReadinessService> _logger;
    private readonly object _cacheLock = new();

    private TradingTrustGateReadinessDto? _cachedReadiness;
    private DateTimeOffset _cachedAtUtc;

    public Dk1TrustGateReadinessService(
        Dk1TrustGateReadinessOptions options,
        ILogger<Dk1TrustGateReadinessService> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<TradingTrustGateReadinessDto> GetCurrentAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var now = DateTimeOffset.UtcNow;

        lock (_cacheLock)
        {
            if (_cachedReadiness is not null && now - _cachedAtUtc <= CacheDuration)
            {
                return _cachedReadiness;
            }
        }

        var readiness = await BuildCurrentAsync(ct).ConfigureAwait(false);

        lock (_cacheLock)
        {
            _cachedReadiness = readiness;
            _cachedAtUtc = now;
        }

        return readiness;
    }

    public static TradingTrustGateReadinessDto CreateUnavailable(string detail) =>
        new(
            GateId: "DK1",
            Status: "packet-unavailable",
            ReadyForOperatorReview: false,
            OperatorSignoffRequired: true,
            OperatorSignoffStatus: "unknown",
            GeneratedAt: null,
            PacketPath: null,
            SourceSummary: null,
            RequiredSampleCount: 4,
            ReadySampleCount: 0,
            ValidatedEvidenceDocumentCount: 0,
            RequiredOwners: DefaultRequiredOwners,
            Blockers: ["No DK1 pilot parity packet is available to the workstation."],
            Detail: detail,
            OperatorSignoff: new TradingOperatorSignoffReadinessDto(
                Status: "unknown",
                RequiredBeforeDk1Exit: true,
                RequiredOwners: DefaultRequiredOwners,
                SignedOwners: [],
                MissingOwners: DefaultRequiredOwners,
                CompletedAt: null,
                SourcePath: null));

    private async Task<TradingTrustGateReadinessDto> BuildCurrentAsync(CancellationToken ct)
    {
        var automationRoot = ResolveAutomationRoot();
        if (automationRoot is null)
        {
            return CreateUnavailable(
                $"DK1 parity packet root was not found: {_options.AutomationRoot}.");
        }

        var packetPath = FindLatestPacketPath(automationRoot);
        if (packetPath is null)
        {
            return CreateUnavailable(
                $"No DK1 pilot parity packet was found under {NormalizePath(automationRoot)}.");
        }

        try
        {
            await using var stream = File.OpenRead(packetPath);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
            return BuildReadinessFromPacket(packetPath, document.RootElement);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Unable to read DK1 pilot parity packet at {PacketPath}.", packetPath);
            return CreateUnavailable(
                $"DK1 pilot parity packet could not be read from {NormalizePath(packetPath)}.");
        }
    }

    private TradingTrustGateReadinessDto BuildReadinessFromPacket(string packetPath, JsonElement root)
    {
        var status = GetString(root, "status") ?? "unknown";
        var generatedAt = TryParseDateTimeOffset(GetString(root, "generatedAtUtc"));
        var sourceSummary = GetString(root, "sourceSummary");
        var sampleReview = TryGetProperty(root, "sampleReview");
        var evidenceDocuments = TryGetProperty(root, "evidenceDocuments");
        var trustRationaleContract = ReadContract(
            "trust-rationale",
            TryGetProperty(root, "trustRationaleContract"));
        var baselineThresholdContract = ReadContract(
            "baseline-threshold",
            TryGetProperty(root, "baselineThresholdContract"));
        var operatorSignoff = TryGetProperty(root, "operatorSignoff");
        var blockers = ReadStringArray(TryGetProperty(root, "blockers"))
            .Concat(BuildContractBlockers(trustRationaleContract, baselineThresholdContract))
            .ToArray();
        var requiredOwners = ReadStringArray(operatorSignoff, "requiredOwners");
        if (requiredOwners.Count == 0)
        {
            requiredOwners = DefaultRequiredOwners;
        }

        var sampleReviews = ReadSampleReviews(TryGetProperty(sampleReview, "samples"));
        var evidenceDocumentReviews = ReadEvidenceDocuments(evidenceDocuments);
        var requiredSampleCount = GetInt32(sampleReview, "requiredCount");
        var readySampleCount = sampleReviews.Count(static sample =>
            string.Equals(sample.Status, "ready", StringComparison.OrdinalIgnoreCase));
        var validatedEvidenceDocumentCount = evidenceDocumentReviews.Count(static document =>
            string.Equals(document.Status, "validated", StringComparison.OrdinalIgnoreCase));
        var operatorSignoffStatus = GetString(operatorSignoff, "status") ?? "unknown";
        var operatorSignoffRequired = GetBoolean(operatorSignoff, "requiredBeforeDk1Exit") ?? true;
        var signedOwners = ReadStringArray(operatorSignoff, "signedOwners");
        var missingOwners = ReadStringArray(operatorSignoff, "missingOwners");
        if (missingOwners.Count == 0 && operatorSignoffRequired && !IsOperatorSignoffComplete(operatorSignoffStatus))
        {
            missingOwners = requiredOwners
                .Where(owner => !signedOwners.Contains(owner, StringComparer.OrdinalIgnoreCase))
                .ToArray();
        }

        var operatorSignoffCompletedAt = TryParseDateTimeOffset(GetString(operatorSignoff, "completedAtUtc"));
        var operatorSignoffSourcePath = GetString(operatorSignoff, "sourcePath");
        var readyForReview = string.Equals(status, "ready-for-operator-review", StringComparison.OrdinalIgnoreCase);
        var operatorSignoffReadiness = new TradingOperatorSignoffReadinessDto(
            Status: operatorSignoffStatus,
            RequiredBeforeDk1Exit: operatorSignoffRequired,
            RequiredOwners: requiredOwners,
            SignedOwners: signedOwners,
            MissingOwners: missingOwners,
            CompletedAt: operatorSignoffCompletedAt,
            SourcePath: operatorSignoffSourcePath);

        return new TradingTrustGateReadinessDto(
            GateId: "DK1",
            Status: status,
            ReadyForOperatorReview: readyForReview,
            OperatorSignoffRequired: operatorSignoffRequired,
            OperatorSignoffStatus: operatorSignoffStatus,
            GeneratedAt: generatedAt,
            PacketPath: NormalizePath(packetPath),
            SourceSummary: sourceSummary,
            RequiredSampleCount: requiredSampleCount,
            ReadySampleCount: readySampleCount,
            ValidatedEvidenceDocumentCount: validatedEvidenceDocumentCount,
            RequiredOwners: requiredOwners,
            Blockers: blockers,
            Detail: BuildDetail(
                status,
                readyForReview,
                operatorSignoffRequired,
                operatorSignoffStatus,
                requiredOwners,
                signedOwners,
                missingOwners,
                blockers,
                trustRationaleContract,
                baselineThresholdContract),
            OperatorSignoff: operatorSignoffReadiness)
        {
            SampleReviews = sampleReviews,
            EvidenceDocuments = evidenceDocumentReviews,
            TrustRationaleContract = trustRationaleContract,
            BaselineThresholdContract = baselineThresholdContract
        };
    }

    private string? ResolveAutomationRoot()
    {
        var configuredRoot = _options.AutomationRoot;
        if (Path.IsPathRooted(configuredRoot))
        {
            return Directory.Exists(configuredRoot) ? Path.GetFullPath(configuredRoot) : null;
        }

        foreach (var root in GetCandidateRoots())
        {
            var candidate = Path.GetFullPath(Path.Combine(root, configuredRoot));
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string> GetCandidateRoots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in new[]
                 {
                     Directory.GetCurrentDirectory(),
                     FindRepositoryRoot(AppContext.BaseDirectory),
                     AppContext.BaseDirectory
                 })
        {
            if (!string.IsNullOrWhiteSpace(candidate) && seen.Add(candidate))
            {
                yield return candidate;
            }
        }
    }

    private static string? FindRepositoryRoot(string startPath)
    {
        var directory = new DirectoryInfo(startPath);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Meridian.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static string? FindLatestPacketPath(string automationRoot)
    {
        return Directory
            .EnumerateFiles(automationRoot, "dk1-pilot-parity-packet.json", SearchOption.AllDirectories)
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Select(file => file.FullName)
            .FirstOrDefault();
    }

    private static string BuildDetail(
        string status,
        bool readyForReview,
        bool operatorSignoffRequired,
        string operatorSignoffStatus,
        IReadOnlyList<string> requiredOwners,
        IReadOnlyList<string> signedOwners,
        IReadOnlyList<string> missingOwners,
        IReadOnlyList<string> blockers,
        TradingTrustGateContractReadinessDto? trustRationaleContract,
        TradingTrustGateContractReadinessDto? baselineThresholdContract)
    {
        string detail;
        if (blockers.Count > 0)
        {
            detail = blockers[0];
        }
        else if (readyForReview && operatorSignoffRequired && !IsOperatorSignoffComplete(operatorSignoffStatus))
        {
            if (signedOwners.Count > 0 && missingOwners.Count > 0)
            {
                detail = $"DK1 packet is ready for operator review; sign-off is partial with {string.Join(", ", missingOwners)} still missing.";
            }
            else if (missingOwners.Count > 0)
            {
                detail = $"DK1 packet is ready for operator review; {string.Join(", ", missingOwners)} sign-off remains {operatorSignoffStatus}.";
            }
            else
            {
                detail = $"DK1 packet is ready for operator review; {string.Join(", ", requiredOwners)} sign-off remains {operatorSignoffStatus}.";
            }
        }
        else if (readyForReview && operatorSignoffRequired && IsOperatorSignoffComplete(operatorSignoffStatus))
        {
            detail = signedOwners.Count > 0
                ? $"DK1 packet is ready for operator review; operator sign-off is complete for {string.Join(", ", signedOwners)}."
                : "DK1 packet is ready for operator review; operator sign-off is complete.";
        }
        else if (readyForReview)
        {
            detail = "DK1 packet is ready for operator review with no packet blockers.";
        }
        else
        {
            detail = $"DK1 packet status is {status}.";
        }

        return AppendContractSummary(detail, trustRationaleContract, baselineThresholdContract);
    }

    private static string AppendContractSummary(
        string detail,
        TradingTrustGateContractReadinessDto? trustRationaleContract,
        TradingTrustGateContractReadinessDto? baselineThresholdContract)
    {
        var summary = BuildContractSummary(trustRationaleContract, baselineThresholdContract);
        return string.IsNullOrWhiteSpace(summary)
            ? detail
            : $"{detail} {summary}";
    }

    private static string BuildContractSummary(
        TradingTrustGateContractReadinessDto? trustRationaleContract,
        TradingTrustGateContractReadinessDto? baselineThresholdContract)
    {
        var parts = new[]
        {
            BuildContractSummaryPart("explainability", trustRationaleContract),
            BuildContractSummaryPart("calibration", baselineThresholdContract)
        }.Where(static part => !string.IsNullOrWhiteSpace(part));

        var summary = string.Join("; ", parts);
        return string.IsNullOrWhiteSpace(summary)
            ? string.Empty
            : $"Contract review: {summary}.";
    }

    private static string BuildContractSummaryPart(
        string label,
        TradingTrustGateContractReadinessDto? contract)
    {
        if (contract is null)
        {
            return $"{label} unavailable";
        }

        if (string.Equals(contract.Status, "validated", StringComparison.OrdinalIgnoreCase))
        {
            return $"{label} validated";
        }

        var missing = contract.MissingRequirements.Count > 0
            ? $" missing {string.Join(", ", contract.MissingRequirements)}"
            : string.Empty;
        return $"{label} {contract.Status}{missing}";
    }

    private static bool IsOperatorSignoffComplete(string status) =>
        string.Equals(status, "signed", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "approved", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "complete", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> BuildContractBlockers(
        TradingTrustGateContractReadinessDto? trustRationaleContract,
        TradingTrustGateContractReadinessDto? baselineThresholdContract)
    {
        var blockers = new List<string>();
        AddContractBlocker(blockers, "explainability", trustRationaleContract);
        AddContractBlocker(blockers, "calibration", baselineThresholdContract);
        return blockers;
    }

    private static void AddContractBlocker(
        ICollection<string> blockers,
        string label,
        TradingTrustGateContractReadinessDto? contract)
    {
        if (contract is null)
        {
            blockers.Add($"DK1 {label} contract is missing from the parity packet.");
            return;
        }

        if (string.Equals(contract.Status, "validated", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var missing = contract.MissingRequirements.Count > 0
            ? $": missing {string.Join(", ", contract.MissingRequirements)}"
            : ".";
        blockers.Add($"DK1 {label} contract is {contract.Status}{missing}");
    }

    private static IReadOnlyList<TradingTrustGateSampleReviewDto> ReadSampleReviews(JsonElement? element)
    {
        if (element is not { ValueKind: JsonValueKind.Array } value)
        {
            return [];
        }

        return value
            .EnumerateArray()
            .Where(static item => item.ValueKind == JsonValueKind.Object)
            .Select(static item => new TradingTrustGateSampleReviewDto(
                SampleId: GetString(item, "id") ?? string.Empty,
                Provider: GetString(item, "provider") ?? string.Empty,
                RequiredStep: GetString(item, "requiredStep") ?? string.Empty,
                StepStatus: GetString(item, "stepStatus") ?? string.Empty,
                Status: GetString(item, "status") ?? "unknown",
                Observed: GetBoolean(item, "observed") ?? false,
                MissingRequirements: ReadStringArray(item, "missingRequirements"),
                EvidenceAnchors: ReadStringArray(item, "evidenceAnchors"),
                AcceptanceCheck: GetString(item, "acceptanceCheck") ?? string.Empty))
            .ToArray();
    }

    private static IReadOnlyList<TradingTrustGateEvidenceDocumentDto> ReadEvidenceDocuments(JsonElement? element)
    {
        if (element is not { ValueKind: JsonValueKind.Array } value)
        {
            return [];
        }

        return value
            .EnumerateArray()
            .Where(static item => item.ValueKind == JsonValueKind.Object)
            .Select(static item => new TradingTrustGateEvidenceDocumentDto(
                Name: GetString(item, "name") ?? string.Empty,
                Gate: GetString(item, "gate") ?? string.Empty,
                Path: GetString(item, "path") ?? string.Empty,
                Exists: GetBoolean(item, "exists") ?? false,
                Status: GetString(item, "status") ?? "unknown",
                MissingRequirements: ReadStringArray(item, "missingRequirements")))
            .ToArray();
    }

    private static TradingTrustGateContractReadinessDto? ReadContract(string contractId, JsonElement? element)
    {
        if (element is not { ValueKind: JsonValueKind.Object } value)
        {
            return null;
        }

        return new TradingTrustGateContractReadinessDto(
            ContractId: contractId,
            DocumentPath: GetString(value, "documentPath") ?? string.Empty,
            Status: GetString(value, "status") ?? "unknown",
            RequiredPayloadFields: ReadStringArray(value, "requiredPayloadFields"),
            RequiredReasonCodes: ReadStringArray(value, "requiredReasonCodes"),
            RequiredMetrics: ReadStringArray(value, "requiredMetrics"),
            FpFnReviewRequired: GetBoolean(value, "fpFnReviewRequired"),
            MissingRequirements: ReadStringArray(value, "missingRequirements"));
    }

    private static JsonElement? TryGetProperty(JsonElement? element, string propertyName)
    {
        if (element is not { ValueKind: JsonValueKind.Object } value ||
            !value.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property;
    }

    private static string? GetString(JsonElement? element, string propertyName)
    {
        var property = TryGetProperty(element, propertyName);
        return property is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;
    }

    private static int GetInt32(JsonElement? element, string propertyName)
    {
        var property = TryGetProperty(element, propertyName);
        return property is { ValueKind: JsonValueKind.Number } value && value.TryGetInt32(out var number)
            ? number
            : 0;
    }

    private static bool? GetBoolean(JsonElement? element, string propertyName)
    {
        var property = TryGetProperty(element, propertyName);
        return property is { ValueKind: JsonValueKind.True or JsonValueKind.False } value ? value.GetBoolean() : null;
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement? element, string propertyName) =>
        ReadStringArray(TryGetProperty(element, propertyName));

    private static IReadOnlyList<string> ReadStringArray(JsonElement? element)
    {
        if (element is not { ValueKind: JsonValueKind.Array } value)
        {
            return [];
        }

        return value
            .EnumerateArray()
            .Where(static item => item.ValueKind == JsonValueKind.String)
            .Select(static item => item.GetString())
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Select(static item => item!)
            .ToArray();
    }

    private static int CountObjectsWithStringProperty(JsonElement? element, string propertyName, string expectedValue)
    {
        if (element is not { ValueKind: JsonValueKind.Array } value)
        {
            return 0;
        }

        return value
            .EnumerateArray()
            .Count(item => string.Equals(GetString(item, propertyName), expectedValue, StringComparison.OrdinalIgnoreCase));
    }

    private static DateTimeOffset? TryParseDateTimeOffset(string? value) =>
        DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;

    private static string NormalizePath(string path) =>
        path.Replace(Path.DirectorySeparatorChar, '/');
}
