using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Workstation;
using Meridian.Core.Exceptions;

namespace Meridian.Ui.Shared.Evidence;

/// <summary>Supported independently reviewed JSON source; provenance is resolved from the vault.</summary>
public sealed record AccountCashForecastSourceDocument(
    int SchemaVersion, string Kind, string SourceDomain, string SourceEntityId, long EvidenceVersion,
    string AccountId, string BookId, DateOnly EffectiveDate, AccountCashEvidenceRole Role,
    AccountCashSourceBalance? Balance = null, AccountCashSourceFlow? Flow = null, AccountCashSourceFx? Fx = null,
    AccountCashSourceAllocation? Allocation = null, AccountCashSourceReceiptRevision? ReceiptRevision = null,
    AccountCashSourceWatchObservation? WatchObservation = null);
public sealed record AccountCashSourceBalance(DateOnly AsOfDate, decimal Amount, string Currency, bool IsSettled);
public sealed record AccountCashSourceFlow(string FlowId, string VersionId, AccountCashFlowStatus Status,
    string FlowType, decimal Amount, string Currency, DateOnly? EconomicDate, DateOnly? SettlementDate,
    DateOnly? CashReceiptDate, Guid? SecurityId = null, Guid? ProjectionRunId = null,
    string? ProjectionEngineVersion = null, Guid? TermsVersionId = null, string? TermsHash = null,
    string? SupersedesVersionId = null);
public sealed record AccountCashSourceFx(string Currency, string BaseCurrency, DateOnly ConversionDate, decimal BaseUnitsPerCurrencyUnit);
public sealed record AccountCashSourceAllocation(string AllocationId, string ExpectedFlowId, string ActualVersionId,
    decimal NetAmount, decimal GrossAmount, decimal WithholdingTax, decimal Fees);
public sealed record AccountCashSourceReceiptRevision(string ExpectedFlowId, DateOnly ReceiptDate);
public sealed record AccountCashSourceWatchObservation(string ObservationId, Guid SecurityId, string Kind,
    DateOnly EffectiveDate, string Value, string? Agency, string? SupersedesObservationId);

/// <summary>
/// Indicates that cash-forecast source evidence could not satisfy its authority requirements.
/// </summary>
public sealed class AccountCashForecastSourceAuthorityException(string message) : MeridianException(message);

/// <summary>Exact supported source economics and accepted review, scoped to a single account/book.</summary>
public sealed class AccountCashForecastEvidenceAdapter(IEvidenceArtifactStore store)
{
    private static readonly JsonSerializerOptions SourceJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter<AccountCashEvidenceRole>(), new JsonStringEnumConverter<AccountCashFlowStatus>() }
    };
    private static readonly AccountCashForecastSourceJsonContext SourceJson = new(SourceJsonOptions);

    public async Task VerifyMonitoringAsync(BondMonitoringCaptureRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var sourceRequest = new AccountCashForecastRequestDto(request.RunId, request.Scope,
            DateOnly.FromDateTime(request.RecordedAtUtc.UtcDateTime), DateOnly.FromDateTime(request.RecordedAtUtc.UtcDateTime),
            null, null, request.Payments, [], request.RecordedAtUtc)
        {
            CalculationVersion = "account-cash-forecast-v2",
            PaymentAllocations = request.Allocations,
            ReceiptRevisions = request.ReceiptRevisions
        };
        await VerifyAsync(sourceRequest, ct).ConfigureAwait(false);
        foreach (var observation in request.Observations ?? [])
        {
            if (observation is null)
                throw Failure("Watch observations cannot contain null entries.");
            var source = await ReadAsync(observation.Provenance, request.Scope, ct).ConfigureAwait(false);
            var expected = new AccountCashSourceWatchObservation(observation.ObservationId, observation.SecurityId,
                observation.Kind, observation.EffectiveDate, observation.Value, observation.Agency, observation.SupersedesObservationId);
            if (source.SchemaVersion != 2 || source.Kind != "watchObservation" || source.WatchObservation != expected)
                throw Failure("Watch observation differs from independently reviewed source JSON.");
        }
    }

    public async Task VerifyAsync(AccountCashForecastRequestDto request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Scope);
        ArgumentNullException.ThrowIfNull(request.Flows);
        ArgumentNullException.ThrowIfNull(request.FxConversions);
        if (request.Flows.Any(static flow => flow is null) || request.FxConversions.Any(static fx => fx is null))
            throw new ArgumentException("Forecast flow and FX collections cannot contain null entries.", nameof(request));
        if (request.OpeningCash is { } opening)
            await BalanceAsync(opening).ConfigureAwait(false);
        if (request.ClosingCash is { } closing)
            await BalanceAsync(closing).ConfigureAwait(false);
        foreach (var balance in request.NativeOpeningBalances ?? [])
            await BalanceAsync(balance ?? throw Failure("Native opening balances cannot contain null entries.")).ConfigureAwait(false);
        foreach (var balance in request.NativeClosingBalances ?? [])
            await BalanceAsync(balance ?? throw Failure("Native closing balances cannot contain null entries.")).ConfigureAwait(false);
        foreach (var flow in request.Flows)
        {
            var source = await ReadAsync(flow.Provenance, request.Scope, ct).ConfigureAwait(false);
            var expected = new AccountCashSourceFlow(flow.FlowId, flow.VersionId, flow.Status, flow.FlowType,
                flow.Amount, flow.Currency, flow.EconomicDate, flow.SettlementDate, flow.CashReceiptDate,
                flow.SecurityId, flow.ProjectionRunId, flow.ProjectionEngineVersion, flow.TermsVersionId, flow.TermsHash, flow.SupersedesVersionId);
            if (source.Kind != "flow" || source.Flow != expected || source.Balance is not null || source.Fx is not null
                || flow.AccountId != request.Scope.AccountId || flow.BookId != request.Scope.BookId)
                throw Failure("Submitted flow economics differ from the independently reviewed source JSON.");
        }
        foreach (var fx in request.FxConversions)
        {
            var source = await ReadAsync(fx.Provenance, request.Scope, ct).ConfigureAwait(false);
            if (source.Kind != "fx" || source.Fx != new AccountCashSourceFx(fx.Currency, fx.BaseCurrency, fx.ConversionDate, fx.BaseUnitsPerCurrencyUnit)
                || source.Balance is not null || source.Flow is not null)
                throw Failure("Submitted FX direction, date or rate differs from the independently reviewed source JSON.");
        }
        foreach (var allocation in request.PaymentAllocations ?? [])
        {
            if (allocation is null)
                throw Failure("Payment allocations cannot contain null entries.");
            if (allocation.WithholdingTax == 0m && allocation.Fees == 0m && allocation.DeductionEvidence is null)
                continue;
            var source = await ReadAsync(allocation.DeductionEvidence
                ?? throw Failure("Retained deduction evidence is required for taxes and fees."), request.Scope, ct).ConfigureAwait(false);
            var expected = new AccountCashSourceAllocation(allocation.AllocationId, allocation.ExpectedFlowId,
                allocation.ActualVersionId, allocation.NetAmount, allocation.GrossAmount, allocation.WithholdingTax, allocation.Fees);
            if (source.SchemaVersion != 2 || source.Kind != "allocation" || source.Allocation != expected)
                throw Failure("Payment allocation or gross-to-net deductions differ from the independently reviewed source JSON.");
        }
        foreach (var revision in request.ReceiptRevisions ?? [])
        {
            if (revision is null)
                throw Failure("Receipt revisions cannot contain null entries.");
            var source = await ReadAsync(revision.Evidence, request.Scope, ct).ConfigureAwait(false);
            if (source.SchemaVersion != 2 || source.Kind != "receiptRevision"
                || source.ReceiptRevision != new AccountCashSourceReceiptRevision(revision.ExpectedFlowId, revision.ReceiptDate))
                throw Failure("Revised receipt date differs from the independently reviewed source JSON.");
        }

        async Task BalanceAsync(AccountSettledCashObservationDto balance)
        {
            var source = await ReadAsync(balance.Provenance, request.Scope, ct).ConfigureAwait(false);
            if (source.Kind != "balance" || source.Balance != new AccountCashSourceBalance(balance.AsOfDate, balance.Amount, balance.Currency, balance.IsSettled)
                || source.Flow is not null || source.Fx is not null
                || balance.AccountId != request.Scope.AccountId || balance.BookId != request.Scope.BookId)
                throw Failure("Submitted settled balance differs from the independently reviewed source JSON.");
        }
    }

    private async Task<AccountCashForecastSourceDocument> ReadAsync(AccountCashProvenanceDto provenance,
        AccountCashForecastScopeDto scope, CancellationToken ct)
    {
        if (provenance is null || !RetainedEvidenceIdentityValidator.IsComplete(provenance.Evidence)
            || !EvidenceVaultReference.TryParseCanonical(provenance.Evidence.EvidenceUri, out var vaultId))
            throw Failure("A canonical, complete retained Evidence Vault source identity is required.");
        var evidence = provenance.Evidence;
        var identity = await store.TryGetVaultIdentityAsync(vaultId, scope.TenantId, scope.CompanyId, ct).ConfigureAwait(false);
        var documents = identity?.Documents.Where(row => row.DocumentId == evidence.EvidenceId).ToArray();
        if (identity is null || identity.SubjectKind != "account" || identity.SubjectId != scope.AccountId
            || identity.TenantId != scope.TenantId || identity.Scope != scope.CompanyId || documents?.Length != 1)
            throw Failure("Retained source must belong to this exact tenant, company and account subject.");
        var document = documents[0];
        var review = document.ReviewerState;
        if (document.TenantId != scope.TenantId || document.Scope != scope.CompanyId
            || document.SourceSystem != provenance.SourceDomain || document.SourceReference != provenance.SourceEntityId
            || !Sha256Digest.FixedEquals(document.SourceHashSha256, provenance.SourceContentHash)
            || !Sha256Digest.FixedEquals(document.SourceHashSha256, evidence.ContentHashSha256)
            || review.Status != EvidenceDocumentReviewStatusDto.Accepted || review.Reviewer != evidence.ReviewedBy
            || review.ReviewedAt != evidence.ReviewedAtUtc || string.IsNullOrWhiteSpace(document.Actor)
            || string.Equals(review.Reviewer, document.Actor, StringComparison.OrdinalIgnoreCase)
            || evidence.RetainedAtUtc != evidence.ReviewedAtUtc || evidence.RetainedBy != review.Reviewer
            || evidence.SubjectType != "account" || evidence.SubjectId != scope.AccountId
            || !review.ConfirmedFields.Any(field => field.FieldName == "sourceContentHashSha256"
                && Sha256Digest.FixedEquals(field.ConfirmedValue, document.SourceHashSha256)
                && field.ConfirmedBy == review.Reviewer && field.ConfirmedAt == review.ReviewedAt)
            || !document.AuditTrail.Any(item => item.Action == "DocumentReviewRecorded"
                && item.Actor == review.Reviewer && item.RecordedAt == review.ReviewedAt))
            throw Failure("Source hash, independent accepted review and retained review identity must agree.");
        var retained = await store.TryOpenDocumentContentAsync(vaultId, evidence.EvidenceId,
            scope.TenantId, scope.CompanyId, ct).ConfigureAwait(false);
        if (retained is null)
            throw Failure("Exact source bytes are unavailable or failed retained content verification.");
        await using var stream = retained.Content;
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct).ConfigureAwait(false)) != 0)
        {
            if (buffer.Length + read > 1048576)
                throw Failure("Supported account source JSON is limited to one MiB.");
            buffer.Write(chunk, 0, read);
        }
        var bytes = buffer.ToArray();
        if (!Sha256Digest.FixedEquals(Sha256Digest.Compute(bytes), evidence.ContentHashSha256))
            throw Failure("Source bytes differ from their retained identity.");
        AccountCashForecastSourceDocument source;
        try
        {
            using var json = JsonDocument.Parse(bytes);
            RefuseDuplicateProperties(json.RootElement);
            source = JsonSerializer.Deserialize(bytes, SourceJson.AccountCashForecastSourceDocument)
                ?? throw Failure("Source JSON is empty.");
        }
        catch (JsonException) { throw Failure("Source format is unsupported; strict typed account cash JSON is required."); }
        var populatedKinds = (source.Balance is null ? 0 : 1) + (source.Flow is null ? 0 : 1)
            + (source.Fx is null ? 0 : 1) + (source.Allocation is null ? 0 : 1) + (source.ReceiptRevision is null ? 0 : 1)
            + (source.WatchObservation is null ? 0 : 1);
        if (source.SchemaVersion is not (1 or 2) || populatedKinds != 1
            || source.SchemaVersion == 1 && (source.Allocation is not null || source.ReceiptRevision is not null || source.WatchObservation is not null)
            || source.AccountId != scope.AccountId || source.BookId != scope.BookId
            || source.SourceDomain != provenance.SourceDomain || source.SourceEntityId != provenance.SourceEntityId
            || evidence.SourceSystem != source.SourceDomain || evidence.SourceReference != source.SourceEntityId
            || source.EvidenceVersion != evidence.EvidenceVersion || source.EffectiveDate != evidence.EffectiveDate
            || source.Role != provenance.Role)
            throw Failure("Source JSON identity, version, effective date, evidence role and exact account/book must agree.");
        var currentIdentity = await store.TryGetVaultIdentityAsync(vaultId, scope.TenantId, scope.CompanyId, ct).ConfigureAwait(false);
        var current = currentIdentity?.Documents.Where(row => row.DocumentId == evidence.EvidenceId).ToArray();
        if (current?.Length != 1 || JsonSerializer.Serialize(current[0], SourceJson.EvidenceDocumentDto) != JsonSerializer.Serialize(document, SourceJson.EvidenceDocumentDto))
            throw Failure("Source review changed during verification; reload the retained evidence before capture.");
        return source;
    }

    private static void RefuseDuplicateProperties(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw new JsonException("Duplicate source JSON property.");
                RefuseDuplicateProperties(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray())
                RefuseDuplicateProperties(item);
    }

    private static AccountCashForecastSourceAuthorityException Failure(string message) => new(message);
}

[JsonSerializable(typeof(AccountCashForecastSourceDocument))]
[JsonSerializable(typeof(EvidenceDocumentDto))]
internal sealed partial class AccountCashForecastSourceJsonContext : JsonSerializerContext;
