import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { OwnedProviderCredentialPanel } from "./settings-owned-provider-credentials";
import type { SettingsProviderConnectionRow } from "./settings-screen.view-model";
import type { ProviderConnectionRow, ProviderRoutingConnection, SessionInfo } from "@/types";

const api = vi.hoisted(() => ({ getProviderRoutingConnections: vi.fn(), getProviderConnections: vi.fn(), putProviderCredentials: vi.fn(), verifyProviderConnection: vi.fn(), deleteProviderCredentials: vi.fn() }));
vi.mock("@/lib/api", () => api);
const ownership = vi.hoisted(() => ({
  createOwnedProviderConnection: vi.fn(), bindProviderRuntimeCredential: vi.fn(), previewProviderCredentialMigration: vi.fn(), migrateProviderCredential: vi.fn(),
  rollbackProviderCredential: vi.fn(), getProviderRuntimeCredentialStatus: vi.fn(), applyProviderRuntimeCredential: vi.fn(), finalizeProviderCredentialMigration: vi.fn()
}));
vi.mock("@/lib/api/provider-ownership.api", () => ownership);
const session = { displayName: "Operator", role: "Admin", environment: "paper", activeWorkspace: "settings", commandCount: 0 } as SessionInfo;
const row = { providerId: "alpaca", displayName: "Alpaca", affectedWorkflowsLabel: "Paper orders" } as SettingsProviderConnectionRow;
const owned = (id: string): ProviderRoutingConnection => ({ connectionId: id, providerFamilyId: "alpaca", displayName: id, tenantId: "tenant-a", externalAccountId: `account-${id}`, credentialEnvironment: "paper", enabled: true } as ProviderRoutingConnection);
const status = (id: string): ProviderConnectionRow => ({ providerId: "alpaca", externalAccountId: `account-${id}`, environment: "paper", credentialState: "Configured", verificationState: "NotVerified", credentialFields: [
  { name: "KeyId", label: "Key ID", required: true, inputKind: "Text" },
  { name: "SecretKey", label: "Secret key", required: true, inputKind: "Password" }
] } as ProviderConnectionRow);
const deferred = <T,>() => { let resolve!: (value: T) => void; const promise = new Promise<T>(done => { resolve = done; }); return { promise, resolve }; };
async function select(id = "paper-a") {
  await screen.findByRole("option", { name: new RegExp(id) });
  fireEvent.change(screen.getByLabelText("Alpaca owned connection"), { target: { value: id } });
  await screen.findByText("Selected connection status loaded.");
}
async function fill() {
  await userEvent.click(screen.getByRole("button", { name: "Edit Alpaca credentials" }));
  fireEvent.change(screen.getByLabelText("Alpaca Key ID"), { target: { value: "key" } });
  fireEvent.change(screen.getByLabelText("Alpaca Secret key"), { target: { value: "secret" } });
}
beforeEach(() => {
  vi.clearAllMocks();
  api.getProviderRoutingConnections.mockResolvedValue([owned("paper-a"), owned("paper-b")]);
  api.getProviderConnections.mockImplementation(({ connectionId }) => Promise.resolve([status(connectionId)]));
  api.putProviderCredentials.mockResolvedValue({ providerId: "alpaca", credentialState: "Configured" });
  api.verifyProviderConnection.mockResolvedValue({ providerId: "alpaca", success: true });
  api.deleteProviderCredentials.mockResolvedValue({ providerId: "alpaca", credentialState: "Missing" });
});

describe("owned provider credential editor", () => {
  it.each(["live", "LIVE", " Live ", "production", "Production", " production "])(
    "requires acknowledgement for retained environment %j while preserving its exact scoped request", async environment => {
      const retained = { ...owned("retained-live"), credentialEnvironment: environment };
      api.getProviderRoutingConnections.mockResolvedValue([retained]);
      api.getProviderConnections.mockImplementation(({ connectionId }) => Promise.resolve([{ ...status(connectionId), environment }]));
      render(<OwnedProviderCredentialPanel row={row} session={session} />);
      await select("retained-live"); await fill();
      const save = screen.getByRole("button", { name: "Save Alpaca credentials" });
      expect(save).toBeDisabled();
      await userEvent.click(save);
      expect(api.putProviderCredentials).not.toHaveBeenCalled();
      await userEvent.click(screen.getByRole("checkbox", { name: "I understand this save changes the selected live connection credentials." }));
      expect(save).toBeEnabled();
      await userEvent.click(save);
      await screen.findByText("Credentials saved (Configured).");
      expect(api.putProviderCredentials).toHaveBeenCalledWith("alpaca", {
        credentials: { KeyId: "key", SecretKey: "secret" }, environment
      }, expect.objectContaining({ connectionId: "retained-live", allowDevelopmentFallback: false }));
    }
  );

  it("loads and saves a retained provider alias through the canonical provider without rewriting its connection", async () => {
    api.getProviderRoutingConnections.mockResolvedValue([{ ...owned("paper-a"), providerFamilyId: "alpaca-brokerage" }]);
    render(<OwnedProviderCredentialPanel row={row} session={session} />);
    await select(); await fill();
    await userEvent.click(screen.getByRole("button", { name: "Save Alpaca credentials" }));
    await screen.findByText("Credentials saved (Configured).");
    expect(api.putProviderCredentials).toHaveBeenCalledWith("alpaca", {
      credentials: { KeyId: "key", SecretKey: "secret" }, environment: "paper"
    }, expect.objectContaining({ connectionId: "paper-a", allowDevelopmentFallback: false }));
    expect(api.getProviderConnections.mock.calls.every(([options]) => options.connectionId === "paper-a")).toBe(true);
  });

  it.each([
    { providerFamilyId: "alpaca" }, { tenantId: "other-tenant" },
    { externalAccountId: "other-account" }, { credentialEnvironment: "live" },
    { connectionId: "PAPER-A" }
  ])("blocks changed retained ownership %j even when the provider alias still matches", async changed => {
    const retained = { ...owned("paper-a"), providerFamilyId: "alpaca-brokerage" };
    api.getProviderRoutingConnections.mockResolvedValue([retained]);
    render(<OwnedProviderCredentialPanel row={row} session={session} />);
    await select(); await fill();
    api.getProviderRoutingConnections.mockResolvedValue([{ ...retained, ...changed }]);
    await userEvent.click(screen.getByRole("button", { name: "Save Alpaca credentials" }));
    await screen.findByText(/Selected ownership changed or was removed/);
    expect(api.putProviderCredentials).not.toHaveBeenCalled();
    expect(screen.getByLabelText("Alpaca owned connection")).toHaveValue("");
    expect(screen.queryByLabelText("Alpaca Secret key")).not.toBeInTheDocument();
  });

  it("requires explicit selection and scopes status, save, verification and confirmed deletion", async () => {
    render(<OwnedProviderCredentialPanel row={row} session={session} />);
    await screen.findByRole("option", { name: /paper-a/ });
    expect(screen.getByRole("button", { name: "Save Alpaca credentials" })).toBeDisabled();
    expect(api.getProviderConnections).not.toHaveBeenCalled();
    await select(); await fill();
    expect(screen.queryByRole("checkbox", { name: "I understand this save changes the selected live connection credentials." })).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Save Alpaca credentials" }));
    await screen.findByText("Credentials saved (Configured).");
    expect(api.putProviderCredentials).toHaveBeenCalledWith("alpaca", { credentials: { KeyId: "key", SecretKey: "secret" }, environment: "paper" }, expect.objectContaining({ connectionId: "paper-a", allowDevelopmentFallback: false }));
    await userEvent.click(screen.getByRole("button", { name: "Re-verify Alpaca connection" }));
    await screen.findByText("Verification succeeded.");
    await userEvent.click(screen.getByRole("button", { name: "Clear credentials for Alpaca" }));
    expect(api.deleteProviderCredentials).not.toHaveBeenCalled();
    await userEvent.click(screen.getByRole("button", { name: "Confirm clear for Alpaca" }));
    await screen.findByText("Credentials cleared.");
    expect(api.verifyProviderConnection).toHaveBeenCalledWith("alpaca", expect.objectContaining({ connectionId: "paper-a" }));
    expect(api.deleteProviderCredentials).toHaveBeenCalledWith("alpaca", expect.objectContaining({ connectionId: "paper-a" }));
    expect(api.getProviderConnections.mock.calls.every(([options]) => options.connectionId === "paper-a" && !options.allowDevelopmentFallback)).toBe(true);
  });
  it("never falls back for incomplete, duplicate or mismatched connection records", async () => {
    api.getProviderRoutingConnections.mockResolvedValue([owned("duplicate"), owned("DUPLICATE"), { ...owned("foreign-family"), providerFamilyId: "polygon" }, { ...owned("incomplete"), tenantId: null }]);
    render(<OwnedProviderCredentialPanel row={row} session={session} />);
    await screen.findByText(/No owned connection is available/);
    expect(screen.getByRole("button", { name: "Save Alpaca credentials" })).toBeDisabled();
    expect(screen.getAllByRole("option")).toHaveLength(1);
    expect(api.getProviderConnections).not.toHaveBeenCalled();
    expect(api.putProviderCredentials).not.toHaveBeenCalled();
  });
  it.each(["account", "environment", "provider", "ambiguous", "foreign"])("blocks a %s status mismatch/refusal", async kind => {
    api.getProviderConnections.mockImplementation(() => kind === "foreign" ? Promise.reject(new Error("Forbidden")) : Promise.resolve(kind === "ambiguous" ? [status("paper-a"), status("paper-a")] : [{ ...status("paper-a"), ...(kind === "account" ? { externalAccountId: "other" } : kind === "environment" ? { environment: "live" } : { providerId: "polygon" }) }]));
    render(<OwnedProviderCredentialPanel row={row} session={session} />);
    await screen.findByRole("option", { name: /paper-a/ });
    fireEvent.change(screen.getByLabelText("Alpaca owned connection"), { target: { value: "paper-a" } });
    await waitFor(() => expect(screen.getByRole("status")).not.toHaveTextContent("Loading"));
    expect(screen.getByRole("button", { name: "Save Alpaca credentials" })).toBeDisabled();
    expect(api.putProviderCredentials).not.toHaveBeenCalled();
  });
  it("discards secrets and late save results when the selected connection changes", async () => {
    const save = deferred<{ providerId: string; credentialState: string }>(); api.putProviderCredentials.mockReturnValue(save.promise);
    render(<OwnedProviderCredentialPanel row={row} session={session} />);
    await select(); await fill();
    await userEvent.click(screen.getByRole("button", { name: "Save Alpaca credentials" }));
    await waitFor(() => expect(api.putProviderCredentials).toHaveBeenCalled());
    fireEvent.change(screen.getByLabelText("Alpaca owned connection"), { target: { value: "paper-b" } });
    await screen.findByText("Selected connection status loaded.");
    await act(async () => save.resolve({ providerId: "alpaca", credentialState: "Configured" }));
    expect(screen.queryByText("Credentials saved (Configured).")).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Edit Alpaca credentials" }));
    expect(screen.getByLabelText("Alpaca Key ID")).toHaveValue("");
    expect(screen.getByLabelText("Alpaca Secret key")).toHaveValue("");
  });
  it("rejects a connection removed after selection before mutation", async () => {
    render(<OwnedProviderCredentialPanel row={row} session={session} />);
    await select(); await fill();
    api.getProviderRoutingConnections.mockResolvedValue([owned("paper-b")]);
    await userEvent.click(screen.getByRole("button", { name: "Save Alpaca credentials" }));
    await screen.findByText(/Selected ownership changed or was removed/);
    expect(api.putProviderCredentials).not.toHaveBeenCalled();
    expect(screen.getByLabelText("Alpaca owned connection")).toHaveValue("");
  });
  it("keeps the selected ownership and its migration receipt across the refresh a migration triggers, so verify then finalize can follow", async () => {
    const receipt = { providerId: "alpaca", scope: { tenantId: "tenant-a", connectionId: "paper-a", externalAccountId: "account-paper-a", environment: "paper" }, credentialGeneration: "gen-1", actor: "operator-a", migratedAt: "2026-10-31T00:00:00Z", rolledBack: false };
    ownership.previewProviderCredentialMigration.mockResolvedValue({ providerId: "alpaca", displayName: "Alpaca", credentialState: "Configured", credentialSource: "LocalEncryptedStore", verificationState: "NotVerified", missingFields: [], presentFields: ["KeyId"], auditMetadata: { credentialGeneration: "gen-1" } });
    ownership.migrateProviderCredential.mockImplementation(async (_c: string, request: { migrationId: string }) => ({ ...receipt, migrationId: request.migrationId }));
    ownership.finalizeProviderCredentialMigration.mockImplementation(async (_c: string, request: { migrationId: string }) => ({ ...receipt, migrationId: request.migrationId, finalizedBy: "controller", finalizedAt: "2026-11-01T00:00:00Z" }));
    render(<OwnedProviderCredentialPanel row={row} session={session} />);
    await select();
    const listings = api.getProviderRoutingConnections.mock.calls.length;

    fireEvent.click(screen.getByRole("button", { name: "Preview migration" }));
    await screen.findByText(/generation gen-1 can be migrated/);
    fireEvent.click(screen.getByRole("button", { name: "Migrate to this connection" }));
    await screen.findByText(/retained by operator-a at generation gen-1/);
    await screen.findByText("Owned connections refreshed; selected connection status reloaded.");
    expect(api.getProviderRoutingConnections.mock.calls.length).toBeGreaterThan(listings);
    expect(screen.getByLabelText("Alpaca owned connection")).toHaveValue("paper-a");
    expect(screen.getByLabelText("Alpaca migration receipt")).toHaveTextContent(/· migrated · generation gen-1 · paper-a/);

    await userEvent.click(screen.getByRole("button", { name: "Re-verify Alpaca connection" }));
    await screen.findByText("Verification succeeded.");
    expect(api.verifyProviderConnection).toHaveBeenCalledWith("alpaca", expect.objectContaining({ connectionId: "paper-a" }));
    expect(screen.getByLabelText("Alpaca migration receipt")).toBeInTheDocument();
    const finalize = screen.getByRole("button", { name: "Finalize migration" });
    expect(finalize).toBeEnabled();
    fireEvent.click(finalize);
    await screen.findByText(/finalized by controller; rollback has ended/);
    expect(ownership.finalizeProviderCredentialMigration).toHaveBeenCalledWith("paper-a", { providerId: "alpaca", migrationId: ownership.migrateProviderCredential.mock.calls[0][1].migrationId });
    expect(screen.getByLabelText("Alpaca owned connection")).toHaveValue("paper-a");

    api.getProviderRoutingConnections.mockResolvedValue([owned("paper-b")]);
    ownership.getProviderRuntimeCredentialStatus.mockResolvedValue({ providerId: "alpaca", connectionId: "paper-a", isBound: false, pendingRestart: false, detail: "Not bound." });
    fireEvent.click(screen.getByRole("button", { name: "Check running instances" }));
    await screen.findByText(/Selected ownership changed or was removed/);
    expect(screen.getByLabelText("Alpaca owned connection")).toHaveValue("");
  });
  it("retains selection through ordinary data refreshes but blocks when the session disappears", async () => {
    const view = render(<OwnedProviderCredentialPanel row={row} session={session} />);
    await select();
    view.rerender(<OwnedProviderCredentialPanel row={row} session={{ ...session, commandCount: 1 }} />);
    expect(screen.getByLabelText("Alpaca owned connection")).toHaveValue("paper-a");
    expect(screen.getByRole("button", { name: "Edit Alpaca credentials" })).toBeEnabled();
    view.rerender(<OwnedProviderCredentialPanel row={row} session={null} />);
    expect(screen.getByLabelText("Alpaca owned connection")).toHaveValue("");
    expect(screen.getByRole("button", { name: "Save Alpaca credentials" })).toBeDisabled();
  });
  it("invalidates selection on exposed session change and ignores delayed status", async () => {
    const pending = deferred<ProviderConnectionRow[]>(); api.getProviderConnections.mockReturnValue(pending.promise);
    const view = render(<OwnedProviderCredentialPanel row={row} session={session} />);
    await screen.findByRole("option", { name: /paper-a/ });
    fireEvent.change(screen.getByLabelText("Alpaca owned connection"), { target: { value: "paper-a" } });
    view.rerender(<OwnedProviderCredentialPanel row={row} session={{ ...session, displayName: "Different operator" }} />);
    await act(async () => pending.resolve([status("paper-a")]));
    expect(screen.getByLabelText("Alpaca owned connection")).toHaveValue("");
    expect(screen.getByRole("button", { name: "Edit Alpaca credentials" })).toBeDisabled();
  });
});
