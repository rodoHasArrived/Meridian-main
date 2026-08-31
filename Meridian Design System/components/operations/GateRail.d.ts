/**
 * Gate rail — the operations pipeline as a connected horizontal stepper. Mirrors the
 * operator gate model (`OperationsGateKey`: BrokerIngest, SecurityMaster, LedgerPosting,
 * Reconciliation, Approval) and gate statuses (`NotStarted | InProgress | Passed |
 * ReviewRequired | Blocked`). Each node tints by status; the connector into a node turns
 * green once the previous gate has Passed.
 *
 * @example
 * <GateRail gates={[
 *   { key: "BrokerIngest",   label: "Broker ingest",   status: "Passed" },
 *   { key: "SecurityMaster", label: "Security master", status: "Passed" },
 *   { key: "LedgerPosting",  label: "Ledger posting",  status: "InProgress" },
 *   { key: "Reconciliation", label: "Reconciliation",  status: "ReviewRequired" },
 *   { key: "Approval",       label: "Approval",         status: "NotStarted" },
 * ]} />
 */
export interface GateRailGate {
  /** Stable key (e.g. an OperationsGateKey). */
  key?: string;
  /** Gate label shown under the node. */
  label: React.ReactNode;
  /** Gate status string — normalized to ready · review · action · blocked · info. */
  status: string;
  /** Override the auto-humanized status caption. */
  statusLabel?: React.ReactNode;
}
export interface GateRailProps {
  gates: GateRailGate[];
  className?: string;
}
export declare function GateRail(props: GateRailProps): JSX.Element;
