import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi, beforeEach } from "vitest";
import { ProviderSetupPanel } from "@/components/data/provider-setup-panel";
import type { ProviderModuleStatus, ProviderModuleCatalogueEntry } from "@/types/provider-setup";

const mockModule: ProviderModuleStatus = {
  moduleId: "alpaca",
  displayName: "Alpaca",
  enabled: true,
  priority: 100,
  isConfigured: true,
  isRunning: true,
  credentialStatus: [
    { key: "keyId", hasValue: true },
    { key: "secretKey", hasValue: false },
  ],
  capabilityNames: ["Streaming", "Brokerage"],
};

const mockCatalogueEntry: ProviderModuleCatalogueEntry = {
  moduleId: "new-provider",
  displayName: "New Provider",
  capabilityNames: ["Backfill"],
  credentialKeyHints: ["apiKey"],
  requiresExternalConfig: false,
  alreadyConfigured: false,
};

const mockUseProviderSetup = vi.hoisted(() => vi.fn());

vi.mock("@/lib/provider-setup/use-provider-setup", () => ({
  useProviderSetup: mockUseProviderSetup,
}));

const defaultHookReturn = {
  modules: [],
  catalogue: [],
  loading: false,
  error: null,
  pendingRestart: false,
  refresh: vi.fn().mockResolvedValue(undefined),
  addModule: vi.fn().mockResolvedValue(true),
  editModule: vi.fn().mockResolvedValue(true),
  removeModule: vi.fn().mockResolvedValue(true),
  toggleEnabled: vi.fn().mockResolvedValue(true),
  testModule: vi.fn().mockResolvedValue({ success: true, credentialConfigValid: true, connectionVerified: true }),
  triggerRestart: vi.fn().mockResolvedValue(true),
};

describe("ProviderSetupPanel", () => {
  beforeEach(() => {
    mockUseProviderSetup.mockReturnValue({ ...defaultHookReturn });
  });

  it("shows empty state when no modules are configured", () => {
    render(<ProviderSetupPanel />);
    expect(screen.getByText("No provider modules configured")).toBeInTheDocument();
  });

  it("shows a restart banner when pendingRestart is true", () => {
    mockUseProviderSetup.mockReturnValue({
      ...defaultHookReturn,
      pendingRestart: true,
    });

    render(<ProviderSetupPanel />);
    expect(screen.getByText("Restart required")).toBeInTheDocument();
  });

  it("shows an error banner when error is present", () => {
    mockUseProviderSetup.mockReturnValue({
      ...defaultHookReturn,
      error: "Network error",
    });

    render(<ProviderSetupPanel />);
    expect(screen.getByText("Failed to load provider modules")).toBeInTheDocument();
  });

  it("renders a configured module card", () => {
    mockUseProviderSetup.mockReturnValue({
      ...defaultHookReturn,
      modules: [mockModule],
    });

    render(<ProviderSetupPanel />);
    expect(screen.getByText("Alpaca")).toBeInTheDocument();
    expect(screen.getByText("alpaca")).toBeInTheDocument();
    expect(screen.getByText("Running")).toBeInTheDocument();
  });

  it("renders credential status dots for each credential", () => {
    mockUseProviderSetup.mockReturnValue({
      ...defaultHookReturn,
      modules: [mockModule],
    });

    render(<ProviderSetupPanel />);
    expect(screen.getByText("keyId")).toBeInTheDocument();
    expect(screen.getByText("secretKey")).toBeInTheDocument();
  });

  it("opens the add provider drawer when Add provider button is clicked", async () => {
    mockUseProviderSetup.mockReturnValue({
      ...defaultHookReturn,
      catalogue: [mockCatalogueEntry],
    });

    render(<ProviderSetupPanel />);
    const addButton = screen.getAllByRole("button", { name: /add provider/i })[0];
    await userEvent.click(addButton);

    await waitFor(() => {
      expect(screen.getByRole("dialog")).toBeInTheDocument();
      expect(screen.getByText("Add provider module")).toBeInTheDocument();
    });
  });

  it("calls testModule when the test button is clicked", async () => {
    const testModule = vi.fn().mockResolvedValue({ success: true, credentialConfigValid: true, connectionVerified: true });
    mockUseProviderSetup.mockReturnValue({
      ...defaultHookReturn,
      modules: [mockModule],
      testModule,
    });

    render(<ProviderSetupPanel />);
    const testBtn = screen.getByRole("button", { name: /test connection/i });
    await userEvent.click(testBtn);

    await waitFor(() => {
      expect(testModule).toHaveBeenCalledWith("alpaca");
    });
  });

  it("calls removeModule when the remove button is clicked", async () => {
    const removeModule = vi.fn().mockResolvedValue(true);
    mockUseProviderSetup.mockReturnValue({
      ...defaultHookReturn,
      modules: [mockModule],
      removeModule,
    });

    render(<ProviderSetupPanel />);
    const removeBtn = screen.getByRole("button", { name: /remove provider/i });
    await userEvent.click(removeBtn);

    await waitFor(() => {
      expect(removeModule).toHaveBeenCalledWith("alpaca");
    });
  });

  it("shows connected badge after successful test", async () => {
    mockUseProviderSetup.mockReturnValue({
      ...defaultHookReturn,
      modules: [mockModule],
      testModule: vi.fn().mockResolvedValue({ success: true, credentialConfigValid: true, connectionVerified: true }),
    });

    render(<ProviderSetupPanel />);
    await userEvent.click(screen.getByRole("button", { name: /test connection/i }));

    await waitFor(() => {
      expect(screen.getByText("Connected")).toBeInTheDocument();
    });
  });
});
