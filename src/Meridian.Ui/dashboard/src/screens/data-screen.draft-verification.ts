import { useCallback, useEffect, useRef, useState } from "react";
import { verifyDraftProviderConnection } from "@/lib/api";
import { supportsProviderDraft } from "@/lib/provider-credential-scope";
import type { ProviderCredentialDraftResult } from "@/types/workstation-2";
import type { ProviderSetupFormState } from "./data-screen.provider-setup.types";

export function useProviderSetupDraftVerification() {
  const active = useRef<AbortController | null>(null);
  const [result, setResult] = useState<ProviderCredentialDraftResult | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => () => active.current?.abort(), []);

  const reset = useCallback(() => {
    active.current?.abort();
    active.current = null;
    setResult(null);
    setBusy(false);
  }, []);

  const verify = useCallback(async (form: ProviderSetupFormState) => {
    if (active.current || !canTestProviderDraft(form)) return;
    const controller = new AbortController();
    active.current = controller;
    setBusy(true);
    setResult(null);
    const credentials: Record<string, string> = form.kind === "alpaca"
      ? { KeyId: form.apiKey.trim(), SecretKey: form.apiSecret.trim() }
      : { ApiKey: form.apiKey.trim() };
    try {
      const response = await verifyDraftProviderConnection(form.kind, credentials, form.environment, { signal: controller.signal });
      if (response.providerId.toLowerCase() !== form.kind.toLowerCase()) throw new Error("Draft result names another provider.");
      if (active.current === controller) setResult(response);
    } catch {
      if (active.current === controller) {
        // placeholder-vocabulary-ignore: "Unavailable" is the server verification status enum.
        setResult({ providerId: form.kind, status: "Unavailable",
          message: "The host could not verify this draft. For an owned account, use the credential editor in Settings." });
      }
    } finally {
      if (active.current === controller) {
        active.current = null;
        setBusy(false);
      }
    }
  }, []);

  return { result, busy, reset, verify };
}

export function canTestProviderDraft(form: ProviderSetupFormState) {
  return supportsProviderDraft(form.kind) && Boolean(form.apiKey.trim() &&
    (form.kind !== "alpaca" || form.apiSecret.trim()));
}
