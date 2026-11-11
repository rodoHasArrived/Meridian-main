/** Inline status banner for run results, data health, and session notices. */
export interface StatusBannerProps {
  /** @default "success" */
  tone?: "success" | "warning" | "danger" | "info";
  title: string;
  /** Muted second line. */
  detail?: string;
  /** 3.5 · Live-region announcement: "polite" (role=status) | "assertive" (role=alert) | "off".
   *  @default assertive for danger, polite otherwise */
  announce?: "polite" | "assertive" | "off";
}
export declare function StatusBanner(props: StatusBannerProps): JSX.Element;
