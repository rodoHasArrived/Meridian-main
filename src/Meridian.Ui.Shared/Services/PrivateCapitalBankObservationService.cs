using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Banking;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Storage.Banking;
using Meridian.Ui.Shared.Evidence;

namespace Meridian.Ui.Shared.Services;

/// <summary>Imports reviewed settled bank observations without creating a payment or posting cash.</summary>
public sealed class PrivateCapitalBankObservationService(
    IEvidenceArtifactStore vault, IBankingStore store, TimeProvider? clock = null)
{
    private const int MaxSourceBytes = 64 * 1024;
    private static readonly JsonSerializerOptions SourceJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public async Task<PrivateCapitalBankObservationImportResultDto> ImportAsync(
        PrivateCapitalScopeDto scope, ImportPrivateCapitalBankObservationRequest request, string actor,
        CancellationToken ct = default)
    {
        scope = PrivateCapitalAuthorityRules.NormalizeScope(scope);
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(actor)) throw new ArgumentException("Authenticated import actor is required.");
        if (!EvidenceVaultReference.TryParseCanonical(request.EvidenceLink, out var vaultId))
            throw new ArgumentException("Select a canonical retained Evidence Vault source.");
        if (string.IsNullOrWhiteSpace(request.DocumentId) || request.DocumentId.Length > 200 || request.DocumentId.Any(char.IsControl))
            throw new ArgumentException("An exact retained document identity is required.");
        var documentId = request.DocumentId.Trim();
        var identity = await vault.TryGetVaultIdentityAsync(vaultId, scope.TenantId, scope.CompanyId, ct).ConfigureAwait(false)
            ?? throw Blocked("The bank observation source was not found in the current tenant/company.");
        if (identity.VaultId != vaultId || !ScopeMatches(identity.TenantId, scope.TenantId) || !ScopeMatches(identity.Scope, scope.CompanyId))
            throw Blocked("The bank observation source belongs to another tenant/company.");
        var documents = identity.Documents.Where(x => x.DocumentId == documentId).ToArray();
        if (documents.Length != 1) throw Blocked("The exact bank observation document is unavailable or ambiguous.");
        var document = documents[0];
        var now = _clock.GetUtcNow();
        if (!ScopeMatches(document.TenantId, scope.TenantId) || !ScopeMatches(document.Scope, scope.CompanyId)
            || document.Classification is not (EvidenceDocumentClassificationDto.BankEvidence or EvidenceDocumentClassificationDto.BankStatement)
            || document.ReviewerState.Status != EvidenceDocumentReviewStatusDto.Accepted
            || string.IsNullOrWhiteSpace(document.ReviewerState.Reviewer)
            || document.ReviewerState.ReviewedAt is not { } reviewedAt || reviewedAt > now)
            throw Blocked("The bank observation document requires accepted review in the current scope.");
        if (!await vault.VerifyRetainedContentAsync(vaultId, scope.TenantId, scope.CompanyId, ct).ConfigureAwait(false))
            throw Blocked("The retained bank observation bytes could not be verified.");
        var file = await vault.TryOpenDocumentContentAsync(vaultId, documentId, scope.TenantId, scope.CompanyId, ct).ConfigureAwait(false)
            ?? throw Blocked("The exact bank observation source bytes are unavailable.");
        byte[] bytes;
        await using (var content = file.Content)
        {
            var buffer = new byte[MaxSourceBytes + 1];
            var length = 0;
            while (length < buffer.Length)
            {
                var read = await content.ReadAsync(buffer.AsMemory(length), ct).ConfigureAwait(false);
                if (read == 0) break;
                length += read;
            }
            if (length == 0 || length > MaxSourceBytes)
                throw new ArgumentException("Bank observation JSON must contain between 1 and 65536 bytes.");
            bytes = buffer.AsSpan(0, length).ToArray();
        }
        var sourceHash = Sha256Digest.Compute(bytes);
        if (!Sha256Digest.FixedEquals(sourceHash, document.SourceHashSha256))
            throw Blocked("The bank observation content does not match its retained document hash.");
        PrivateCapitalBankObservationSourceDto source;
        try
        {
            using var parsed = JsonDocument.Parse(bytes);
            if (parsed.RootElement.ValueKind != JsonValueKind.Object)
                throw new JsonException("A single observation object is required.");
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in parsed.RootElement.EnumerateObject())
                if (!names.Add(field.Name)) throw new JsonException("Duplicate observation fields are not supported.");
            ValidateRawAmount(parsed.RootElement);
            source = JsonSerializer.Deserialize<PrivateCapitalBankObservationSourceDto>(bytes, SourceJson)
                ?? throw new JsonException("The observation is empty.");
        }
        catch (JsonException ex)
        {
            throw new ArgumentException("The retained source must contain one complete, unambiguous bank observation JSON object.", ex);
        }
        ValidateSource(scope, source, now);
        if (source.ObservedAtUtc > identity.RetainedAt || identity.RetainedAt > now)
            throw Blocked("The bank observation time cannot follow its trusted retained-source timestamp.");
        var evidenceLink = EvidenceVaultReference.CanonicalPrefix + vaultId;
        var binding = JsonSerializer.Serialize(new
        {
            kind = "private-capital-bank-observation-v1",
            tenantId = scope.TenantId,
            companyId = scope.CompanyId,
            statementReference = source.StatementReference.Trim(),
            evidenceLink,
            documentId,
            sourceHashSha256 = sourceHash
        });
        // PostgreSQL timestamptz retains microseconds. Source bytes retain the original timestamp;
        // canonicalize the observation once so exact replay survives the database round trip.
        var recordedAt = new DateTimeOffset(source.ObservedAtUtc.Ticks - source.ObservedAtUtc.Ticks % 10, TimeSpan.Zero);
        var transaction = new BankTransactionDto(source.BankTransactionId, source.EntityId, "CapitalCallFunding",
            source.EffectiveDate, source.TransactionDate, source.SettlementDate, source.Amount, scope.Currency,
            binding, recordedAt, false, document.ReviewerState.Reviewer.Trim());
        var existing = await FindAsync(source.BankTransactionId, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            RequireExactReplay(existing, transaction);
            return new(existing, evidenceLink, documentId, sourceHash, true);
        }
        await store.InsertBankTransactionAsync(transaction, ct).ConfigureAwait(false);
        var retained = await FindAsync(source.BankTransactionId, ct).ConfigureAwait(false)
            ?? throw Blocked("The bank observation was not durably retained; retry the same document.");
        RequireExactReplay(retained, transaction);
        return new(retained, evidenceLink, documentId, sourceHash, false);
    }

    private static void ValidateSource(PrivateCapitalScopeDto scope, PrivateCapitalBankObservationSourceDto source, DateTimeOffset now)
    {
        if (source.BankTransactionId == Guid.Empty || !Guid.TryParse(scope.EntityId, out var entityId) || source.EntityId != entityId)
            throw Blocked("The bank observation must identify this book's legal entity and a stable bank transaction.");
        if (source.Amount <= 0m || source.Currency != scope.Currency || source.Direction != "Credit")
            throw Blocked("Only positive incoming Credit observations in the book currency are supported.");
        if (source.Amount > 999_999_999_999_999.9999m || decimal.Round(source.Amount, 4) != source.Amount)
            throw Blocked("The observation amount must be exactly representable by the bank store's numeric(19,4) contract.");
        if (string.IsNullOrWhiteSpace(source.StatementReference) || source.StatementReference.Length > 500 || source.StatementReference.Any(char.IsControl))
            throw new ArgumentException("The bank observation requires a nonempty statement reference of at most 500 characters.");
        if (source.EffectiveDate == default || source.TransactionDate == default || source.SettlementDate == default
            || source.EffectiveDate != source.SettlementDate || source.TransactionDate > source.SettlementDate
            || source.SettlementDate > DateOnly.FromDateTime(now.UtcDateTime)
            || source.ObservedAtUtc.Offset != TimeSpan.Zero || source.ObservedAtUtc > now
            || source.ObservedAtUtc < new DateTimeOffset(source.SettlementDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero))
            throw Blocked("The bank observation must be settled, with valid dates and a retained UTC observation time.");
    }

    private static void ValidateRawAmount(JsonElement observation)
    {
        if (!observation.TryGetProperty("amount", out var amount))
            throw new JsonException("A source amount is required.");
        var text = amount.ValueKind switch
        {
            JsonValueKind.Number => amount.GetRawText(),
            JsonValueKind.String => amount.GetString(),
            _ => null
        };
        const string message = "The bank observation amount must use a plain positive decimal without signs, exponents or separators, exactly representable as numeric(19,4).";
        if (string.IsNullOrEmpty(text)) throw Blocked(message);
        var parts = text.Split('.');
        if (parts.Length > 2 || parts.Any(part => part.Length == 0 || part.Any(ch => ch is < '0' or > '9')))
            throw Blocked(message);
        // Validate the source token before decimal deserialization can round excess significant
        // digits away. Leading integer zeros and trailing fractional zeros do not change value.
        var integer = parts[0].TrimStart('0');
        var fraction = parts.Length == 2 ? parts[1].TrimEnd('0') : string.Empty;
        if (integer.Length > 15 || fraction.Length > 4 || integer.Length == 0 && fraction.Length == 0)
            throw Blocked(message);
    }

    private async Task<BankTransactionDto?> FindAsync(Guid id, CancellationToken ct)
    {
        // The bank transaction identity is global. Check foreign-entity collisions without returning their data.
        var matches = (await store.GetBankTransactionsAsync(null, ct).ConfigureAwait(false))
            .Where(x => x.BankTransactionId == id).ToArray();
        if (matches.Length > 1) throw Blocked("Retained bank transaction identity is ambiguous.");
        return matches.SingleOrDefault();
    }

    private static void RequireExactReplay(BankTransactionDto retained, BankTransactionDto expected)
    {
        if (retained != expected)
            throw Blocked("This bank transaction identity already retains different economic or source evidence. Import a reviewed successor source instead.");
    }

    private static bool ScopeMatches(string? retained, string expected)
        => string.Equals(retained, expected, StringComparison.OrdinalIgnoreCase);

    private static InvalidOperationException Blocked(string message) => new(message);
}
