/**
 * ContextMenu — right-click menu for table rows and UI elements.
 *
 * Supports icons, dividers, disabled items, and danger (red) styling.
 * Smart positioning: auto-adjusts to avoid overflow off-screen.
 *
 * @example
 * const [onContextMenu, isOpen, closeMenu] = useContextMenu();
 * <div onContextMenu={onContextMenu}>Right-click me</div>
 * {isOpen && (
 *   <ContextMenu
 *     items={[
 *       { label: 'Edit', icon: '✎', onClick: handleEdit },
 *       { label: 'Copy', icon: '⎘', onClick: handleCopy },
 *       { type: 'divider' },
 *       { label: 'Delete', icon: '🗑️', dangerous: true, onClick: handleDelete }
 *     ]}
 *     onClose={closeMenu}
 *   />
 * )}
 */

export interface ContextMenuItem {
  /** Display label */
  label?: string;

  /** Icon emoji or symbol */
  icon?: string;

  /** Fired when item is clicked */
  onClick?: () => void;

  /** Disable the item (grayed out, not clickable) */
  disabled?: boolean;

  /** Color the item red (destructive actions) */
  dangerous?: boolean;

  /** Show a › indicator (for submenu affordance) */
  submenuIcon?: boolean;
}

export interface ContextMenuDivider {
  type: "divider";
}

export interface ContextMenuProps {
  /** Menu items and dividers */
  items: (ContextMenuItem | ContextMenuDivider)[];

  /** Fired when the menu is closed (click overlay or item selected) */
  onClose?: () => void;
}

export declare function ContextMenu(props: ContextMenuProps): JSX.Element | null;

/**
 * useContextMenu — hook to attach context menu to an element.
 *
 * @example
 * const [onContextMenu, isOpen, closeMenu] = useContextMenu();
 * <div onContextMenu={onContextMenu}>Right-click me</div>
 * {isOpen && <ContextMenu items={[...]} onClose={closeMenu} />}
 *
 * @returns Tuple: [onContextMenu handler, isOpen boolean, closeMenu function, position object]
 */
export declare function useContextMenu(): [
  onContextMenu: (e: React.MouseEvent) => void,
  isOpen: boolean,
  closeMenu: () => void,
  position: { x: number; y: number } | null
];
