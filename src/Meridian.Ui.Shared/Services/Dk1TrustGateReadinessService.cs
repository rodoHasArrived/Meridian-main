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
            Detail: detail);

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
        var operatorSignoff = TryGetProperty(root, "operatorSignoff");
        var blockers = ReadStringArray(TryGetProperty(root, "blockers"));
        var requiredOwners = ReadStringArray(operatorSignoff, "requiredOwners");
        if (requiredOwners.Count == 0)
        {
            requiredOwners = DefaultRequiredOwners;
        }

        var requiredSampleCount = GetInt32(sampleReview, "requiredCount");
        var readySampleCount = CountObjectsWithStringProperty(
            TryGetProperty(sampleReview, "samples"),
            "status",
            "ready");
        var validatedEvidenceDocumentCount = CountObjectsWithStringProperty(
            evidenceDocuments,
            "status",
            "validated");
        var operatorSignoffStatus = GetString(operatorSignoff, "status") ?? "unknown";
        var operatorSignoffRequired = GetBoolean(operatorSignoff, "requiredBeforeDk1Exit") ?? true;
        var readyForReview = string.Equals(status, "ready-for-operator-review", StringComparison.OrdinalIgnoreCase);

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
            Detail: BuildDetail(status, readyForReview, operatorSignoffRequired, operatorSignoffStatus, requiredOwners, blockers));
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
        IReadOnlyList<string> blockers)
    {
        if (blockers.Count > 0)
        {
            return blockers[0];
        }

        if (readyForReview && operatorSignoffRequired && !IsOperatorSignoffComplete(operatorSignoffStatus))
        {
            return $"DK1 packet is ready for operator review; {string.Join(", ", requiredOwners)} sign-off remains {operatorSignoffStatus}.";
        }

        if (readyForReview)
        {
            return "DK1 packet is ready for operator review with no packet blockers.";
        }

        return $"DK1 packet status is {status}.";
    }

    private static bool IsOperatorSignoffComplete(string status) =>
        string.Equals(status, "signed", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "approved", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "complete", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase);

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
