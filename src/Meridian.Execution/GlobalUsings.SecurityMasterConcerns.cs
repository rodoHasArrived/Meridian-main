// Re-exports the SecurityMaster concern sub-namespaces so existing consumers that reference
// Meridian.Application.SecurityMaster keep resolving the rebuild, corporate-action, and
// validation types that were regrouped into these sub-namespaces, without per-file using churn.
global using Meridian.Application.SecurityMaster.CorporateActions;
global using Meridian.Application.SecurityMaster.Rebuild;
global using Meridian.Application.SecurityMaster.Validation;
