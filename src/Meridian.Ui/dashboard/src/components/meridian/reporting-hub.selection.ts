export const REPORTING_WORK_SELECTION_PARAM = "reportingWork";

const reportingScopeParams = new Set([
  "tenant", "tenantid", "company", "companyid", "entity", "entityid",
  "fund", "fundid", "fundprofileid", "fundaccountid", "account", "accountid",
  "book", "bookid", "ledgerbookid", "period", "periodid", "asofdate",
  "symbol", "runid", "provider", "from", "to", "date", "asof"
]);

export function readReportingWorkSelection(search: string): string | null {
  return new URLSearchParams(search).get(REPORTING_WORK_SELECTION_PARAM);
}

export function withReportingWorkSelection(search: string, workItemId: string | null): string {
  const params = new URLSearchParams(search);
  if (workItemId === null) {
    params.delete(REPORTING_WORK_SELECTION_PARAM);
  } else {
    params.set(REPORTING_WORK_SELECTION_PARAM, workItemId);
  }
  const nextSearch = params.toString();
  return nextSearch ? `?${nextSearch}` : "";
}

/** UI selection is bound to the route's financial scope, never to a row's label. */
export function reportingWorkScopeKey(pathname: string, search: string): string {
  const scope = Array.from(new URLSearchParams(search).entries())
    .filter(([key]) => reportingScopeParams.has(key.toLowerCase()))
    .map(([key, value]) => [key.toLowerCase(), value])
    .sort(([leftKey, leftValue], [rightKey, rightValue]) =>
      leftKey.localeCompare(rightKey) || leftValue.localeCompare(rightValue));
  return JSON.stringify([pathname, scope]);
}
