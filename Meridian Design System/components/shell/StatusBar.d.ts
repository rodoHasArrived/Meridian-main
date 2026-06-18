/** Near-black footer status bar — a row of mono telemetry fields (connection, sync, counts, latency). */
export interface StatusBarItem {
  /** Optional small-caps label preceding the value. */
  label?: string;
  value: React.ReactNode;
  /** Leading status dot. */
  status?: "ok" | "warn" | "err";
  /** Push this item (and following) to the right. */
  push?: boolean;
}
export interface StatusBarProps {
  items: StatusBarItem[];
}
export declare function StatusBar(props: StatusBarProps): JSX.Element;
