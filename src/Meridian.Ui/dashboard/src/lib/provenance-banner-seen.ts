import { getLocalStorage } from "@/lib/theme";

/**
 * Per-viewer record of the last provenance/environment pairing whose full banner this browser
 * showed. Only a convenience: a missing or blocked store means "not seen", which shows the full
 * banner again, so the failure mode is always the more conspicuous label.
 */
export const PROVENANCE_BANNER_SEEN_STORAGE_KEY = "meridian.workstation.provenance-banner-seen";

export function provenanceBannerSeenKey(provenance: string, environment: string): string {
  return `${provenance}:${environment}`;
}

export function readSeenProvenanceBanner(storage: Storage | null = getLocalStorage()): string | null {
  try {
    return storage?.getItem(PROVENANCE_BANNER_SEEN_STORAGE_KEY) ?? null;
  } catch {
    return null;
  }
}

export function writeSeenProvenanceBanner(key: string, storage: Storage | null = getLocalStorage()): void {
  try {
    storage?.setItem(PROVENANCE_BANNER_SEEN_STORAGE_KEY, key);
  } catch {
    // Best-effort: blocked storage keeps showing the full banner, which is the safe default.
  }
}
