using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Meridian.Contracts.AssetOperations;

namespace Meridian.Instruments.AssetOperations;

public interface IFactorPaydownProjectionService
{
    FactorPaydownProjectionResult Project(FactorPaydownProjectionRequest request);
}

public enum FactorPaydownProjectionStatus : byte
{
    Rejected = 0,
    NoChange = 1,
    Projected = 2
}

public sealed record FactorPaydownProjectionRequest(
    Guid SecurityId,
    Guid PositionId,
    long PositionVersion,
    long ExpectedPositionVersion,
    decimal HeldFace,
    decimal PriorFactor,
    decimal CurrentFactor,
    string Currency,
    DateOnly EffectiveDate,
    DateTimeOffset OccurredAtUtc,
    string SourceDomain,
    string SourceEntityId,
    string SourceContentHash,
    IReadOnlyList<string>? EvidenceLinks = null,
    Guid? CorrelationId = null,
    Guid? CausationId = null,
    DateTimeOffset? GeneratedAtUtc = null)
{
    public IReadOnlyList<string> EvidenceLinks { get; init; } = EvidenceLinks ?? [];
}

public sealed record FactorPaydownProjectionIssue(string Code, string Message);

public sealed record FactorPaydownProjectionResult(
    FactorPaydownProjectionStatus Status,
    decimal? PrincipalPaydown,
    EconomicEventReferenceDto? EconomicEvent,
    PositionEconomicStateDto? EconomicState,
    ProjectionLineageDto? Lineage,
    IReadOnlyList<FactorPaydownProjectionIssue>? Issues = null)
{
    public IReadOnlyList<FactorPaydownProjectionIssue> Issues { get; init; } = Issues ?? [];

    public bool ProducesPostingCandidate =>
        Status == FactorPaydownProjectionStatus.Projected && PrincipalPaydown is > 0m;
}

public sealed class FactorPaydownProjectionService : IFactorPaydownProjectionService
{
    public const string EventType = "MbsFactorPaydown";
    public const string ModelKey = "mbs-factor-paydown";
    public const string ModelVersion = "1.0.0";
    public const string EngineVersion = "factor-paydown-projection-v1";

    public FactorPaydownProjectionResult Project(FactorPaydownProjectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var evidence = request.EvidenceLinks
            .Where(static link => !string.IsNullOrWhiteSpace(link))
            .Select(static link => link.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var issues = Validate(request, evidence);
        if (issues.Count > 0)
        {
            return Rejected(issues);
        }

        if (request.CurrentFactor == request.PriorFactor)
        {
            return new FactorPaydownProjectionResult(
                FactorPaydownProjectionStatus.NoChange,
                null,
                null,
                null,
                null);
        }

        decimal rawPrincipal;
        decimal currentFace;
        long nextVersion;
        try
        {
            rawPrincipal = checked(request.HeldFace * (request.PriorFactor - request.CurrentFactor));
            currentFace = checked(request.HeldFace * request.CurrentFactor);
            nextVersion = checked(request.PositionVersion + 1);
        }
        catch (OverflowException)
        {
            return Rejected(new FactorPaydownProjectionIssue(
                "factor-paydown.amount-overflow",
                "The factor-paydown economics exceed the supported decimal or version range."));
        }

        var scale = CurrencyMinorUnits(request.Currency);
        var principal = decimal.Round(rawPrincipal, scale, MidpointRounding.AwayFromZero);
        if (rawPrincipal > 0m && principal <= 0m)
        {
            return Rejected(new FactorPaydownProjectionIssue(
                "factor-paydown.unroundable-currency-result",
                $"The projected principal cannot be represented in {request.Currency.ToUpperInvariant()} minor units."));
        }

        var eventId = DeterministicGuid(BuildIdentity(request));
        var normalizedCurrency = request.Currency.Trim().ToUpperInvariant();
        var normalizedHash = request.SourceContentHash.Trim().ToLowerInvariant();
        var normalizedDomain = request.SourceDomain.Trim();
        var normalizedEntity = request.SourceEntityId.Trim();
        var occurredAtUtc = request.OccurredAtUtc.ToUniversalTime();
        var generatedAtUtc = (request.GeneratedAtUtc ?? request.OccurredAtUtc).ToUniversalTime();

        var economicEvent = new EconomicEventReferenceDto(
            eventId,
            EventType,
            1,
            request.EffectiveDate,
            occurredAtUtc,
            normalizedDomain,
            normalizedEntity,
            request.CorrelationId,
            request.CausationId,
            normalizedHash,
            evidence)
        {
            SecurityId = request.SecurityId,
            BookPositionId = request.PositionId
        };

        var lineage = new ProjectionLineageDto(
            DeterministicGuid($"{eventId:N}|projection-run"),
            DeterministicGuid($"{eventId:N}|projection-event"),
            ModelKey,
            ModelVersion,
            EngineVersion,
            "Base",
            request.EffectiveDate,
            generatedAtUtc,
            normalizedDomain,
            normalizedEntity,
            economicEvent,
            TermsHash: normalizedHash,
            EvidenceLinks: evidence)
        {
            BookPositionId = request.PositionId
        };

        var economicState = new PositionEconomicStateDto(
            DeterministicGuid($"{eventId:N}|economic-state"),
            request.PositionId,
            request.EffectiveDate,
            normalizedCurrency,
            nextVersion,
            ParAmount: request.HeldFace,
            OriginalFaceAmount: request.HeldFace,
            CurrentFaceAmount: decimal.Round(currentFace, scale, MidpointRounding.AwayFromZero),
            PriorFactor: request.PriorFactor,
            CurrentFactor: request.CurrentFactor,
            SourceEvent: economicEvent,
            EvidenceLinks: evidence)
        {
            ProjectionLineage = lineage
        };

        return new FactorPaydownProjectionResult(
            FactorPaydownProjectionStatus.Projected,
            principal,
            economicEvent,
            economicState,
            lineage);
    }

    private static List<FactorPaydownProjectionIssue> Validate(
        FactorPaydownProjectionRequest request,
        IReadOnlyList<string> evidence)
    {
        var issues = new List<FactorPaydownProjectionIssue>();
        AddIf(issues, request.SecurityId == Guid.Empty, "factor-paydown.security-required", "A Security Master identity is required.");
        AddIf(issues, request.PositionId == Guid.Empty, "factor-paydown.position-required", "A book-position identity is required.");
        AddIf(issues, request.PositionVersion <= 0, "factor-paydown.position-version-invalid", "The persisted position version must be positive.");
        AddIf(issues, request.ExpectedPositionVersion != request.PositionVersion, "factor-paydown.position-version-stale", "The expected position version does not match the persisted version.");
        AddIf(issues, request.HeldFace <= 0m, "factor-paydown.held-face-invalid", "Held face must be positive.");
        AddIf(issues, request.PriorFactor is < 0m or > 1m, "factor-paydown.prior-factor-invalid", "Prior factor must be between zero and one.");
        AddIf(issues, request.CurrentFactor is < 0m or > 1m, "factor-paydown.current-factor-invalid", "Current factor must be between zero and one.");
        AddIf(issues, request.CurrentFactor > request.PriorFactor, "factor-paydown.factor-increase", "A factor increase is not a principal-paydown event.");
        AddIf(issues, string.IsNullOrWhiteSpace(request.SourceDomain), "factor-paydown.source-domain-required", "Source domain is required.");
        AddIf(issues, string.IsNullOrWhiteSpace(request.SourceEntityId), "factor-paydown.source-entity-required", "Source entity identity is required.");
        AddIf(issues, string.IsNullOrWhiteSpace(request.SourceContentHash), "factor-paydown.source-hash-required", "A retained source-content hash is required.");
        AddIf(issues, evidence.Count == 0, "factor-paydown.evidence-required", "Retained factor evidence is required.");
        AddIf(issues, !IsSupportedCurrency(request.Currency), "factor-paydown.currency-invalid", "Currency must be a three-letter ISO-style code.");
        return issues;
    }

    private static void AddIf(
        ICollection<FactorPaydownProjectionIssue> issues,
        bool condition,
        string code,
        string message)
    {
        if (condition)
        {
            issues.Add(new FactorPaydownProjectionIssue(code, message));
        }
    }

    private static FactorPaydownProjectionResult Rejected(params FactorPaydownProjectionIssue[] issues)
        => Rejected((IReadOnlyList<FactorPaydownProjectionIssue>)issues);

    private static FactorPaydownProjectionResult Rejected(IReadOnlyList<FactorPaydownProjectionIssue> issues)
        => new(FactorPaydownProjectionStatus.Rejected, null, null, null, null, issues);

    private static string BuildIdentity(FactorPaydownProjectionRequest request)
        => string.Join(
            '|',
            request.SecurityId.ToString("N"),
            request.PositionId.ToString("N"),
            request.EffectiveDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            request.PriorFactor.ToString("G29", CultureInfo.InvariantCulture),
            request.CurrentFactor.ToString("G29", CultureInfo.InvariantCulture),
            request.SourceContentHash.Trim().ToLowerInvariant());

    private static Guid DeterministicGuid(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        Span<byte> bytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(bytes);
        bytes[6] = (byte)((bytes[6] & 0x0f) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3f) | 0x80);
        return new Guid(bytes);
    }

    private static bool IsSupportedCurrency(string currency)
        => !string.IsNullOrWhiteSpace(currency) &&
           currency.Trim().Length == 3 &&
           currency.Trim().All(static character => character is >= 'A' and <= 'Z' or >= 'a' and <= 'z');

    private static int CurrencyMinorUnits(string currency)
        => currency.Trim().ToUpperInvariant() switch
        {
            "BHD" or "IQD" or "JOD" or "KWD" or "LYD" or "OMR" or "TND" => 3,
            "BIF" or "CLP" or "DJF" or "GNF" or "ISK" or "JPY" or "KMF" or "KRW" or "PYG" or "RWF" or "UGX" or "UYI" or "VND" or "VUV" or "XAF" or "XOF" or "XPF" => 0,
            _ => 2
        };
}
