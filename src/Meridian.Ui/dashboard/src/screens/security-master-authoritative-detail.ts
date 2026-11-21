import { useCallback, useState } from "react";
import { describeApiError, type ApiErrorDisplay } from "@/lib/api-errors";
import type { SecurityMasterEntry } from "@/types";

type DetailLoader = (securityId: string) => Promise<SecurityMasterEntry>;

/** Load full economics independently of a filtered search, retaining the caller's selection fence. */
export function useAuthoritativeSecurityDetail(loader?: DetailLoader) {
  const [selectedEntry, setSelectedEntry] = useState<SecurityMasterEntry | null>(null);
  const [detailError, setDetailError] = useState<ApiErrorDisplay | null>(null);
  const [detailLoading, setDetailLoading] = useState(false);
  const resetDetail = useCallback(() => {
    setSelectedEntry(null);
    setDetailError(null);
    setDetailLoading(false);
  }, []);

  const loadDetail = useCallback(async (securityId: string, isCurrent: () => boolean) => {
    if (!loader) return;
    setDetailLoading(true);
    try {
      const entry = await loader(securityId);
      if (!isCurrent()) return;
      if (entry.securityId.toLowerCase() !== securityId.toLowerCase())
        throw new Error("Security detail returned a different security.");
      setSelectedEntry(entry);
    } catch (error) {
      if (isCurrent()) setDetailError(describeApiError(error, "Authoritative security detail failed to load."));
    } finally {
      if (isCurrent()) setDetailLoading(false);
    }
  }, [loader]);

  return { selectedEntry, detailError, detailLoading, resetDetail, loadDetail };
}
