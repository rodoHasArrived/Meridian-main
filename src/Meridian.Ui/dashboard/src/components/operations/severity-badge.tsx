// Meridian severity badge — now the design-system StatusBadge.
//
// The operator status chip (mono, uppercase, alpha wash plus semantic border, mirroring the
// desktop `.severity-badge`) lives in `@/design-system/status` as the one status component.
// This module keeps the historical `SeverityBadge` name so existing call sites keep working.
export { StatusBadge as SeverityBadge, type StatusBadgeProps as SeverityBadgeProps } from "@/design-system/status";
