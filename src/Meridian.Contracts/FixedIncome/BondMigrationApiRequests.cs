namespace Meridian.Contracts.FixedIncome;

/// <summary>Stages a reviewed preview through the existing Security Master draft workflow.</summary>
public sealed record BondMigrationSubmitRequest(
    BondMigrationPreviewRequest Request,
    string PreviewSha256,
    string SecurityContentSha256,
    string Justification);

/// <summary>Bounded bulk preview; each row retains its own expected security version.</summary>
public sealed record BondMigrationBatchPreviewRequest(IReadOnlyList<BondMigrationPreviewRequest> Bonds);
