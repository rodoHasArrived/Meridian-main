namespace Meridian.Application.SecurityMaster;

/// <summary>
/// An approved field-edit revision whose canonical field changed after the proposal was drafted.
/// Publishing it would overwrite a newer value the reviewer never saw, so publication refuses it.
/// The revision stays Approved, and retrying will not help. Discard it and stage the edit again
/// against the current value. The workbench publish route answers 409.
/// </summary>
public sealed class StaleFieldProposalException : InvalidOperationException
{
    public StaleFieldProposalException(Guid securityId, Guid revisionId, string fieldPath, long baseVersion, long currentVersion)
        : base($"Revision '{revisionId:D}' proposes '{fieldPath}' against security '{securityId:D}' version {baseVersion}, "
            + $"but that field changed by version {currentVersion}. Publishing it would overwrite the newer value; "
            + "discard the revision and stage the edit again against the current value.")
    {
        SecurityId = securityId;
        RevisionId = revisionId;
        FieldPath = fieldPath;
        BaseVersion = baseVersion;
        CurrentVersion = currentVersion;
    }

    public Guid SecurityId { get; }

    public Guid RevisionId { get; }

    public string FieldPath { get; }

    public long BaseVersion { get; }

    public long CurrentVersion { get; }
}
