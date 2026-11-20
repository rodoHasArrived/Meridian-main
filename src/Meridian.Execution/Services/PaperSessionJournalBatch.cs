using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Integrity;
using Meridian.Execution.Events;
using Meridian.Execution.Serialization;
using Meridian.Ledger;

namespace Meridian.Execution.Services;

public enum PaperSessionJournalPurpose { OpeningCapital, Fill, Settlement, Financing }

/// <summary>Balanced simulated postings for one retained economic effect.</summary>
public sealed record PaperSessionJournalBatch(
    int SchemaVersion,
    string SessionId,
    string AccountId,
    string SourceId,
    PaperSessionJournalPurpose Purpose,
    string SourceHash,
    string Currency,
    IReadOnlyList<JournalEntry> Entries,
    PaperFinancingCharge? Financing = null)
{
    public const int CurrentSchemaVersion = 1;

    [JsonIgnore]
    public Guid BatchId => GetId(SessionId, AccountId, SourceId, Purpose);

    internal static Guid GetId(string sessionId, string accountId, string sourceId, PaperSessionJournalPurpose purpose) =>
        PaperFinancingCharge.Identity("paper-session-journal", sessionId, accountId, sourceId, purpose.ToString());

    internal string ComputeHash() => Sha256Digest.ComputeUtf8(
        JsonSerializer.Serialize(this, ExecutionJsonContext.Default.PaperSessionJournalBatch));

    internal void Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion || !Enum.IsDefined(Purpose)
            || string.IsNullOrWhiteSpace(SessionId) || string.IsNullOrWhiteSpace(AccountId)
            || string.IsNullOrWhiteSpace(SourceId) || !Sha256Digest.IsCanonical(SourceHash)
            || string.IsNullOrWhiteSpace(Currency) || Entries is null)
            throw new InvalidDataException("Invalid paper-session journal batch.");

        if (Purpose == PaperSessionJournalPurpose.Financing && Financing is { } charge)
        {
            charge.Validate();
            if (charge.SessionId != SessionId || SourceId != charge.SourceEventId.ToString("D")
                || charge.AccountId != PaperTradingPortfolio.DefaultAccountId
                || !Sha256Digest.FixedEquals(SourceHash, Sha256Digest.ComputeUtf8(
                    JsonSerializer.Serialize(charge, ExecutionJsonContext.Default.PaperFinancingCharge))))
                throw new InvalidDataException("Paper financing evidence conflicts with its journal batch.");
        }
        else if (Purpose == PaperSessionJournalPurpose.Financing || Financing is not null)
        {
            throw new InvalidDataException("The paper-session journal batch does not have the expected financing evidence.");
        }

        var validationLedger = new Meridian.Ledger.Ledger(LedgerPolicy.Trading(Currency));
        foreach (var entry in Entries)
        {
            if (entry.Metadata.LedgerBook != SessionId || entry.Metadata.FinancialAccountId != AccountId)
                throw new InvalidDataException("The journal belongs to a different simulated session or account.");
            validationLedger.Post(entry);
        }
    }
}

/// <summary>The immutable batch and its content digest, retained in one crash-atomic record.</summary>
public sealed record PaperSessionJournalReceipt(PaperSessionJournalBatch Batch, string ContentHash)
{
    internal void Validate(string sessionId)
    {
        if (Batch is null || Batch.SessionId != sessionId
            || !Sha256Digest.FixedEquals(ContentHash, Batch.ComputeHash()))
            throw new InvalidDataException("The retained paper-session journal receipt does not match its content or session.");
        Batch.Validate();
    }
}
