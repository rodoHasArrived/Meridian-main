
import { VALUE_STATE_LABELS } from "@/lib/format";export function formatCorporateActionPayload(
  payload: Record<string, unknown> | null | undefined
): string {
  if (!payload) return VALUE_STATE_LABELS.unavailable;
  try {
    return JSON.stringify(payload);
  } catch {
    return "Payload could not be displayed";
  }
}
