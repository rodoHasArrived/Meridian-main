import {
  DesignSystemMasthead,
  DesignSystemTrustStrip,
  type DesignSystemMastheadCommandTrigger,
  type DesignSystemMastheadProps
} from "@/design-system/primitives";

export type WorkstationTopbarCommandTrigger = DesignSystemMastheadCommandTrigger;
export type WorkstationTopbarProps = DesignSystemMastheadProps;

/**
 * Compatibility wrapper over the dashboard design-system {@link DesignSystemMasthead}
 * adapter. The shell now renders `DesignSystemMasthead` directly; this export stays so the
 * `WorkstationTopbar` live-adapter name (tracked in the design-system manifest bridge)
 * keeps resolving to a dashboard-native component.
 */
export function WorkstationTopbar(props: WorkstationTopbarProps) {
  return <DesignSystemMasthead {...props} />;
}

export const WorkstationTrustStrip = DesignSystemTrustStrip;
