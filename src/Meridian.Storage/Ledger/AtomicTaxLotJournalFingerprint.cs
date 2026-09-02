using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Text;
using Meridian.Ledger;

namespace Meridian.Storage.Ledger;

/// <summary>
/// Produces the stable identity for an atomic journal/tax-lot command. The supplied fingerprint is
/// deliberately excluded, so <see cref="AtomicTaxLotJournalCommand.WithComputedFingerprint"/> can
/// never recursively hash an already populated value.
/// </summary>
public static class AtomicTaxLotJournalFingerprint
{
    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

    public static string Compute(AtomicTaxLotJournalCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var canonicalJournal = AccountingPostingCommandValidator.NormalizeAndValidate(
            command.Journal,
            requirePostingCommand: false,
            requireExpectedVersion: false);

        // Version 2 records the acquisition lot's par conventions (original face, booked factor,
        // par basis). LedgerTaxLotRecord is serialized whole below, so those fields entered the
        // digest the moment they were added to the record and every acquisition fingerprint changed;
        // the bump makes that break explicit and greppable rather than silent. Two acquisitions that
        // differ only in the factor their face was booked at are different commands and must not
        // share an idempotency identity.
        var payload = new FingerprintPayload(
            FingerprintVersion: 2,
            command.MutationBatchId,
            command.LedgerBookId,
            canonicalJournal,
            command.SourceEventId,
            TextPrimitives.NormalizeOptional(command.IdempotencyKey),
            command.ExpectedPeriodVersion,
            command.MutationKind,
            OrderEvidence(command.RetainedEvidence),
            command.AcquisitionLot,
            OrderSelections(command.DisposalSelections),
            command.CorrectsMutationBatchId,
            TextPrimitives.NormalizeOptional(command.ReliefMethod),
            TextPrimitives.NormalizeOptional(command.PolicyRevision));

        var element = JsonSerializer.SerializeToElement(payload, SerializerOptions);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            WriteCanonicalJson(writer, element);
        }

        return $"sha256:{Sha256Digest.Compute(stream.ToArray())}";
    }

    internal static bool IsCanonicalSha256(string? fingerprint)
    {
        if (fingerprint is null || fingerprint.Length != 71 ||
            !fingerprint.StartsWith("sha256:", StringComparison.Ordinal))
        {
            return false;
        }

        for (var index = 7; index < fingerprint.Length; index++)
        {
            var character = fingerprint[index];
            if (!char.IsAsciiDigit(character) && character is not (>= 'a' and <= 'f'))
            {
                return false;
            }
        }

        return true;
    }

    private static IReadOnlyList<RetainedEvidenceIdentityDto> OrderEvidence(
        IReadOnlyList<RetainedEvidenceIdentityDto>? evidence)
        => (evidence ?? [])
            .OrderBy(static item => item.EvidenceId, StringComparer.Ordinal)
            .ThenBy(static item => item.EvidenceVersion)
            .ThenBy(static item => item.ContentHashSha256, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<LedgerTaxLotDisposalSelection> OrderSelections(
        IReadOnlyList<LedgerTaxLotDisposalSelection>? selections)
        => (selections ?? [])
            .OrderBy(static item => item.SelectionOrdinal)
            .ThenBy(static item => item.TaxLotRecordId)
            .ThenBy(static item => item.LotId, StringComparer.Ordinal)
            .ToArray();

    private static void WriteCanonicalJson(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element
                             .EnumerateObject()
                             .OrderBy(static property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonicalJson(writer, property.Value);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteCanonicalJson(writer, item);
                }

                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(element.GetRawText(), skipInputValidation: true);
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
            default:
                throw new LedgerValidationException(
                    $"Atomic tax-lot command contains unsupported JSON value kind '{element.ValueKind}'.");
        }
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private sealed record FingerprintPayload(
        int FingerprintVersion,
        Guid MutationBatchId,
        Guid LedgerBookId,
        LedgerJournalEntryWrite Journal,
        Guid SourceEventId,
        string? IdempotencyKey,
        long ExpectedPeriodVersion,
        AtomicTaxLotMutationKind MutationKind,
        IReadOnlyList<RetainedEvidenceIdentityDto> RetainedEvidence,
        LedgerTaxLotRecord? AcquisitionLot,
        IReadOnlyList<LedgerTaxLotDisposalSelection> DisposalSelections,
        Guid? CorrectsMutationBatchId,
        string? ReliefMethod,
        string? PolicyRevision);
}
