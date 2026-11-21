import React from "react";

/**
 * Accessible dialog — the ONE overlay implementation (one scrim, one focus trap, one
 * title-id contract). Confirmations, alerts, and bounded create/edit flows alike: width is
 * the `size` prop, never a second component.
 */
export interface DialogProps {
  /** Dialog open state */
  open: boolean;
  /** Callback when user requests close (backdrop click, ESC key, or close button) */
  onClose: () => void;
  /** Dialog title (shown in header) */
  title?: string;
  /** Dialog content */
  children: React.ReactNode;
  /** Show close button in header (default: true) */
  showClose?: boolean;
  /** Width step — sm 420px (confirm) · md 520px (default) · lg 720px (form takeover) */
  size?: "sm" | "md" | "lg";
  /** Explicit max width, overriding `size` */
  maxWidth?: string;
  /** On ESC key press, call onClose (default: true) */
  closeOnEsc?: boolean;
}

export interface DialogHeaderProps {
  title: string;
  onClose?: () => void;
  showClose?: boolean;
}

export interface DialogBodyProps {
  children: React.ReactNode;
}

export interface DialogFooterProps {
  children: React.ReactNode;
}

/** Bounded create/edit flow — submitting form body with system Button footer actions. */
export interface DialogFormProps {
  open?: boolean;
  onClose?: () => void;
  title?: string;
  onSubmit?: () => void | Promise<void>;
  submitLabel?: string;
  cancelLabel?: string;
  loading?: boolean;
  size?: "sm" | "md" | "lg";
  children: React.ReactNode;
}

export const Dialog: React.FC<DialogProps>;
export const DialogHeader: React.FC<DialogHeaderProps>;
export const DialogBody: React.FC<DialogBodyProps>;
export const DialogFooter: React.FC<DialogFooterProps>;
export const DialogForm: React.FC<DialogFormProps>;
