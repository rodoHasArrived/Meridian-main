import { getJson } from "@/lib/api";

export interface CancelAllPreview {
  orderIds: string[];
  observedAt: string;
}

/** Read the OMS open book, not the selected account's filtered blotter. */
export async function readCancelAllPreview(): Promise<CancelAllPreview> {
  const rows = await getJson<unknown>("/api/execution/orders", { allowDevelopmentFallback: false });
  if (!Array.isArray(rows) || rows.some((row) => !row || typeof row.orderId !== "string" || !row.orderId.trim())) {
    throw new Error("The OMS open-order population could not be verified.");
  }
  const orderIds = rows.map((row: { orderId: string }) => row.orderId);
  if (new Set(orderIds).size !== orderIds.length) throw new Error("The OMS open-order population contains duplicate identities.");
  return { orderIds, observedAt: new Date().toISOString() };
}
