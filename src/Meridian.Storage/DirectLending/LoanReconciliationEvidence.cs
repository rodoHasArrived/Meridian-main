using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.DirectLending;

namespace Meridian.Storage.DirectLending;

internal sealed record LoanReconciliationRunEvidence(
    string SchemaVersion, string? RequestHash, DateOnly? AsOfDate, decimal? AmountTolerance, int? DateToleranceDays);

internal sealed record LoanReconciliationResultEvidence(
    string SchemaVersion, string? MeasurementKind, string? Currency, DateOnly? AsOfDate,
    Guid? LedgerBookId, long? ServicingRevision, Guid? PaymentAllocationId, IReadOnlyList<Guid> JournalEntryIds);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(LoanReconciliationRunEvidence))]
[JsonSerializable(typeof(LoanReconciliationResultEvidence))]
internal partial class LoanReconciliationEvidenceJsonContext : JsonSerializerContext
{
}

internal static class LoanReconciliationEvidence
{
    private const string Version = "meridian.loan-reconciliation.v1";

    internal static string Serialize(ReconciliationRunDto run) => JsonSerializer.Serialize(
        new LoanReconciliationRunEvidence(Version, run.RequestHash, run.AsOfDate, run.AmountTolerance, run.DateToleranceDays),
        LoanReconciliationEvidenceJsonContext.Default.LoanReconciliationRunEvidence);

    internal static string Serialize(ReconciliationResultDto result) => JsonSerializer.Serialize(
        new LoanReconciliationResultEvidence(Version, result.MeasurementKind, result.Currency, result.AsOfDate,
            result.LedgerBookId, result.ServicingRevision, result.PaymentAllocationId, result.JournalEntryIds),
        LoanReconciliationEvidenceJsonContext.Default.LoanReconciliationResultEvidence);

    internal static ReconciliationRunDto Restore(ReconciliationRunDto run, string? json)
    {
        if (json is null) return run;
        var evidence = JsonSerializer.Deserialize(json, LoanReconciliationEvidenceJsonContext.Default.LoanReconciliationRunEvidence)
            ?? throw new InvalidDataException("Loan reconciliation run evidence is null.");
        if (evidence.SchemaVersion != Version) throw new InvalidDataException("Unsupported loan reconciliation run evidence version.");
        return run with
        {
            RequestHash = evidence.RequestHash, AsOfDate = evidence.AsOfDate,
            AmountTolerance = evidence.AmountTolerance, DateToleranceDays = evidence.DateToleranceDays
        };
    }

    internal static ReconciliationResultDto Restore(ReconciliationResultDto result, string? json)
    {
        if (json is null) return result;
        var evidence = JsonSerializer.Deserialize(json, LoanReconciliationEvidenceJsonContext.Default.LoanReconciliationResultEvidence)
            ?? throw new InvalidDataException("Loan reconciliation result evidence is null.");
        if (evidence.SchemaVersion != Version) throw new InvalidDataException("Unsupported loan reconciliation result evidence version.");
        return result with
        {
            MeasurementKind = evidence.MeasurementKind, Currency = evidence.Currency, AsOfDate = evidence.AsOfDate,
            LedgerBookId = evidence.LedgerBookId, ServicingRevision = evidence.ServicingRevision,
            PaymentAllocationId = evidence.PaymentAllocationId, JournalEntryIds = evidence.JournalEntryIds ?? []
        };
    }

    internal static bool SameRequest(ReconciliationRunDto committed, ReconciliationRunDto proposed) =>
        string.Equals(committed.RequestHash, proposed.RequestHash, StringComparison.Ordinal)
        // Explicit exact requests are compatible with pre-evidence runs, which always matched exactly.
        || (committed.RequestHash is null && proposed.AmountTolerance == 0m && proposed.DateToleranceDays == 0);
}
