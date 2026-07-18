import type { ApiErrorDisplay } from "@/lib/api-errors";
import type {
  PlaidInstitution,
  PlaidLinkTokenResponse,
  PlaidPublicTokenExchangeResult,
} from "@/types";
import type {
  ProviderSetupFormState,
  ProviderSetupInstitutionSearchPhase,
  ProviderSetupInstitutionSearchState,
  ProviderSetupPlaidLinkTokenPhase,
} from "./data-screen.view-model";

export function buildPlaidInstitutionSearchState({
  form,
  query,
  phase,
  results,
  selectedInstitutionId,
  error,
  linkTokenPhase = "idle",
  linkTokenResult = null,
  exchangeResult = null,
  linkTokenError = null
}: {
  form: ProviderSetupFormState;
  query: string;
  phase: ProviderSetupInstitutionSearchPhase;
  results: PlaidInstitution[];
  selectedInstitutionId: string | null;
  error: ApiErrorDisplay | null;
  linkTokenPhase?: ProviderSetupPlaidLinkTokenPhase;
  linkTokenResult?: PlaidLinkTokenResponse | null;
  exchangeResult?: PlaidPublicTokenExchangeResult | null;
  linkTokenError?: ApiErrorDisplay | null;
}): ProviderSetupInstitutionSearchState | null {
  if (form.kind !== "plaid") {
    return null;
  }

  const trimmedQuery = query.trim();
  const disabledReason = null;
  const searchDisabledReason = trimmedQuery.length < 2
    ? "Type at least two characters to search for a financial institution."
    : null;
  const selectedInstitution = results.find((institution) => institution.institutionId === selectedInstitutionId);
  const linkTokenDisabledReason = selectedInstitution
    ? null
    : "Select a supported financial institution before opening the secure bank connection.";
  const linkInstitutionName = linkTokenResult?.institutionName ?? selectedInstitution?.name ?? "the selected institution";
  const linkTokenStatusLabel = linkTokenPhase === "creating"
    ? "Preparing the secure bank connection."
    : linkTokenPhase === "opening"
      ? `Plaid Link is open for ${linkInstitutionName}. Complete the secure bank login in the modal.`
      : linkTokenPhase === "exchanging"
        ? "Plaid Link returned a public token. Meridian is exchanging it on the server."
        : linkTokenPhase === "linked"
          ? `${linkInstitutionName} account evidence was linked and stored by Meridian.`
          : linkTokenPhase === "ready"
            ? `Secure sandbox bank connection is ready for ${linkInstitutionName}.`
            : linkTokenPhase === "error"
              ? linkTokenError?.summary ?? "Secure bank connection could not be prepared."
              : selectedInstitution
                ? `Open Plaid Link for ${selectedInstitution.name}, then sign in with the sandbox test credentials.`
                : "Select a supported institution to prepare the sandbox bank connection.";
  const statusLabel = phase === "searching"
    ? "Searching supported financial institutions."
    : phase === "error"
      ? error?.summary ?? "Bank search failed."
      : phase === "success"
        ? results.length === 0
          ? `No supported institutions matched "${trimmedQuery}".`
          : `${results.length} supported institution${results.length === 1 ? "" : "s"} found for "${trimmedQuery}".`
        : "Search Plaid-supported institutions before opening the bank connection flow.";

  return {
    id: "provider-setup-plaid-institution",
    label: "Financial institution",
    ariaLabel: "Search supported financial institutions",
    placeholder: "Search for your bank",
    description: "Availability comes from Meridian's bank-connection provider. Final consent still happens in the secure bank connection flow.",
    value: query,
    disabled: Boolean(disabledReason),
    disabledReason,
    phase,
    statusLabel,
    searchAction: {
      label: phase === "searching" ? "Searching..." : "Search",
      ariaLabel: searchDisabledReason
        ? `Search supported financial institutions unavailable: ${searchDisabledReason}`
        : `Search supported financial institutions for ${trimmedQuery}`,
      disabled: phase === "searching" || searchDisabledReason !== null,
      disabledReason: phase === "searching" ? "Bank search is already running." : searchDisabledReason,
      busy: phase === "searching"
    },
    results: results.map((institution) => ({
      institutionId: institution.institutionId,
      name: institution.name,
      detail: [
        institution.institutionId,
        institution.countryCodes.join(", "),
        institution.products.length > 0 ? institution.products.join(", ") : null
      ].filter(Boolean).join(" | "),
      selected: institution.institutionId === selectedInstitutionId
    })),
    selectedInstitutionLabel: selectedInstitution?.name ?? null,
    linkTokenPhase,
    linkTokenStatusLabel,
    linkTokenAction: {
      label: linkTokenPhase === "creating"
        ? "Preparing..."
        : linkTokenPhase === "opening"
          ? "Plaid Link open"
          : linkTokenPhase === "exchanging"
            ? "Linking..."
            : linkTokenPhase === "linked"
              ? "Linked"
              : "Open secure bank connection",
      ariaLabel: linkTokenDisabledReason
        ? `Open secure bank connection unavailable: ${linkTokenDisabledReason}`
        : `Open secure bank connection for ${selectedInstitution?.name}`,
      disabled: linkTokenPhase === "creating" || linkTokenPhase === "opening" || linkTokenPhase === "exchanging" || linkTokenPhase === "linked" || linkTokenDisabledReason !== null,
      disabledReason: linkTokenPhase === "creating" || linkTokenPhase === "opening" || linkTokenPhase === "exchanging"
        ? "Secure bank connection is already in progress."
        : linkTokenPhase === "linked"
          ? "Bank account evidence is already linked."
          : linkTokenDisabledReason,
      busy: linkTokenPhase === "creating" || linkTokenPhase === "opening" || linkTokenPhase === "exchanging"
    },
    linkTokenResult: linkTokenResult
      ? {
          linkTokenPreview: maskLinkToken(linkTokenResult.linkToken),
          requestId: linkTokenResult.requestId ?? null,
          expirationLabel: formatOptionalDateTime(linkTokenResult.expiration ?? null),
          institutionLabel: linkTokenResult.institutionName ?? selectedInstitution?.name ?? null,
          environmentLabel: linkTokenResult.environment ?? form.environment ?? null
        }
      : null,
    linkedEvidence: exchangeResult
      ? {
          itemId: exchangeResult.item.itemId,
          institutionName: exchangeResult.item.institutionName,
          status: exchangeResult.item.status,
          accountCountLabel: `${exchangeResult.accounts.length} account${exchangeResult.accounts.length === 1 ? "" : "s"} linked`,
          accounts: exchangeResult.accounts.map((account) => ({
            id: account.plaidAccountId,
            name: account.name,
            detail: [
              account.mask ? `mask ${account.mask}` : null,
              account.type,
              account.subtype
            ].filter(Boolean).join(" | ")
          })),
          requestId: exchangeResult.requestId ?? null
        }
      : null,
    sandboxGuide: form.environment === "sandbox" || linkTokenResult?.environment?.toLowerCase() === "sandbox"
      ? {
          title: "Sandbox login",
          detail: "Use these Plaid Sandbox credentials inside the secure bank connection after Link opens.",
          username: "user_good",
          password: "pass_good"
        }
      : null,
    errorText: error?.summary ?? null
  };
}

function maskLinkToken(linkToken: string): string {
  if (linkToken.length <= 16) {
    return "Link token ready";
  }

  return `${linkToken.slice(0, 12)}...${linkToken.slice(-4)}`;
}

function formatOptionalDateTime(value: string | null): string | null {
  if (!value) {
    return null;
  }

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return value;
  }

  return date.toLocaleString(undefined, {
    dateStyle: "medium",
    timeStyle: "short"
  });
}

