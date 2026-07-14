import { DesignSystemBadge, type DesignSystemBadgeProps } from "@/design-system/primitives";

/**
 * Compact status label in font-mono uppercase, used for environment state, data posture,
 * and event classification throughout the operator workstation.
 *
 * Public re-export of the dashboard design-system {@link DesignSystemBadge} adapter so
 * screens keep importing `@/components/ui/badge`. Variants — `default`, `outline`,
 * `success`, `warning`, `danger`, `paper`, `live`, `research` — and the `dot` indicator are
 * owned by the adapter.
 *
 * @example
 * <Badge variant="live" dot>LIVE</Badge>
 */
export type BadgeProps = DesignSystemBadgeProps;
<<<<<<< Updated upstream
<<<<<<< HEAD
=======

>>>>>>> 8facbd635d8f4063eabf809bf413de18c7345b25
=======
>>>>>>> Stashed changes
export const Badge = DesignSystemBadge;
