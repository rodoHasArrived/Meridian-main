export function formatCorporateActionPayload(
  payload: Record<string, unknown> | null | undefined
): string {
  if (!payload) return "—";
  try {
    return JSON.stringify(payload);
  } catch {
    return "Payload could not be displayed";
  }
}
