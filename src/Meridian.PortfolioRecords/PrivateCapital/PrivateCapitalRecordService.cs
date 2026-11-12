using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Meridian.Contracts.PrivateCapital;
using Meridian.Storage.FundAccounts;

namespace Meridian.PortfolioRecords.PrivateCapital;

/// <summary>Host-owned source access checks, separated from pure financial-record validation.</summary>
public interface IPrivateCapitalRecordAuthority
{
    Task<PrivateCapitalChoices> ChoicesAsync(PrivateCapitalScope scope, CancellationToken ct);
    Task<PrivateCapitalVehicle> ValidateRegisterAsync(PrivateCapitalScope scope, Guid entityId, Guid securityId, string currency, CancellationToken ct);
    Task ValidateEvidenceAsync(PrivateCapitalScope scope, PrivateCapitalEvidence evidence, CancellationToken ct);
}

/// <summary>
/// Retains source-reported observations only. No journal, payment, call-capacity or consolidated
/// balance-sheet authority is granted by accepting these records.
/// </summary>
public sealed class PrivateCapitalRecordService(
    IPrivateCapitalRecordStore store, IPrivateCapitalRecordAuthority authority, TimeProvider? timeProvider = null)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public Task<PrivateCapitalChoices> ChoicesAsync(PrivateCapitalScope scope, CancellationToken ct = default)
    {
        PrivateCapitalScopeValidation.Require(scope);
        return authority.ChoicesAsync(scope, ct);
    }

    public async Task<PrivateCapitalRecords> ReadAsync(PrivateCapitalScope scope, CancellationToken ct = default)
    {
        var records = await store.ReadAsync(scope, ct).ConfigureAwait(false);
        return new(Read<PrivateCapitalCommitment>(records, "commitment"),
            Read<PrivateCapitalSnapshot>(records, "snapshot"), _time.GetUtcNow());
    }

    public async Task<PrivateCapitalImportInspection?> InspectImportAsync(PrivateCapitalScope scope, Guid batchId, CancellationToken ct = default)
    {
        var records = await store.ReadAsync(scope, ct).ConfigureAwait(false);
        var preview = Find<PrivateCapitalImportPreview>(records, "preview", batchId);
        if (preview is null) return null;
        var input = Find<PreviewPrivateCapitalImport>(records, "source-input", batchId)
            ?? throw new InvalidDataException("The retained preview has no exact source input.");
        return new(preview, input, Read<AcceptReceipt>(records, "accept-receipt")
            .SingleOrDefault(item => item.Receipt.BatchId == batchId)?.Receipt);
    }

    public async Task<PrivateCapitalCommitment> CreateAsync(PrivateCapitalScope scope, string actor,
        CreatePrivateCapitalCommitment request, CancellationToken ct = default)
    {
        RequireActor(actor);
        if (request.CommandId == Guid.Empty || request.HolderEntityId == Guid.Empty || request.VehicleSecurityId == Guid.Empty)
            throw new ArgumentException("Command, holder entity and vehicle security identities are required.");
        var currency = request.Currency?.Trim().ToUpperInvariant() ?? "";
        if (!Regex.IsMatch(currency, "^[A-Z]{3}$")) throw new ArgumentException("Supply a three-letter commitment currency.");
        var normalized = request with { Currency = currency, SourceReference = RequiredText(request.SourceReference, "Source reference", 512), Evidence = NormalizeEvidence(request.Evidence) };
        var fingerprint = Hash(normalized);
        var prior = await store.ReadAsync(scope, ct).ConfigureAwait(false);
        if (Find<CreateReceipt>(prior, "create-receipt", request.CommandId) is { } old)
            return ResolveCreateReplay(prior, old, fingerprint);
        var vehicle = await authority.ValidateRegisterAsync(scope, request.HolderEntityId, request.VehicleSecurityId, currency, ct).ConfigureAwait(false);
        await authority.ValidateEvidenceAsync(scope, normalized.Evidence, ct).ConfigureAwait(false);
        PrivateCapitalCommitment? result = null;
        await store.AppendAsync(scope, records =>
        {
            if (Find<CreateReceipt>(records, "create-receipt", request.CommandId) is { } replay)
            {
                result = ResolveCreateReplay(records, replay, fingerprint);
                return [];
            }
            result = new(Guid.NewGuid(), request.HolderEntityId, request.VehicleSecurityId, vehicle.Name,
                vehicle.Strategy, vehicle.VintageYear, currency, normalized.SourceReference,
                normalized.Evidence, _time.GetUtcNow(), actor, vehicle.Version);
            return [Pack("commitment", result.CommitmentId, result),
                Pack("create-receipt", request.CommandId, new CreateReceipt(fingerprint, result.CommitmentId))];
        }, ct).ConfigureAwait(false);
        return result!;
    }

    public async Task<PrivateCapitalImportPreview> PreviewAsync(PrivateCapitalScope scope, string actor,
        PreviewPrivateCapitalImport request, CancellationToken ct = default)
    {
        RequireActor(actor);
        var evidence = NormalizeEvidence(request.Evidence);
        var rationale = RequiredText(request.Rationale, "Review rationale", 2000);
        if ((request.Rows is not null) == (request.Csv is not null))
            throw new ArgumentException("Supply exactly one of rows or CSV.");
        var supplied = request.Rows ?? PrivateCapitalCsv.Parse(request.Csv!);
        if (supplied.Count is < 1 or > 500) throw new ArgumentException("Supply between one and 500 observations.");
        var rows = supplied.Select(NormalizeRow).ToArray();
        if (rows.Select(row => (row.CommitmentId, row.AsOfDate)).Distinct().Count() != rows.Length)
            throw new ArgumentException("A commitment may appear only once for each as-of date in a batch.");
        if (rows.Where(row => row.SupersedesSnapshotId.HasValue).Select(row => row.SupersedesSnapshotId).Distinct().Count()
            != rows.Count(row => row.SupersedesSnapshotId.HasValue))
            throw new ArgumentException("Each corrected observation may be superseded only once.");
        await authority.ValidateEvidenceAsync(scope, evidence, ct).ConfigureAwait(false);
        var retained = await store.ReadAsync(scope, ct).ConfigureAwait(false);
        await ValidateSourcesAsync(scope, retained, rows, ct).ConfigureAwait(false);
        ValidateRowsAgainstHistory(retained, rows);
        var fingerprint = Hash(new { Rows = rows, Evidence = evidence, Rationale = rationale, SourceInputHash = Hash(request) });
        var preview = new PrivateCapitalImportPreview(Guid.NewGuid(), fingerprint, rows, evidence, rationale, _time.GetUtcNow(), actor);
        await store.AppendAsync(scope, records =>
        {
            ValidateRowsAgainstHistory(records, rows);
            return [Pack("preview", preview.BatchId, preview), Pack("source-input", preview.BatchId, request)];
        }, ct).ConfigureAwait(false);
        return preview;
    }

    public async Task<PrivateCapitalImportReceipt> AcceptAsync(PrivateCapitalScope scope, string actor,
        AcceptPrivateCapitalImport request, CancellationToken ct = default)
    {
        RequireActor(actor);
        if (request.CommandId == Guid.Empty || request.BatchId == Guid.Empty) throw new ArgumentException("Batch and command identities are required.");
        var retained = await store.ReadAsync(scope, ct).ConfigureAwait(false);
        if (Find<AcceptReceipt>(retained, "accept-receipt", request.CommandId) is { } old)
            return ResolveAcceptReplay(old, request);
        var preview = Find<PrivateCapitalImportPreview>(retained, "preview", request.BatchId)
            ?? throw new InvalidOperationException("The retained preview is unavailable in this scope.");
        if (!string.Equals(preview.Fingerprint, request.Fingerprint, StringComparison.Ordinal))
            throw new InvalidOperationException("The reviewed preview fingerprint does not match.");
        await authority.ValidateEvidenceAsync(scope, preview.Evidence, ct).ConfigureAwait(false);
        await ValidateSourcesAsync(scope, retained, preview.Rows, ct).ConfigureAwait(false);
        PrivateCapitalImportReceipt? result = null;
        await store.AppendAsync(scope, records =>
        {
            if (Find<AcceptReceipt>(records, "accept-receipt", request.CommandId) is { } replay)
            {
                result = ResolveAcceptReplay(replay, request);
                return [];
            }
            // One accepted batch cannot be submitted again under a new command id.
            if (Read<AcceptReceipt>(records, "accept-receipt").Any(receipt => receipt.Receipt.BatchId == preview.BatchId))
                throw new InvalidOperationException("This preview was already accepted; inspect its retained outcome.");
            ValidateRowsAgainstHistory(records, preview.Rows);
            var now = _time.GetUtcNow();
            var snapshots = preview.Rows.Select(row => new PrivateCapitalSnapshot(Guid.NewGuid(), row.CommitmentId,
                row.AsOfDate, row.NavValuationDate, row.CommitmentAmount, row.CalledAmount, row.UnfundedAmount,
                row.CumulativeDistributions, row.NavAmount, row.SourceReference, preview.Evidence,
                row.SupersedesSnapshotId, now, actor, preview.Rationale, preview.BatchId)).ToArray();
            result = new(request.CommandId, preview.BatchId, snapshots.Select(row => row.SnapshotId).ToArray(), now);
            return snapshots.Select(row => Pack("snapshot", row.SnapshotId, row))
                .Append(Pack("accept-receipt", request.CommandId, new AcceptReceipt(request.Fingerprint, result))).ToArray();
        }, ct).ConfigureAwait(false);
        return result!;
    }

    private async Task ValidateSourcesAsync(PrivateCapitalScope scope, IReadOnlyList<PrivateCapitalStoredRecord> records,
        IReadOnlyList<PrivateCapitalSnapshotInput> rows, CancellationToken ct)
    {
        foreach (var id in rows.Select(row => row.CommitmentId).Distinct())
        {
            var commitment = Find<PrivateCapitalCommitment>(records, "commitment", id)
                ?? throw new ArgumentException("An observation references an unavailable commitment.");
            await authority.ValidateRegisterAsync(scope, commitment.HolderEntityId, commitment.VehicleSecurityId, commitment.Currency, ct).ConfigureAwait(false);
        }
    }

    private static void ValidateRowsAgainstHistory(IReadOnlyList<PrivateCapitalStoredRecord> records, IReadOnlyList<PrivateCapitalSnapshotInput> rows)
    {
        var all = Read<PrivateCapitalSnapshot>(records, "snapshot");
        var superseded = all.Where(row => row.SupersedesSnapshotId.HasValue).Select(row => row.SupersedesSnapshotId!.Value).ToHashSet();
        var current = all.Where(row => !superseded.Contains(row.SnapshotId)).ToArray();
        foreach (var row in rows)
        {
            if (Find<PrivateCapitalCommitment>(records, "commitment", row.CommitmentId) is null)
                throw new ArgumentException("Commitment identity does not exist in this scope.");
            if (row.SupersedesSnapshotId is Guid previous)
            {
                var old = current.SingleOrDefault(item => item.SnapshotId == previous);
                if (old is null || old.CommitmentId != row.CommitmentId)
                    throw new InvalidOperationException("Correction must reference the current observation for this commitment.");
            }
            if (current.Any(item => item.CommitmentId == row.CommitmentId && item.AsOfDate == row.AsOfDate && item.SnapshotId != row.SupersedesSnapshotId))
                throw new InvalidOperationException("An observation already exists for this date; select its exact identity for correction.");
        }
    }

    private static PrivateCapitalSnapshotInput NormalizeRow(PrivateCapitalSnapshotInput row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (row.CommitmentId == Guid.Empty) throw new ArgumentException("A stable commitment identity is required.");
        var asOf = Date(row.AsOfDate, "As-of date");
        var valuation = string.IsNullOrWhiteSpace(row.NavValuationDate) ? null : Date(row.NavValuationDate, "NAV valuation date");
        if (valuation is not null && string.CompareOrdinal(valuation, asOf) > 0)
            throw new ArgumentException("NAV valuation date cannot follow the observation's as-of date.");
        var normalized = row with
        {
            AsOfDate = asOf, NavValuationDate = valuation,
            CommitmentAmount = Amount(row.CommitmentAmount, "Commitment"),
            CalledAmount = Amount(row.CalledAmount, "Called capital"),
            UnfundedAmount = Amount(row.UnfundedAmount, "Unfunded commitment"),
            CumulativeDistributions = Amount(row.CumulativeDistributions, "Cumulative distributions"),
            NavAmount = Amount(row.NavAmount, "NAV", signed: true),
            SourceReference = RequiredText(row.SourceReference, "Source reference", 512)
        };
        if (normalized.NavAmount is not null && valuation is null) throw new ArgumentException("A reported NAV requires its valuation date.");
        if (new[] { normalized.CommitmentAmount, normalized.CalledAmount, normalized.UnfundedAmount, normalized.CumulativeDistributions, normalized.NavAmount }.All(value => value is null))
            throw new ArgumentException("An observation must contain at least one source-reported amount.");
        return normalized;
    }

    private static string? Amount(string? text, string field, bool signed = false)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = text.Trim();
        if (text.Length > 80 || !Regex.IsMatch(text, "^-?(0|[1-9][0-9]*)(\\.[0-9]{1,28})?$") ||
            !decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
            throw new ArgumentException($"{field} must be representable exact decimal text, without exponents or separators.");
        if (!signed && value < 0m) throw new ArgumentException($"{field} is outside its permitted range.");
        var canonical = text.Contains('.') ? text.TrimEnd('0').TrimEnd('.') : text;
        if (canonical == "-0") canonical = "0";
        var parsed = value.ToString("0.############################", CultureInfo.InvariantCulture);
        if (parsed != canonical) throw new ArgumentException($"{field} exceeds exact decimal precision; rounding is not permitted.");
        return parsed;
    }

    private static string Date(string? text, string field)
    {
        if (!DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var value))
            throw new ArgumentException($"{field} must be an ISO calendar date.");
        return value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static PrivateCapitalEvidence NormalizeEvidence(PrivateCapitalEvidence? evidence)
    {
        if (evidence is null || string.IsNullOrWhiteSpace(evidence.VaultId) || string.IsNullOrWhiteSpace(evidence.DocumentId) ||
            !Regex.IsMatch(evidence.ContentHash ?? "", "^[a-fA-F0-9]{64}$"))
            throw new ArgumentException("A retained vault/document identity and SHA-256 source content hash are required.");
        return evidence with { VaultId = evidence.VaultId.Trim(), DocumentId = evidence.DocumentId.Trim(), ContentHash = evidence.ContentHash!.ToLowerInvariant() };
    }

    private static string RequiredText(string? value, string name, int maximum)
    {
        value = value?.Trim();
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximum) throw new ArgumentException($"{name} is required and must be at most {maximum} characters.");
        return value;
    }

    private static void RequireActor(string actor)
    {
        if (string.IsNullOrWhiteSpace(actor)) throw new UnauthorizedAccessException("An authenticated operator is required.");
    }

    private static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, Json)))).ToLowerInvariant();
    private static PrivateCapitalStoredRecord Pack<T>(string kind, Guid id, T value) => new(kind, id, JsonSerializer.Serialize(value, Json));
    private static T? Find<T>(IReadOnlyList<PrivateCapitalStoredRecord> rows, string kind, Guid id) where T : class
        => rows.FirstOrDefault(row => row.Kind == kind && row.Id == id) is { } found ? JsonSerializer.Deserialize<T>(found.Payload, Json) : null;
    private static IReadOnlyList<T> Read<T>(IReadOnlyList<PrivateCapitalStoredRecord> rows, string kind)
        => rows.Where(row => row.Kind == kind).Select(row => JsonSerializer.Deserialize<T>(row.Payload, Json)
            ?? throw new InvalidDataException("A retained private-capital record is invalid.")).ToArray();

    private static PrivateCapitalCommitment ResolveCreateReplay(IReadOnlyList<PrivateCapitalStoredRecord> rows, CreateReceipt receipt, string fingerprint)
    {
        if (receipt.Fingerprint != fingerprint) throw new InvalidOperationException("This command identity was used with different register inputs.");
        return Find<PrivateCapitalCommitment>(rows, "commitment", receipt.CommitmentId) ?? throw new InvalidDataException("The retained create receipt has no register entry.");
    }

    private static PrivateCapitalImportReceipt ResolveAcceptReplay(AcceptReceipt retained, AcceptPrivateCapitalImport request)
    {
        if (retained.Fingerprint != request.Fingerprint || retained.Receipt.BatchId != request.BatchId)
            throw new InvalidOperationException("This command identity was used with a different reviewed import.");
        return retained.Receipt;
    }

    private sealed record CreateReceipt(string Fingerprint, Guid CommitmentId);
    private sealed record AcceptReceipt(string Fingerprint, PrivateCapitalImportReceipt Receipt);
}
