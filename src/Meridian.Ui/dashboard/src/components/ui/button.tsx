import { DesignSystemButton, type DesignSystemButtonProps } from "@/design-system/primitives";

/**
 * Primary interactive element for user actions.
 *
 * Public re-export of the dashboard design-system {@link DesignSystemButton} adapter so
 * screens keep importing `@/components/ui/button`. All behavior — `asChild`, `busy`,
 * `busyLabel`, `disabledReason`, variants, and sizes — is owned by the adapter.
 *
 * @example
 * <Button size="sm" variant="outline" busy={isLoading} busyLabel="Saving…">
 *   Save
 * </Button>
 */
export type ButtonProps = DesignSystemButtonProps;

export const Button = DesignSystemButton;
