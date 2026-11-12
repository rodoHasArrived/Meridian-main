namespace Meridian.Contracts.PrivateCapital;

/// <summary>Authenticated storage partition. Never accepted from a browser request.</summary>
public sealed record PrivateCapitalScope(string TenantId, string CompanyId);

public sealed record PrivateCapitalEvidence(string VaultId, string DocumentId, string ContentHash);
public sealed record PrivateCapitalChoice(Guid Id, string Label);
public sealed record PrivateCapitalChoices(IReadOnlyList<PrivateCapitalChoice> Entities, IReadOnlyList<PrivateCapitalChoice> Vehicles);
public sealed record PrivateCapitalVehicle(string Name, string Strategy, int? VintageYear, long Version);

public sealed record PrivateCapitalCommitment(
    Guid CommitmentId, Guid HolderEntityId, Guid VehicleSecurityId, string VehicleName,
    string Strategy, int? VintageYear, string Currency, string SourceReference,
    PrivateCapitalEvidence Evidence, DateTimeOffset CreatedAt, string CreatedBy, long VehicleVersion);

public sealed record CreatePrivateCapitalCommitment(
    Guid CommandId, Guid HolderEntityId, Guid VehicleSecurityId, string Currency,
    string SourceReference, PrivateCapitalEvidence Evidence);

/// <summary>Source observations; exact decimal text and missing values survive the browser boundary.</summary>
public sealed record PrivateCapitalSnapshotInput(
    Guid CommitmentId, string AsOfDate, string? NavValuationDate,
    string? CommitmentAmount, string? CalledAmount, string? UnfundedAmount,
    string? CumulativeDistributions, string? NavAmount, string SourceReference,
    Guid? SupersedesSnapshotId = null);

public sealed record PrivateCapitalSnapshot(
    Guid SnapshotId, Guid CommitmentId, string AsOfDate, string? NavValuationDate,
    string? CommitmentAmount, string? CalledAmount, string? UnfundedAmount,
    string? CumulativeDistributions, string? NavAmount, string SourceReference,
    PrivateCapitalEvidence Evidence, Guid? SupersedesSnapshotId,
    DateTimeOffset RecordedAt, string RecordedBy, string Rationale, Guid BatchId);

public sealed record PreviewPrivateCapitalImport(
    IReadOnlyList<PrivateCapitalSnapshotInput>? Rows, string? Csv,
    PrivateCapitalEvidence Evidence, string Rationale);

public sealed record PrivateCapitalImportPreview(
    Guid BatchId, string Fingerprint, IReadOnlyList<PrivateCapitalSnapshotInput> Rows,
    PrivateCapitalEvidence Evidence, string Rationale, DateTimeOffset CreatedAt, string CreatedBy);

public sealed record AcceptPrivateCapitalImport(Guid BatchId, string Fingerprint, Guid CommandId);
public sealed record PrivateCapitalImportReceipt(Guid CommandId, Guid BatchId, IReadOnlyList<Guid> SnapshotIds, DateTimeOffset AcceptedAt);
public sealed record PrivateCapitalImportInspection(PrivateCapitalImportPreview Preview,
    PreviewPrivateCapitalImport SourceInput, PrivateCapitalImportReceipt? Receipt);
public sealed record PrivateCapitalRecords(
    IReadOnlyList<PrivateCapitalCommitment> Commitments, IReadOnlyList<PrivateCapitalSnapshot> Snapshots,
    DateTimeOffset GeneratedAt);
