// Meridian Blotter — orders blotter preset over DataGrid. Encodes the desk conventions
// once: side colored by direction (washed text, never fills), qty/price mono right-aligned,
// status through SeverityBadge normalization, UTC times through Timestamp.
// The preset keeps its index-based API (selectedIndex/onRowClick(row, index)) across the
// 4.0 DenseDataTable → DataGrid substitution.
import React from "react";
import { DataGrid } from "../data/DataGrid";
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
    { key: "time", label: "Time", cell: (r) => <Timestamp value={r.time} format="time" /> },
    { key: "symbol", label: "Symbol" },
    { key: "side", label: "Side", cell: (r) => sideCell(r.side) },
    { key: "qty", label: "Qty", align: "right" },
    { key: "type", label: "Type" },
    { key: "limit", label: "Limit", align: "right", cell: (r) => r.limit ?? "—" },
    { key: "filled", label: "Filled", align: "right" },
    {
      key: "status", label: "Status",
      cell: (r) => <SeverityBadge status={STATUS_MAP[String(r.status).toLowerCase()] || r.status} label={r.status} />,
    },
    ...extraColumns.map((c) => (c.render && !c.cell ? { ...c, cell: c.render } : c)),
  ];
  return (
    <DataGrid
      columns={columns}
      rows={orders}
      rowKey="index"
      onRowClick={onRowClick ? (r, k, i) => onRowClick(r, i) : undefined}
      activeKey={selectedIndex}
      sortKey={sortKey}
      sortDir={sortDir}
      onSort={onSort}
      label="Orders blotter"
    />
  );
}
