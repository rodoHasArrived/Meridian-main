namespace Meridian.Application.SecurityMaster;

/// <summary>Compatibility contract for hosts using the former Application-owned resolver.</summary>
[Obsolete("Use Meridian.Contracts.SecurityMaster.ISecurityResolver.")]
public interface ISecurityResolver : Meridian.Contracts.SecurityMaster.ISecurityResolver
{
}
