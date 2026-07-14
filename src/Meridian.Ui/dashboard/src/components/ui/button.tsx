<<<<<<< HEAD
import {
  DesignSystemButton,
  type DesignSystemButtonProps
} from "@/design-system/primitives";

export type ButtonProps = DesignSystemButtonProps;
=======
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

>>>>>>> bc00bfd6a5c542ab8d7e5f96a4f054e5239c4708
export const Button = DesignSystemButton;
