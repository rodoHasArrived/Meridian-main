export interface PlaidLinkInstitutionMetadata {
  name?: string | null;
  institution_id?: string | null;
}

export interface PlaidLinkAccountMetadata {
  id: string;
  name?: string | null;
  official_name?: string | null;
  mask?: string | null;
  type?: string | null;
  subtype?: string | null;
  verification_status?: string | null;
}

export interface PlaidLinkSuccessMetadata {
  institution?: PlaidLinkInstitutionMetadata | null;
  accounts?: PlaidLinkAccountMetadata[] | null;
  link_session_id?: string | null;
}

export interface PlaidLinkSuccessResult {
  publicToken: string;
  metadata: PlaidLinkSuccessMetadata;
}

interface PlaidLinkErrorPayload {
  error_type?: string | null;
  error_code?: string | null;
  error_message?: string | null;
}

interface PlaidLinkHandler {
  open: () => void;
  exit?: () => void;
  destroy?: () => void;
}

interface PlaidLinkCreateOptions {
  token: string;
  onSuccess: (publicToken: string, metadata: PlaidLinkSuccessMetadata) => void;
  onExit: (error: PlaidLinkErrorPayload | null, metadata: Record<string, unknown>) => void;
  onEvent?: (eventName: string, metadata: Record<string, unknown>) => void;
  onLoad?: () => void;
}

declare global {
  interface Window {
    Plaid?: {
      create: (options: PlaidLinkCreateOptions) => PlaidLinkHandler;
    };
  }
}

const plaidScriptId = "plaid-link-js";
const plaidScriptSrc = "https://cdn.plaid.com/link/v2/stable/link-initialize.js";
let plaidScriptLoad: Promise<void> | null = null;

export async function openPlaidLink(linkToken: string): Promise<PlaidLinkSuccessResult> {
  if (!linkToken) {
    throw new Error("Plaid Link token is required.");
  }

  await loadPlaidLinkScript();
  if (!window.Plaid?.create) {
    throw new Error("Plaid Link did not load.");
  }

  return new Promise<PlaidLinkSuccessResult>((resolve, reject) => {
    let settled = false;
    let handler: PlaidLinkHandler | null = null;
    const cleanup = () => {
      handler?.destroy?.();
      handler = null;
    };
    const settle = (callback: () => void) => {
      if (settled) {
        return;
      }

      settled = true;
      callback();
      cleanup();
    };

    handler = window.Plaid!.create({
      token: linkToken,
      onSuccess: (publicToken, metadata) => settle(() => resolve({ publicToken, metadata: metadata ?? {} })),
      onExit: (error) => settle(() => {
        if (error) {
          reject(new Error(error.error_message ?? error.error_code ?? "Plaid Link exited with an error."));
          return;
        }

        reject(new Error("Plaid Link was closed before a bank account was linked."));
      }),
      onEvent: () => undefined
    });
    handler.open();
  });
}

function loadPlaidLinkScript(): Promise<void> {
  if (window.Plaid?.create) {
    return Promise.resolve();
  }

  if (plaidScriptLoad) {
    return plaidScriptLoad;
  }

  plaidScriptLoad = new Promise<void>((resolve, reject) => {
    const existing = document.getElementById(plaidScriptId) as HTMLScriptElement | null;
    if (existing) {
      existing.addEventListener("load", () => resolve(), { once: true });
      existing.addEventListener("error", () => reject(new Error("Plaid Link script failed to load.")), { once: true });
      return;
    }

    const script = document.createElement("script");
    script.id = plaidScriptId;
    script.src = plaidScriptSrc;
    script.async = true;
    script.onload = () => resolve();
    script.onerror = () => reject(new Error("Plaid Link script failed to load."));
    document.head.appendChild(script);
  });

  return plaidScriptLoad;
}
