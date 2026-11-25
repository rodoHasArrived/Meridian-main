import { apiGetJson } from "@/lib/api";

export interface EffectiveBackfillProvider {
  providerId: string;
  displayName: string;
  priority: number;
  isSimulated: boolean;
}

export interface EffectiveBackfillOrder {
  providers: EffectiveBackfillProvider[];
  availableProviders?: EffectiveBackfillProvider[];
  fallbackEnabled: boolean;
  rotationEnabled: boolean;
}

export function loadEffectiveBackfillOrder(signal?: AbortSignal) {
  return apiGetJson<EffectiveBackfillOrder>("/api/backfill/providers/effective-order", {
    ...(signal ? { signal } : {}),
    allowDevelopmentFallback: false
  });
}
