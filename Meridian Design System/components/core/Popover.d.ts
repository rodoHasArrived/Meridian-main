import React from "react";

export interface PopoverProps {
  /** Popover open state */
  open: boolean;
  /** Callback when popover should close */
  onClose: () => void;
  /** Anchor element (ref) or element selector */
  anchorEl?: HTMLElement | null;
  /** Position relative to anchor: top, bottom, left, right */
  position?: "top" | "bottom" | "left" | "right";
  /** Offset from anchor (px) — default: 8 */
  offset?: number;
  /** Popover content */
  children: React.ReactNode;
  /** Close on backdrop click (default: true) */
  closeOnBackdrop?: boolean;
  /** Close on ESC key (default: true) */
  closeOnEsc?: boolean;
}

export const Popover: React.FC<PopoverProps>;
