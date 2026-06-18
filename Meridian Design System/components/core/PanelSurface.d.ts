/** White paper card — mirrors `CardStyle` (1px border, 8px radius, whisper shadow). The outermost surface primitive. */
export interface PanelSurfaceProps extends React.HTMLAttributes<HTMLDivElement> {
  /** Use the raised off-white surface (metric tiles, inspector rails). @default false */
  raised?: boolean;
  /** Slightly deeper shadow (ElevatedSurfaceCard). @default false */
  elevated?: boolean;
  /** Drop the shadow entirely. @default false */
  flat?: boolean;
  children?: React.ReactNode;
}
export declare function PanelSurface(props: PanelSurfaceProps): JSX.Element;
