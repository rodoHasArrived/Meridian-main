using System.Runtime.CompilerServices;

// Keep existing assembly-qualified contract identities resolvable after moving their
// declarations to Contracts. The legacy namespaces remain a deprecated compatibility surface.
#pragma warning disable CS0618
[assembly: TypeForwardedTo(typeof(Meridian.Application.SecurityMaster.ISecurityResolver))]
[assembly: TypeForwardedTo(typeof(Meridian.Application.SecurityMaster.CorporateActions.ILivePositionCorporateActionAdjuster))]
[assembly: TypeForwardedTo(typeof(Meridian.Application.SecurityMaster.CorporateActions.PositionCorporateActionAdjustment))]
#pragma warning restore CS0618
