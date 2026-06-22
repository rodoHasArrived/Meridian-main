/** Inline status banner for run results, data health, and session notices. */
export interface StatusBannerProps {
  /** @default "success" */
  tone?: "success" | "warning" | "danger" | "info";
  title: string;
  /** Muted second line. */
  detail?: string;
}
export declare function StatusBanner(props: StatusBannerProps): JSX.Element;
