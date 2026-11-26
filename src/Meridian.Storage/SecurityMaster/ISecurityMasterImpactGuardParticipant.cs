using Meridian.Contracts.SecurityMaster;

namespace Meridian.Storage.SecurityMaster;

/// <summary>Identifies the authoritative database whose rows a review finalization must fence.</summary>
public interface ISecurityMasterImpactGuardParticipant
{
    SecurityMasterOptions ImpactGuardOptions { get; }
}

public sealed partial class PostgresSecurityMasterStore : ISecurityMasterImpactGuardParticipant
{
    public SecurityMasterOptions ImpactGuardOptions => _options;
}
