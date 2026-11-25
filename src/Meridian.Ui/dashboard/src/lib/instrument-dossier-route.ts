/** Stable identity is required; retain existing scope and replace only record-local state. */
export function instrumentDossierRoute(instrumentId: string, search = "") {
  const params = new URLSearchParams(search);
  params.delete("securityId");
  params.set("tab", "positions");
  return `/records/instrument/${encodeURIComponent(instrumentId)}?${params}`;
}
