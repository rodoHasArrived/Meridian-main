export const CORPORATE_ACTION_DATE_KINDS: Record<string, { symbol: string; label: string }> = {
  ExDate: { symbol: "×", label: "Ex-date" }, RecordDate: { symbol: "○", label: "Record date" }, PaymentDate: { symbol: "◆", label: "Payment date" },
  IssuerElection: { symbol: "!", label: "Issuer election deadline" }, BrokerElection: { symbol: "B", label: "Broker election deadline" }, CustodianElection: { symbol: "C", label: "Custodian election deadline" }
};
export function isCalendarDate(value: string): boolean {
  return /^\d{4}-\d{2}-\d{2}$/.test(value) && Number.isFinite(Date.parse(`${value}T00:00:00Z`)) && new Date(`${value}T00:00:00Z`).toISOString().slice(0, 10) === value;
}
export function addCalendarDays(value: string, days: number) {
  const date = new Date(`${value}T00:00:00Z`); date.setUTCDate(date.getUTCDate() + days); return date.toISOString().slice(0, 10);
}
export function corporateActionCalendarRange(search: string, localToday: string) {
  const query = new URLSearchParams(search);
  const anchor = query.get("date") ?? query.get("asOf")?.slice(0, 10) ?? localToday;
  const weekStart = isCalendarDate(anchor) ? addCalendarDays(anchor, -((new Date(`${anchor}T00:00:00Z`).getUTCDay() + 6) % 7)) : anchor;
  const from = query.get("caFrom") ?? query.get("from") ?? weekStart;
  const to = query.get("caTo") ?? query.get("to") ?? (isCalendarDate(from) ? addCalendarDays(from, 6) : from);
  const error = !isCalendarDate(from) || !isCalendarDate(to) || to < from || Date.parse(`${to}T00:00:00Z`) - Date.parse(`${from}T00:00:00Z`) > 366 * 86400000 ? "Choose a valid date range of at most 367 days." : null;
  return { from, to, error, caseId: query.get("caCase"), eventId: query.get("caEvent"), financialAccountId: query.get("financialAccountId") ?? undefined, custodyAccountId: query.get("custodyAccountId") ?? undefined };
}
export function updateCorporateActionCalendarSearch(search: string, updates: Record<string, string | null>) {
  const params = new URLSearchParams(search);
  for (const [key, value] of Object.entries(updates)) { if (value !== null) params.set(key, value); else params.delete(key); }
  return params.toString();
}
