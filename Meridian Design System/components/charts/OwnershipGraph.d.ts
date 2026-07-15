/**
 * Ownership graph — entity/control structure diagram for family-office and fund
 * structures. Mirrors `FamilyOwnershipGraphDto`: nodes (household, trust, LLC, fund,
 * account, individual) and directed edges with a relationship type and ownership %.
 * Layered top-down layout — roots (no incoming edge) on top, each node one level
 * below its deepest parent; edges are measured SVG curves labeled with the ownership
 * percent (relationship type in the hover title). Accepts either short prop names
 * (`id`/`label`/`from`/`to`/`percent`) or the DTO field names
 * (`nodeId`/`displayName`/`sourceNodeId`/`targetNodeId`/`ownershipPercent`).
 * Re-measures on container resize; cycle-safe.
 *
 * @example
 * <OwnershipGraph
 *   nodes={[
 *     { id: "hh",  label: "Whitfield Household", type: "Household" },
 *     { id: "tr1", label: "Whitfield Family Trust", type: "Trust", jurisdiction: "SD" },
 *     { id: "llc", label: "Blue Harbor LLC", type: "Operating", jurisdiction: "DE", currency: "USD" },
 *     { id: "acct", label: "NT Custody 4417", type: "Account", currency: "USD" },
 *   ]}
 *   edges={[
 *     { from: "hh",  to: "tr1", relationship: "Grantor",  percent: 100 },
 *     { from: "tr1", to: "llc", relationship: "Member",   percent: 85 },
 *     { from: "llc", to: "acct", relationship: "Owner",   percent: 100 },
 *   ]}
 *   selectedId={sel} onSelectNode={setSel}
 * />
 */
export interface OwnershipGraphNode {
  id?: string; nodeId?: string;
  label?: React.ReactNode; displayName?: string;
  /** Node kind eyebrow (Household, Trust, LLC, Fund, Account, Individual, …). */
  type?: string; nodeType?: string;
  jurisdiction?: string;
  currency?: string;
  [key: string]: unknown;
}
export interface OwnershipGraphEdge {
  id?: string; edgeId?: string;
  from?: string; sourceNodeId?: string;
  to?: string; targetNodeId?: string;
  /** Relationship type (Grantor, Trustee, Member, Owner, Beneficiary, …) — hover title. */
  relationship?: string; relationshipType?: string;
  /** Ownership percent, rendered on the edge. */
  percent?: number; ownershipPercent?: number;
}
export interface OwnershipGraphProps extends React.HTMLAttributes<HTMLElement> {
  nodes?: OwnershipGraphNode[];
  edges?: OwnershipGraphEdge[];
  /** Controlled selected node id (accent outline). */
  selectedId?: string;
  /** Makes nodes clickable buttons. */
  onSelectNode?: (id: string, node: OwnershipGraphNode) => void;
}
export declare function OwnershipGraph(props: OwnershipGraphProps): JSX.Element;
