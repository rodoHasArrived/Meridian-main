/**
 * Compatibility barrel for dashboard-native adapters over the vendored
 * Meridian Design System vocabulary.
 *
 * The implementation is split per primitive so Button, Badge, Status, Masthead,
 * TrustStrip, and NavRail can evolve independently. Existing public imports keep
 * using `@/design-system/primitives` while new component work can import the
 * narrower module directly when that lowers churn.
 */
export { DesignSystemButton, type DesignSystemButtonProps } from "@/design-system/button";
export { DesignSystemBadge, type DesignSystemBadgeProps } from "@/design-system/badge";
export {
  DESIGN_SYSTEM_SEVERITIES,
  DesignSystemStatus,
  normalizeDesignSystemSeverity,
  type DesignSystemSeverity,
  type DesignSystemStatusProps
} from "@/design-system/status";
export {
  DesignSystemMasthead,
  type DesignSystemMastheadCommandTrigger,
  type DesignSystemMastheadProps
} from "@/design-system/masthead";
export { DesignSystemTrustStrip } from "@/design-system/trust-strip";
export {
  DesignSystemNavRail,
  designSystemNavRailClasses,
  type DesignSystemNavRailProps
} from "@/design-system/nav-rail";
export { DESIGN_SYSTEM_SHELL_TOKEN_CONTRACT } from "@/design-system/tokens";
