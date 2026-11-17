/** Query keys that make one provider's verification history linkable from Today and elsewhere. */
export const PROVIDER_HISTORY_CONNECTION_PARAM = "verificationConnection";
export const PROVIDER_HISTORY_FAMILY_PARAM = "providerFamily";
export const PROVIDER_HISTORY_ANCHOR = "provider-verification-history";

/** Link to a provider family's verification history on the Data overview. */
export function providerHistoryHref(providerFamilyId: string): string {
  return `/data?${PROVIDER_HISTORY_FAMILY_PARAM}=${encodeURIComponent(providerFamilyId)}#${PROVIDER_HISTORY_ANCHOR}`;
}
