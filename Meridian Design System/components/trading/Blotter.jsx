// Meridian Blotter — orders blotter preset over DenseDataTable. Encodes the desk conventions
// once: side colored by direction (washed text, never fills), qty/price mono right-aligned,
// status through SeverityBadge normalization, UTC times through Timestamp.
import React from "react";
import { DenseDataTable } from "../data/DenseDataTable";
import { SeverityBadge } from "../operations/SeverityBadge";
import { Timestamp } from "../core/Timestamp";

const STATUS_MAP = {
  filled: "Complete", "partially filled": "InProgress", working: "Running",
  pending: "Pending", cancelled: "NotStarted", canceled: "NotStarted", rejected: "Rejected",
};

function sideCell(side) {
  const s = String(side).toLowerCase();
  return (
    <span style={{
      color: s === "buy" ? "var(--green-dim)" : s === "sell" ? "var(--red-dim)" : "var(--text-secondary)",
      fontWeight: 600, fontVariant: "all-small-caps", letterSpacing: ".03em",
    }}>{side}</span>
  );
}

export function Blotter({ orders = [], onRowClick, selectedIndex, sortKey, sortDir, onSort, extraColumns = [] }) {
  const columns = [
    { key: "id", label: "Order" },
    { key: "time", label: "Time", render: (r) => <Timestamp value={r.time} format="time" /> },
    { key: "symbol", label: "Symbol" },
    { key: "side", label: "Side", render: (r) => sideCell(r.side) },
    { key: "qty", label: "Qty", align: "right" },
    { key: "type", label: "Type" },
    { key: "limit", label: "Limit", align: "right", render: (r) => r.limit ?? "—" },
    { key: "filled", label: "Filled", align: "right" },
    {
      key: "status", label: "Status",
      render: (r) => <SeverityBadge status={STATUS_MAP[String(r.status).toLowerCase()] || r.status} label={r.status} />,
    },
    ...extraColumns,
  ];
  return (
    <DenseDataTable
      columns={columns}
      rows={orders}
      onRowClick={onRowClick}
      selectedIndex={selectedIndex}
      sortKey={sortKey}
      sortDir={sortDir}
      onSort={onSort}
    />
  );
}
