import type { ProviderIntegrationConnection, ProviderIntegrationManifest } from "@/types";

export interface ProviderIntegrationSetupDraftIssue {
  field: string;
  label: string;
  message: string;
}

/**
 * Validates a provider integration setup draft before it is sent to the server. Rules mirror
 * ProviderIntegrationSetupValidator in src/Meridian.Application/Integrations/
 * ProviderIntegrationSetupValidation.cs; keep the two in sync when adding rules. Drafts come from
 * hand-edited JSON textareas, so every field access is defensive against missing values.
 */
export function validateProviderIntegrationSetupDraft(
  manifest: ProviderIntegrationManifest,
  connection: ProviderIntegrationConnection
): ProviderIntegrationSetupDraftIssue[] {
  const issues: ProviderIntegrationSetupDraftIssue[] = [];

  requireText(issues, manifest.manifestId, "manifest.manifestId", "Manifest id", "Manifest id is required.");
  requireText(
    issues,
    manifest.providerId,
    "manifest.providerId",
    "Manifest provider id",
    "Manifest provider id is required."
  );
  requireText(issues, connection.connectionId, "connection.connectionId", "Connection id", "Connection id is required.");
  requireText(
    issues,
    connection.providerId,
    "connection.providerId",
    "Connection provider id",
    "Connection provider id is required."
  );
  requireText(
    issues,
    connection.manifestId,
    "connection.manifestId",
    "Connection manifest id",
    "Connection manifest id is required."
  );
  requireText(
    issues,
    connection.credentialSecretRef,
    "connection.credentialSecretRef",
    "Connection credential secret reference",
    "Connection credential secret reference is required."
  );

  if (hasText(connection.manifestId) && hasText(manifest.manifestId) && connection.manifestId !== manifest.manifestId) {
    issues.push({
      field: "connection.manifestId",
      label: "Connection manifest id",
      message: "Provider connection manifest id must match the manifest being saved."
    });
  }

  if (hasText(connection.providerId) && hasText(manifest.providerId) && connection.providerId !== manifest.providerId) {
    issues.push({
      field: "connection.providerId",
      label: "Connection provider id",
      message: "Provider connection provider id must match the manifest provider id."
    });
  }

  const connectionEnvironment = typeof connection.environment === "string" ? connection.environment : "";
  const manifestEnvironment = typeof manifest.environment === "string" ? manifest.environment : "";
  if (connectionEnvironment.toLowerCase() !== manifestEnvironment.toLowerCase()) {
    issues.push({
      field: "connection.environment",
      label: "Connection environment",
      message: "Provider connection environment must match the manifest environment."
    });
  }

  if (!Array.isArray(manifest.capabilities)) {
    issues.push({
      field: "manifest.capabilities",
      label: "Manifest capabilities",
      message: "Manifest capabilities list is required (an empty list is allowed)."
    });
  }

  if (!Array.isArray(connection.enabledCapabilities)) {
    issues.push({
      field: "connection.enabledCapabilities",
      label: "Connection enabled capabilities",
      message: "Connection enabled capabilities list is required (an empty list is allowed)."
    });
  }

  const declaredCapabilities = new Set(
    (Array.isArray(manifest.capabilities) ? manifest.capabilities : [])
      .map((capability) => capability?.capability)
      .filter((capability): capability is NonNullable<typeof capability> => Boolean(capability))
  );
  const undeclaredCapabilities = new Set<string>();
  for (const capability of Array.isArray(connection.enabledCapabilities) ? connection.enabledCapabilities : []) {
    if (!declaredCapabilities.has(capability) && !undeclaredCapabilities.has(capability)) {
      undeclaredCapabilities.add(capability);
      issues.push({
        field: "connection.enabledCapabilities",
        label: "Connection enabled capabilities",
        message: `Provider connection enables ${capability}, but the manifest does not declare it.`
      });
    }
  }

  return issues;
}

export function formatProviderIntegrationSetupDraftIssues(
  issues: ProviderIntegrationSetupDraftIssue[]
): string[] {
  return issues.map((issue) => `${issue.label}: ${issue.message}`);
}

function requireText(
  issues: ProviderIntegrationSetupDraftIssue[],
  value: unknown,
  field: string,
  label: string,
  message: string
): void {
  if (!hasText(value)) {
    issues.push({ field, label, message });
  }
}

function hasText(value: unknown): value is string {
  return typeof value === "string" && value.trim().length > 0;
}
