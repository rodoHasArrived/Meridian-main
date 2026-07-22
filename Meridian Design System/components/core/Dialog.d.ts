import React from "react";

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
  /** Max width (default: 520px) */
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

export const Dialog: React.FC<DialogProps>;
export const DialogHeader: React.FC<DialogHeaderProps>;
export const DialogBody: React.FC<DialogBodyProps>;
export const DialogFooter: React.FC<DialogFooterProps>;
