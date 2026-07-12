import React from "react";

export interface DrawerProps {
  /** Drawer open state */
  open: boolean;
  /** Callback when drawer closes */
  onClose: () => void;
  /** Side to anchor (left, right, top, bottom) — default: "right" */
  side?: "left" | "right" | "top" | "bottom";
  /** Drawer title (shown in header) */
  title?: string;
  /** Drawer content */
  children: React.ReactNode;
  /** Width (for left/right) or height (for top/bottom) — default: "360px" */
  size?: string;
  /** Close on backdrop click (default: true) */
  closeOnBackdrop?: boolean;
  /** Close on ESC key (default: true) */
  closeOnEsc?: boolean;
}

export interface DrawerHeaderProps {
  title: string;
  onClose?: () => void;
}

export interface DrawerBodyProps {
  children: React.ReactNode;
}

export const Drawer: React.FC<DrawerProps>;
export const DrawerHeader: React.FC<DrawerHeaderProps>;
export const DrawerBody: React.FC<DrawerBodyProps>;
