using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;

namespace Meridian.Reporting;

/// <summary>Format-independent verification of a certified report's retained ledger population.</summary>
public static class ReportingRetainedLedgerPopulationValidation
{
    private const string EvidencePrefix = "ledger-population:";
    private const string RetainedCheckpointPrefix = "ledger-checkpoint-v2-";

    public static string BuildCheckpointId(string hash) => $"{RetainedCheckpointPrefix}{hash[..32]}";

    public static void Validate(ReportingAuthoritativeSourceCheckpoint source,
        ImmutableArray<IReadOnlyDictionary<string, string>> certifiedRows)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (string.IsNullOrWhiteSpace(source.CheckpointId))
            throw Invalid("The authoritative source checkpoint identity is missing.");
        var markers = source.EvidenceIds.IsDefault ? [] : source.EvidenceIds
            .Where(id => id?.StartsWith(EvidencePrefix, StringComparison.Ordinal) == true).ToArray();
        if (source.LedgerPopulation is not { } population)
        {
            // Historic checkpoints did not claim to retain a complete journal population.
            if (markers.Length == 0 && !source.CheckpointId.StartsWith(RetainedCheckpointPrefix, StringComparison.Ordinal))
                return;
            throw Invalid("A retained ledger population is missing from its certified evidence binding.");
        }
        if (string.IsNullOrWhiteSpace(population.PayloadJson)
            || !Sha256Digest.IsCanonical(population.ContentHashSha256)
            || !Sha256Digest.FixedEquals(population.ContentHashSha256, Sha256Digest.ComputeUtf8(population.PayloadJson))
            || population.SnapshotId != $"ledger-population-{population.ContentHashSha256[..32]}"
            || markers.Length != 1
            || markers[0] != $"{EvidencePrefix}{population.SnapshotId}:{population.ContentHashSha256}"
            || population.HighestGlobalSequence != source.HighestGlobalSequence
            || population.JournalEntryCount < 0 || population.LedgerLineCount < 0)
            throw Invalid("The retained ledger population digest, identity, counts, or evidence binding is invalid.");

        try
        {
            using var document = JsonDocument.Parse(population.PayloadJson);
            var root = document.RootElement;
            RequireObject(root);
            RejectDuplicateProperties(root);
            var scope = JsonSerializer.Deserialize<LedgerAmountScopeDto>(Required(root, "Scope"))
                ?? throw Invalid("The retained ledger population scope is missing.");
            if (scope.TenantId != source.TenantId || scope.CompanyId != source.CompanyId
                || scope.FundProfileId != source.FundId || scope.LedgerBookId == Guid.Empty || scope.PeriodId == Guid.Empty
                || scope.LedgerBookId.ToString("D") != source.LedgerBookId
                || scope.PeriodId.ToString("D") != source.AccountingPeriodId)
                throw Invalid("The retained ledger population is outside the certified accounting scope.");
            var request = JsonSerializer.Deserialize<LedgerReportPackRequest>(Required(root, "Request"))
                ?? throw Invalid("The retained ledger population request is missing.");
            if (request.FundId != source.FundId || request.PeriodId != source.AccountingPeriodId
                || request.AsOf != source.CutoffUtc)
                throw Invalid("The retained ledger population request does not match its certified scope and cutoff.");
            var rows = ReadRows(Required(root, "DatasetRows"));
            if (certifiedRows.IsDefault || certifiedRows.Any(row => row is null
                    || row.Keys.Any(string.IsNullOrWhiteSpace) || row.Values.Any(value => value is null))
                || rows.Length != source.LedgerLineCount
                || !Sha256Digest.FixedEquals(ReportingCertifiedManifestValidation.ComputeCertifiedRowsHash(rows),
                    ReportingCertifiedManifestValidation.ComputeCertifiedRowsHash(certifiedRows)))
                throw Invalid("The report dataset does not reproduce the retained ledger population rows.");
            var journals = RequiredArray(root, "Journals");
            var sequences = new HashSet<long>();
            var journalIds = new HashSet<Guid>();
            var lineIds = new HashSet<Guid>();
            var lineCount = 0;
            long highestSequence = 0;
            foreach (var record in journals.EnumerateArray())
            {
                var sequence = Required(record, "GlobalSequence").GetInt64();
                var periodId = Required(record, "PeriodId").GetGuid();
                var basis = JsonSerializer.Deserialize<AccountingBasisKindDto>(Required(record, "AccountingBasis"));
                var entry = JsonSerializer.Deserialize<JournalEntry>(Required(record, "Entry"))
                    ?? throw Invalid("A retained journal is missing its entry.");
                if (sequence <= 0 || !sequences.Add(sequence) || periodId == Guid.Empty
                    || basis.ToString() != source.AccountingBasis || !journalIds.Add(entry.JournalEntryId)
                    || entry.JournalEntryId == Guid.Empty || entry.Timestamp > source.CutoffUtc || !entry.IsBalanced)
                    throw Invalid("A retained journal has invalid accounting scope, sequence, identity, or balance.");
                foreach (var line in entry.Lines)
                {
                    if (line.EntryId == Guid.Empty || !lineIds.Add(line.EntryId)
                        || line.Dimensions?.FundId != source.FundId || line.Dimensions?.BookId != source.LedgerBookId
                        || line.Dimensions?.OrganizationId != source.OrganizationId)
                        throw Invalid("A retained journal line is ambiguous or outside the certified accounting scope.");
                }
                lineCount = checked(lineCount + entry.Lines.Count);
                highestSequence = Math.Max(highestSequence, sequence);
            }
            if (journals.GetArrayLength() != population.JournalEntryCount || lineCount != population.LedgerLineCount
                || highestSequence != population.HighestGlobalSequence)
                throw Invalid("Retained journal population counts do not match their certified declaration.");
            RequiredArray(root, "TaxLotReliefProjections");
            RequiredArray(root, "ReportAmounts");
            var periodVersion = Required(root, "PeriodVersion").GetInt64();
            if (!Sha256Digest.FixedEquals(source.CheckpointHash,
                    ComputeCheckpointHash(source, scope.LedgerBookId, scope.PeriodId, periodVersion, rows))
                || source.CheckpointId != BuildCheckpointId(source.CheckpointHash))
                throw Invalid("The retained population does not reproduce its certified source checkpoint digest.");
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException
            or InvalidOperationException or FormatException or OverflowException or LedgerValidationException)
        {
            throw new InvalidDataException("The retained ledger population is malformed or cannot be verified.", exception);
        }
    }

    public static string ComputeCheckpointHash(ReportingAuthoritativeSourceCheckpoint source,
        Guid ledgerBookId, Guid periodId, long periodVersion,
        ImmutableArray<IReadOnlyDictionary<string, string>> rows)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("sourceKind", source.SourceKind);
            writer.WriteString("tenantId", source.TenantId);
            writer.WriteString("organizationId", source.OrganizationId);
            writer.WriteString("companyId", source.CompanyId);
            writer.WriteString("fundId", source.FundId);
            writer.WriteString("ledgerBookId", ledgerBookId);
            writer.WriteString("accountingPeriodId", periodId);
            writer.WriteString("accountingBasis", source.AccountingBasis);
            writer.WriteString("asOfDate", source.AsOfDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            writer.WriteString("cutoffUtc", source.CutoffUtc.ToString("O", CultureInfo.InvariantCulture));
            writer.WriteNumber("periodVersion", periodVersion);
            writer.WriteNumber("highestGlobalSequence", source.HighestGlobalSequence);
            writer.WriteString("ledgerPopulationHash", source.LedgerPopulation!.ContentHashSha256);
            writer.WriteStartArray("rows");
            foreach (var row in rows)
            {
                writer.WriteStartObject();
                foreach (var pair in row.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                    writer.WriteString(pair.Key, pair.Value);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return Sha256Digest.Compute(stream.ToArray());
    }

    private static ImmutableArray<IReadOnlyDictionary<string, string>> ReadRows(JsonElement element)
    {
        RequireArray(element);
        var rows = ImmutableArray.CreateBuilder<IReadOnlyDictionary<string, string>>();
        foreach (var row in element.EnumerateArray())
        {
            RequireObject(row);
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in row.EnumerateObject())
            {
                if (string.IsNullOrWhiteSpace(property.Name) || property.Value.ValueKind != JsonValueKind.String)
                    throw Invalid("The retained ledger dataset has invalid field names or values.");
                values.Add(property.Name, property.Value.GetString()!);
            }
            rows.Add(values);
        }
        return rows.ToImmutable();
    }

    private static JsonElement Required(JsonElement element, string name)
    {
        RequireObject(element);
        return element.TryGetProperty(name, out var value) ? value
            : throw Invalid($"The retained ledger population is missing '{name}'.");
    }

    private static JsonElement RequiredArray(JsonElement element, string name)
    {
        var value = Required(element, name);
        RequireArray(value);
        return value;
    }

    private static void RequireObject(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw Invalid("The retained ledger population contains an invalid object.");
    }

    private static void RequireArray(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array)
            throw Invalid("The retained ledger population contains an invalid array.");
    }

    private static void RejectDuplicateProperties(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw Invalid("The retained ledger population contains duplicate JSON properties.");
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var child in value.EnumerateArray())
                RejectDuplicateProperties(child);
    }

    private static InvalidDataException Invalid(string message) => new(message);
}
