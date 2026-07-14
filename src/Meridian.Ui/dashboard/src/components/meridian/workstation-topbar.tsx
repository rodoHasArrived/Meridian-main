import {
  DesignSystemMasthead,
  DesignSystemTrustStrip,
  type DesignSystemMastheadCommandTrigger,
  type DesignSystemMastheadProps
} from "@/design-system/primitives";

export type WorkstationTopbarCommandTrigger = DesignSystemMastheadCommandTrigger;
export type WorkstationTopbarProps = DesignSystemMastheadProps;
<<<<<<< HEAD
export const WorkstationTopbar = DesignSystemMasthead;
=======

/**
 * Compatibility wrapper over the dashboard design-system {@link DesignSystemMasthead}
 * adapter. The shell now renders `DesignSystemMasthead` directly; this export stays so the
 * `WorkstationTopbar` live-adapter name (tracked in the design-system manifest bridge)
 * keeps resolving to a dashboard-native component.
 */
export function WorkstationTopbar(props: WorkstationTopbarProps) {
  return <DesignSystemMasthead {...props} />;
}

>>>>>>> bc00bfd6a5c542ab8d7e5f96a4f054e5239c4708
export const WorkstationTrustStrip = DesignSystemTrustStrip;
