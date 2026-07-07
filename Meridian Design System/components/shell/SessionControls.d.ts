/**
 * Session & permission primitives — the pieces every real deployment needs and no template
 * previously showed.
 *
 * `UserMenu` — initials chip (chrome-dark by default; `onChrome={false}` for paper surfaces)
 * opening a popover with identity, role badge, custom items, and sign out.
 * `RoleBadge` — small-caps permission chip: admin (violet) · operator (steel) · viewer (neutral).
 * `ReadOnlyBanner` — persistent amber strip when the operator's role can't write. Use this,
 * not a Toast — permission state is not transient.
 *
 * @example
 * <UserMenu name="R. Alvarez" role="operator" detail="desk-2 · UTC"
 *   items={[{ label: "Preferences", onSelect: openPrefs }]} onSignOut={signOut} />
 * <RoleBadge role="viewer" />
 * <ReadOnlyBanner />
 */
export interface UserMenuItem {
  label: React.ReactNode;
  onSelect?: () => void;
  danger?: boolean;
}
export interface UserMenuProps {
  name?: string;
  /** Shown as a RoleBadge in the identity block. */
  role?: string;
  /** Mono secondary line (desk, timezone, session). */
  detail?: React.ReactNode;
  items?: UserMenuItem[];
  onSignOut?: () => void;
  /** Style the chip for the near-black chrome (true) or paper surfaces (false). @default true */
  onChrome?: boolean;
}
export declare function UserMenu(props: UserMenuProps): JSX.Element;

export interface RoleBadgeProps {
  /** admin · operator · viewer (unknown values render as viewer style with the raw label). @default "viewer" */
  role?: string;
  /** Override the visible label. */
  label?: React.ReactNode;
}
export declare function RoleBadge(props: RoleBadgeProps): JSX.Element;

export interface ReadOnlyBannerProps {
  message?: React.ReactNode;
}
export declare function ReadOnlyBanner(props: ReadOnlyBannerProps): JSX.Element;

/** Namespace object bundling the three session primitives. */
export declare const SessionControls: {
  UserMenu: typeof UserMenu;
  RoleBadge: typeof RoleBadge;
  ReadOnlyBanner: typeof ReadOnlyBanner;
};
