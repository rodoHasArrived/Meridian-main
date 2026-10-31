import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { ProviderOwnershipPanel } from "./settings-provider-ownership-panel";
import type { ProviderRoutingConnection } from "@/types";

const owned = (id: string): ProviderRoutingConnection =>
  ({ connectionId: id, providerFamilyId: "alpaca", displayName: id, tenantId: "tenant-a", externalAccountId: `account-${id}`, credentialEnvironment: "paper", enabled: true } as ProviderRoutingConnection);

function status(generation: string | null) {
  return {
    providerId: "alpaca", displayName: "Alpaca", credentialState: "Configured", credentialSource: "LocalEncryptedStore",
    verificationState: "NotVerified", missingFields: [], presentFields: ["KeyId"],
    auditMetadata: generation ? { credentialGeneration: generation } : {}
  };
}

describe("provider ownership panel", () => {
  it("previews, migrates with the previewed generation, then rolls back the same receipt", async () => {
    const preview = vi.fn().mockResolvedValue(status("gen-1"));
    const migrate = vi.fn().mockImplementation(async (_connectionId: string, request: { migrationId: string }) => ({
      migrationId: request.migrationId, providerId: "alpaca",
      scope: { tenantId: "tenant-a", connectionId: "paper-a", externalAccountId: "account-paper-a", environment: "paper" },
      credentialGeneration: "gen-1", actor: "operator-a", migratedAt: "2026-10-31T00:00:00Z", rolledBack: false
    }));
    const rollback = vi.fn().mockImplementation(async (_connectionId: string, request: { migrationId: string }) => ({
      migrationId: request.migrationId, providerId: "alpaca",
      scope: { tenantId: "tenant-a", connectionId: "paper-a", externalAccountId: "account-paper-a", environment: "paper" },
      credentialGeneration: "gen-1", actor: "operator-a", migratedAt: "2026-10-31T00:00:00Z", rolledBack: true, rolledBackBy: "operator-a"
    }));
    render(<ProviderOwnershipPanel providerId="alpaca" displayName="Alpaca" connection={owned("paper-a")} api={{ preview, migrate, rollback }} />);

    const migrateButton = screen.getByRole("button", { name: "Migrate to this connection" });
    expect(migrateButton).toBeDisabled();
    expect(screen.getByLabelText("Alpaca ownership connection")).toHaveTextContent("paper-a · account-paper-a · paper");

    fireEvent.click(screen.getByRole("button", { name: "Preview migration" }));
    await waitFor(() => expect(preview).toHaveBeenCalledWith("paper-a", "alpaca"));
    await screen.findByText(/generation gen-1 can be migrated/);
    expect(migrateButton).toBeEnabled();

    fireEvent.click(migrateButton);
    await screen.findByText(/retained by operator-a at generation gen-1/);
    expect(migrate).toHaveBeenCalledTimes(1);
    const [connectionId, request] = migrate.mock.calls[0];
    expect(connectionId).toBe("paper-a");
    expect(request).toMatchObject({ providerId: "alpaca", expectedGeneration: "gen-1" });
    expect(request.migrationId).toBeTruthy();

    fireEvent.click(screen.getByRole("button", { name: "Roll back migration" }));
    await screen.findByText(/rolled back by operator-a/);
    expect(rollback).toHaveBeenCalledWith("paper-a", { providerId: "alpaca", migrationId: request.migrationId });
    expect(screen.getByRole("button", { name: "Roll back migration" })).toBeDisabled();
  });

  it("refuses migration when the preview carries no generation and surfaces a refused bind", async () => {
    const preview = vi.fn().mockResolvedValue(status(null));
    const bind = vi.fn().mockRejectedValue(new Error("forbidden"));
    render(<ProviderOwnershipPanel providerId="alpaca" displayName="Alpaca" connection={owned("paper-a")} api={{ preview, bind }} />);
    fireEvent.click(screen.getByRole("button", { name: "Preview migration" }));
    await screen.findByText(/Nothing to migrate/);
    expect(screen.getByRole("button", { name: "Migrate to this connection" })).toBeDisabled();

    fireEvent.click(screen.getByRole("button", { name: "Bind as host runtime credential" }));
    expect(await screen.findByRole("alert")).toHaveTextContent(/ownership command was not confirmed|forbidden/i);
    expect(bind).toHaveBeenCalledWith("paper-a", { providerId: "alpaca" });
  });

  it("disables every connection command until the editor selects an owned connection", () => {
    render(<ProviderOwnershipPanel providerId="alpaca" displayName="Alpaca" connection={null} api={{}} />);
    for (const name of ["Preview migration", "Migrate to this connection", "Roll back migration", "Bind as host runtime credential"]) {
      expect(screen.getByRole("button", { name })).toBeDisabled();
    }
    expect(screen.getByLabelText("Alpaca ownership status")).toHaveTextContent(/Select an owned connection above/);
  });

  it("creates an owned connection without naming a tenant or actor", async () => {
    const create = vi.fn().mockResolvedValue({ connectionId: "paper-c", providerFamilyId: "alpaca" });
    render(<ProviderOwnershipPanel providerId="alpaca" displayName="Alpaca" connection={null} api={{ create }} />);
    fireEvent.change(screen.getByLabelText("Alpaca new connection id"), { target: { value: "paper-c" } });
    fireEvent.change(screen.getByLabelText("Alpaca new connection display name"), { target: { value: "Paper C" } });
    fireEvent.change(screen.getByLabelText("Alpaca new connection account"), { target: { value: "acct-c" } });
    fireEvent.submit(screen.getByRole("form", { name: "Create owned connection for Alpaca" }));
    await screen.findByText(/Owned connection paper-c created/);
    expect(create).toHaveBeenCalledWith({
      connection: { connectionId: "paper-c", providerFamilyId: "alpaca", displayName: "Paper C", externalAccountId: "acct-c" },
      environment: "paper"
    });
    expect(JSON.stringify(create.mock.calls[0][0])).not.toMatch(/tenant|actor/i);
  });
});
