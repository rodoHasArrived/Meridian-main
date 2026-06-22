/** Left operator rail — light paper sidebar with small-caps section labels and a 3px teal-blue active indicator. */
export interface NavRailItem {
  id: string;
  label: string;
  /** URL of a 16px duotone module icon from assets/icons/. */
  icon?: string;
  /** Optional mono shortcut chip on the right (e.g. "G D"). */
  shortcut?: string;
}
export interface NavRailProps {
  sections: { label: string; items: NavRailItem[] }[];
  activeId?: string;
  onSelect?: (id: string) => void;
}
export declare function NavRail(props: NavRailProps): JSX.Element;
