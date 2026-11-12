using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>A sourced operational cutoff. This is separate from corporate-action economics.</summary>
public sealed record CorporateActionElectionDeadlineDto(
    Guid SeriesId,
    string Kind,
    string SourceParty,
    DateOnly? Date = null,
    [property: JsonConverter(typeof(CorporateActionDeadlineInstantConverter))] DateTimeOffset? At = null,
    string? TimeZoneId = null,
    string? FinancialAccountId = null,
    string? CustodyAccountId = null,
    Guid? SupersedesEvidenceId = null,
    bool Withdrawn = false,
    string? SourceText = null);

public sealed record CorporateActionDeadlineEvidenceDto(int SchemaVersion, CorporateActionElectionDeadlineDto Deadline);

public sealed record RecordCorporateActionDeadlineRequestDto(
    Guid CaseId,
    long ExpectedVersion,
    string IdempotencyKey,
    string TenantId,
    string CompanyId,
    CorporateActionElectionDeadlineDto Deadline,
    string EvidenceReference,
    string EvidenceHash,
    string Rationale,
    CorporateActionCaseScopeDto? ScopeAssertion = null);

public sealed record CorporateActionDeadlineHistoryDto(
    Guid EvidenceId,
    CorporateActionElectionDeadlineDto Deadline,
    string EvidenceReference,
    string? EvidenceHash,
    string? Rationale,
    long CaseVersion,
    string RecordedBy,
    DateTimeOffset RecordedAtUtc,
    bool IsCurrent,
    string EvidenceAssurance = "OperatorRecorded");

public sealed record CorporateActionCaseDeadlinesDto(
    CorporateActionProcessingCaseDto Case,
    IReadOnlyList<CorporateActionDeadlineHistoryDto> History,
    bool CanRecord,
    string? Blocker);

public sealed record CorporateActionCalendarCaseReferenceDto(
    Guid CaseId, long Version, string State, CorporateActionCaseScopeDto Scope);

public sealed record CorporateActionCalendarEventDto(
    string EventId,
    Guid SecurityId,
    string Ticker,
    string ActionType,
    Guid ProposalId,
    string DateKind,
    DateOnly Date,
    DateTimeOffset? At,
    IReadOnlyList<CorporateActionCalendarCaseReferenceDto> Cases,
    CorporateActionProviderEventIdentityDto Source,
    string ProposalState,
    CorporateActionDeadlineHistoryDto? Deadline = null);

public sealed record CorporateActionCalendarPageDto(
    DateOnly From,
    DateOnly To,
    DateTimeOffset GeneratedAtUtc,
    string Revision,
    IReadOnlyList<CorporateActionCalendarEventDto> Items,
    string? NextCursor,
    int TotalCount,
    string Coverage);

public sealed record CorporateActionDeadlineImportRequestDto(
    Guid CaseId,
    long ExpectedVersion,
    string IdempotencyKey,
    string TenantId,
    string CompanyId,
    string Csv,
    string? PreviewHash = null,
    CorporateActionCaseScopeDto? ScopeAssertion = null);

public sealed record CorporateActionDeadlineImportRowDto(int Row, CorporateActionElectionDeadlineDto Deadline,
    string EvidenceReference, string EvidenceHash, string Rationale);
public sealed record CorporateActionDeadlineImportPreviewDto(string PreviewHash, long ExpectedVersion,
    IReadOnlyList<CorporateActionDeadlineImportRowDto> Rows, IReadOnlyList<string> Errors);
public sealed record CorporateActionDeadlineImportResultDto(int CommittedCount, bool Complete,
    IReadOnlyList<CorporateActionEvidenceMutationResultDto> Results, int? FailedRow, string? Error);

/// <summary>One validator is shared by direct capture, CSV preview, import, and persistence.</summary>
public static class CorporateActionDeadlinePolicy
{
    public const string EvidenceKind = "ElectionDeadline";
    public const string CsvHeader = "seriesId,kind,sourceParty,date,at,timeZoneId,financialAccountId,custodyAccountId,evidenceReference,evidenceHash,rationale,supersedesEvidenceId,withdrawn,sourceText";
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string? Validate(CorporateActionElectionDeadlineDto? value)
    {
        if (value is null || value.SeriesId == Guid.Empty)
            return "A stable deadline series ID is required.";
        if (value.Kind is not ("IssuerElection" or "BrokerElection" or "CustodianElection"))
            return "Select an issuer, broker, or custodian election deadline.";
        if (string.IsNullOrWhiteSpace(value.SourceParty))
            return "The source party is required.";
        if (value.SourceParty.Length > 256)
            return "The source party must be at most 256 characters.";
        if (value.Date.HasValue && value.At.HasValue)
            return "Supply a date-only deadline or an offset timestamp, never both.";
        if (value.Withdrawn)
        {
            if (value.SupersedesEvidenceId is null)
                return "Withdrawal must identify the current deadline evidence.";
        }
        else if (!value.Date.HasValue && !value.At.HasValue)
            return "Supply exactly one date-only deadline or timestamp with an explicit offset.";
        if (value.SupersedesEvidenceId == Guid.Empty)
            return "The superseded evidence ID must be a UUID.";
        if (value.Kind == "BrokerElection" && string.IsNullOrWhiteSpace(value.FinancialAccountId) && string.IsNullOrWhiteSpace(value.CustodyAccountId))
            return "A broker cutoff requires its financial or custody account identity.";
        if (value.Kind == "CustodianElection" && string.IsNullOrWhiteSpace(value.CustodyAccountId))
            return "A custodian cutoff requires its custody account identity.";
        if (value.TimeZoneId is { Length: > 128 })
            return "The source timezone is too long.";
        if (!string.IsNullOrWhiteSpace(value.TimeZoneId))
        {
            try
            {
                var zone = TimeZoneInfo.FindSystemTimeZoneById(value.TimeZoneId);
                if (value.At is { } at && zone.GetUtcOffset(at) != at.Offset)
                    return "The source timezone and timestamp offset disagree.";
            }
            catch (TimeZoneNotFoundException) { return "The source timezone is unknown; use an IANA or supported Windows timezone ID."; }
            catch (InvalidTimeZoneException) { return "The source timezone is invalid."; }
        }
        return null;
    }

    public static string? ValidateEvidence(string? reference, string? hash, string? rationale)
    {
        if (!CorporateActionEvidenceKinds.IsTrustedReference(reference))
            return "Use a typed evidence URI such as document://, vault://, or https://.";
        if (hash is null || hash.Length != 64 || hash.Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            return "A lowercase SHA-256 evidence digest is required.";
        if (string.IsNullOrWhiteSpace(rationale))
            return "A source-review rationale is required.";
        return null;
    }

    public static string? ValidateCapture(CorporateActionElectionDeadlineDto? value, string? reference, string? hash, string? rationale)
        => Validate(value) ?? ValidateEvidence(reference, hash, rationale)
            ?? (string.IsNullOrWhiteSpace(value?.SourceText) ? "Retain the original source text stating this deadline." : null)
            ?? (value?.SourceText is { Length: > 8192 } ? "Original source text must be at most 8192 characters." : null);

    public static string? ValidateApplicability(CorporateActionCaseScopeDto scope, CorporateActionElectionDeadlineDto value)
    {
        if (!string.IsNullOrWhiteSpace(scope.FinancialAccountId) && value.Kind != "IssuerElection" && value.FinancialAccountId != scope.FinancialAccountId)
            return "The deadline financial account must match the case's exact account scope.";
        if (!string.IsNullOrWhiteSpace(scope.CustodyAccountId) && value.Kind != "IssuerElection" && value.CustodyAccountId != scope.CustodyAccountId)
            return "The deadline custody account must match the case's exact custody scope.";
        return null;
    }

    public static string? ValidateAppend(CorporateActionElectionDeadlineDto value,
        Guid? currentEvidenceId, CorporateActionElectionDeadlineDto? current)
    {
        if (current is null && value.SupersedesEvidenceId is not null || current is not null && value.SupersedesEvidenceId != currentEvidenceId)
            return "The deadline changed or its predecessor does not belong to this case/series; refresh its history.";
        if (current is not null && (current.Kind != value.Kind || current.SourceParty != value.SourceParty ||
            current.FinancialAccountId != value.FinancialAccountId || current.CustodyAccountId != value.CustodyAccountId))
            return "Correction preserves deadline kind, source party, and account applicability. Withdraw the old series and create another to change them.";
        return null;
    }

    public static CorporateActionElectionDeadlineDto Read(JsonElement? metadata)
    {
        try
        {
            var envelope = metadata?.Deserialize<CorporateActionDeadlineEvidenceDto>(JsonOptions);
            if (envelope is null || envelope.SchemaVersion != 1)
                throw new JsonException();
            if (Validate(envelope.Deadline) is { } error)
                throw new CorporateActionValidationException(error);
            return envelope.Deadline;
        }
        catch (JsonException) { throw new CorporateActionValidationException("Deadline evidence must use the typed version-1 deadline contract."); }
    }

    public static bool TryParseInstant(string value, out DateTimeOffset at)
    {
        at = default;
        // DateTimeOffset.TryParse otherwise guesses the host timezone for an offset-free string.
        return value.Length >= 20 && (value.EndsWith('Z') || value.Length >= 6 &&
            value[^6] is '+' or '-' && value[^3] == ':') &&
            DateTimeOffset.TryParseExact(value, ["yyyy-MM-dd'T'HH:mm:sszzz", "yyyy-MM-dd'T'HH:mmzzz", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz", "yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'"],
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out at);
    }
}

public sealed class CorporateActionDeadlineInstantConverter : JsonConverter<DateTimeOffset?>
{
    public override DateTimeOffset? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;
        if (reader.TokenType == JsonTokenType.String && CorporateActionDeadlinePolicy.TryParseInstant(reader.GetString()!, out var value))
            return value;
        throw new JsonException("A deadline timestamp requires an ISO timestamp with an explicit UTC offset.");
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset? value, JsonSerializerOptions options)
    {
        if (value is { } at)
            writer.WriteStringValue(at.ToString("O", CultureInfo.InvariantCulture));
        else
            writer.WriteNullValue();
    }
}
