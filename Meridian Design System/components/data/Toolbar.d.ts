/**
 * Toolbar — the standard band above a data surface: search/filters at the start, actions at
 * the end. Compose with ToolbarGroup (gapped cluster), ToolbarSpacer (pushes what follows to
 * the end), and ToolbarDivider (vertical hairline).
 *
 * @example
 * <Toolbar>
 *   <ToolbarGroup><Input placeholder="Search runs…" /><SegmentedControl options={["All","Live","Paper"]} value="All" /></ToolbarGroup>
 *   <ToolbarSpacer />
 *   <ToolbarGroup><Button variant="ghost">Export CSV</Button><Button variant="primary">New run</Button></ToolbarGroup>
 * </Toolbar>
 */
export interface ToolbarProps extends React.HTMLAttributes<HTMLDivElement> {
  children?: React.ReactNode;
}
export declare function Toolbar(props: ToolbarProps): JSX.Element;
export declare function ToolbarGroup(props: React.HTMLAttributes<HTMLDivElement>): JSX.Element;
export declare function ToolbarSpacer(): JSX.Element;
export declare function ToolbarDivider(): JSX.Element;
