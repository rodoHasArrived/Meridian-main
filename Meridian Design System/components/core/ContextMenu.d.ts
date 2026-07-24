/**
 * ContextMenu — right-click menu for table rows and UI elements.
 *
 * Supports icons, dividers, disabled items, and danger (red) styling.
 * Smart positioning: auto-adjusts to avoid overflow off-screen.
 *
 * @example
 * const [menu, setMenu] = React.useState(null);
 * <div onContextMenu={(e) => { e.preventDefault(); setMenu({ x: e.clientX, y: e.clientY }); }}>Right-click me</div>
 * {menu && (
 *   <ContextMenu
 *     x={menu.x}
 *     y={menu.y}
 *     items={[
 *       { label: 'Edit', icon: '✎', onClick: handleEdit },
 *       { label: 'Copy', icon: '⎘', onClick: handleCopy },
 *       { type: 'divider' },
 *       { label: 'Delete', icon: '⌫', dangerous: true, onClick: handleDelete }
 *     ]}
 *     onClose={() => setMenu(null)}
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

  /** Anchor x (viewport px) — pass `position.x` from useContextMenu. @default 0 */
  x?: number;

  /** Anchor y (viewport px) — pass `position.y` from useContextMenu. @default 0 */
  y?: number;

  /** Fired when the menu is closed (click overlay, press Escape, or select an item) */
  onClose?: () => void;
}

export declare function ContextMenu(props: ContextMenuProps): JSX.Element | null;

/**
 * useContextMenu — convenience hook that wires the right-click handler + open/position state.
 *
 * NOTE: bundle-internal — like `useToast` / `useTableState`, this lowercase helper is NOT on the
 * `window.<Namespace>` export map, so consumers cannot import it. Manage the anchor point with your
 * own `useState` and pass it as `x`/`y` (see the ContextMenu example above). This declaration
 * documents the in-bundle helper for design-system authoring only.
 *
 * @example
 * const [onContextMenu, isOpen, closeMenu, position] = useContextMenu();
 * <div onContextMenu={onContextMenu}>Right-click me</div>
 * {isOpen && position && <ContextMenu items={[...]} x={position.x} y={position.y} onClose={closeMenu} />}
 *
 * @returns Tuple: [onContextMenu handler, isOpen boolean, closeMenu function, position object]
 */
export declare function useContextMenu(): [
  onContextMenu: (e: React.MouseEvent) => void,
  isOpen: boolean,
  closeMenu: () => void,
  position: { x: number; y: number } | null
];
