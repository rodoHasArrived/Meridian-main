using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Banking;
using Meridian.Contracts.Integrity;

namespace Meridian.Ledger;

/// <summary>Retains the exact call/commitment authority through draft review and posting.</summary>
public static class PrivateCapitalPostingAuthority
{
    public const string MetadataTag = "private-capital.authority";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Serialize(PrivateCapitalPostingAuthorityDto authority)
        => JsonSerializer.Serialize(authority, JsonOptions);

    public static string BankTransactionHash(BankTransactionDto source)
    {
        // PostgreSQL numeric retains its storage scale and timestamps retain microseconds.
        // Hash economic values, not their incidental decimal scale or timezone rendering.
        var recordedAt = source.RecordedAt.ToUniversalTime();
        recordedAt = recordedAt.AddTicks(-(recordedAt.Ticks % 10));
        return Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(new
        {
            source.BankTransactionId, source.EntityId, source.TransactionType, source.EffectiveDate,
            source.TransactionDate, source.SettlementDate,
            Amount = source.Amount.ToString("G29", System.Globalization.CultureInfo.InvariantCulture),
            source.Currency, source.ExternalRef, RecordedAt = recordedAt, source.IsVoided,
            source.RecordedBy, source.PendingPaymentId, source.EvidenceId, source.CanonicalInputHash
        }, JsonOptions));
    }

    public static bool MatchesBankTransaction(PrivateCapitalPostingAuthorityDto authority, BankTransactionDto source)
        => Guid.TryParse(authority.Scope.EntityId, out var entityId) && source.EntityId == entityId
           && Guid.TryParse(authority.BankLineId, out var sourceId) && source.BankTransactionId == sourceId
           && !source.IsVoided && source.Amount == authority.Amount && source.Amount > 0m
           && source.TransactionType == "CapitalCallFunding"
           && source.Currency == authority.Scope.Currency && source.EffectiveDate == authority.EffectiveDate
           && authority.BankTransactionHash == BankTransactionHash(source);

    public static PrivateCapitalPostingAuthorityDto Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 32768)
            throw new InvalidOperationException("Approved capital-call authority is missing or exceeds its retention limit.");
        try
        {
            return JsonSerializer.Deserialize<PrivateCapitalPostingAuthorityDto>(json, JsonOptions)
                ?? throw new InvalidOperationException("Approved capital-call authority is unavailable.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("Approved capital-call authority is invalid.", exception);
        }
    }

    public static bool IsCapitalCall(string? idempotencyKey, string? fundEventType)
        => string.Equals(fundEventType, "CapitalCall", StringComparison.OrdinalIgnoreCase)
           || idempotencyKey?.StartsWith("capital-call:", StringComparison.OrdinalIgnoreCase) == true
           || idempotencyKey?.StartsWith("capital-call-funding:", StringComparison.OrdinalIgnoreCase) == true;

    public static AutomatedJournalDraft Bind(
        AutomatedJournalDraft draft,
        PrivateCapitalPostingAuthorityDto authority,
        string investorId,
        string capitalAccountId)
    {
        var tags = new Dictionary<string, string>(draft.Metadata.Tags ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase)
        {
            [MetadataTag] = Serialize(authority)
        };
        var lines = draft.Lines.Select(line =>
        {
            var dimensions = line.dimensions ?? new LedgerLineDimensionSet();
            var external = new Dictionary<string, string>(dimensions.ExternalGlDimensions, StringComparer.OrdinalIgnoreCase)
            {
                ["capitalCallId"] = authority.CallId,
                ["commitmentId"] = authority.CommitmentId,
                ["commitmentRevision"] = authority.CommitmentRevision.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["capitalActivityKind"] = authority.Kind == "Issuance" ? "CapitalCallIssuance" : "CapitalCallFunding"
            };
            return (line.account, line.debit, line.credit, (LedgerLineDimensionSet?)(dimensions with
            {
                EntityId = authority.Scope.EntityId,
                FundId = authority.Scope.FundProfileId,
                InvestorId = investorId,
                CapitalAccountId = capitalAccountId,
                ExternalGlDimensions = external
            }));
        }).ToArray();
        return draft with { Metadata = draft.Metadata with { Tags = tags }, Lines = lines };
    }

    public static string? ValidateDraft(ManualJournalEntryDraftDto draft)
    {
        if (draft.PrivateCapitalAuthorityJson is null)
            return "Capital-call issuance and funding require an approved retained commitment and call plan.";
        PrivateCapitalPostingAuthorityDto authority;
        try { authority = Deserialize(draft.PrivateCapitalAuthorityJson); }
        catch (InvalidOperationException exception) { return exception.Message; }
        var scope = authority.Scope;
        if (scope is null || !Same(scope.TenantId, draft.TenantId) || !Same(scope.CompanyId, draft.CompanyId)
            || !Same(scope.EntityId, draft.EntityId) || !Same(scope.FundProfileId, draft.FundProfileId)
            || scope.LedgerBookId != draft.LedgerBookId || !Same(scope.Currency, draft.Currency)
            || authority.EffectiveDate != draft.AccountingDate || authority.Amount <= 0m
            || draft.Lines.Count != 2 || draft.Lines.Any(line => line.Amount != authority.Amount
                || !Same(line.Currency, scope.Currency)))
            return "Capital-call draft no longer matches its retained scope, date or amount; recreate it from the call plan.";
        if (draft.Lines.Any(line => line.Dimensions is null || !Same(line.Dimensions.EntityId, scope.EntityId)
            || !Same(line.Dimensions.FundId, scope.FundProfileId)
            || !Same(line.Dimensions.InvestorId, draft.TreasuryContext?.InvestorId)
            || !Same(line.Dimensions.CapitalAccountId, draft.TreasuryContext?.CapitalAccountId)
            || !Same(line.Dimensions.ExternalGlDimensions.GetValueOrDefault("capitalCallId"), authority.CallId)
            || !Same(line.Dimensions.ExternalGlDimensions.GetValueOrDefault("commitmentId"), authority.CommitmentId)
            || line.Dimensions.ExternalGlDimensions.GetValueOrDefault("commitmentRevision") != authority.CommitmentRevision.ToString(System.Globalization.CultureInfo.InvariantCulture)
            || line.Dimensions.ExternalGlDimensions.GetValueOrDefault("capitalActivityKind") != (authority.Kind == "Issuance" ? "CapitalCallIssuance" : "CapitalCallFunding")))
            return "Capital-call draft dimensions no longer match the retained call authority.";
        if (authority.Kind is not ("Issuance" or "Funding")
            || authority.Kind == "Funding" && (string.IsNullOrWhiteSpace(authority.FundingReceiptId)
                || string.IsNullOrWhiteSpace(authority.BankLineId)))
            return "Capital-call funding requires stable receipt and retained bank-line identities.";
        return null;
    }

    internal static bool Same(string? first, string? second)
        => !string.IsNullOrWhiteSpace(first) && string.Equals(first.Trim(), second?.Trim(), StringComparison.OrdinalIgnoreCase);
}
