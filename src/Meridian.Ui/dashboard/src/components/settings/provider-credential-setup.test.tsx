import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { createApiErrorFromResponseBody } from "@/lib/api-errors";
import { ProviderCredentialSetup } from "@/components/settings/provider-credential-setup";

const providers = {
  polygon: {
    name: "Polygon.io",
    description: "Realtime and historical market data.",
    type: "data_provider" as const,
    supportsDemo: true,
    fields: [
      {
        name: "apiKey",
        label: "API key",
        required: true,
        type: "password" as const,
        help: "Stored server-side after verification."
      }
    ],
    docs: "https://example.test/polygon",
    supportedDataTypes: ["Quotes", "Bars"]
  }
};

describe("ProviderCredentialSetup", () => {
  it("renders structured connection-test failures", async () => {
    const user = userEvent.setup();
    render(
      <ProviderCredentialSetup
        providers={providers}
        onSave={vi.fn().mockResolvedValue(undefined)}
        onTest={vi.fn().mockRejectedValue(
          createApiErrorFromResponseBody(
            "/api/providers/test",
            422,
            JSON.stringify({
              title: "Credential verification failed",
              detail: "The provider rejected the supplied API key.",
              errors: {
                apiKey: ["Verify the key against the active workspace environment."]
              }
            })
          )
        )}
      />
    );

    await user.click(screen.getByRole("button", { name: /polygon\.io/i }));
    await user.type(screen.getByLabelText(/api key/i), "demo-key");
    await user.click(screen.getByRole("button", { name: /test connection/i }));

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("The provider rejected the supplied API key.");
    expect(within(alert).getByText("Endpoint returned 422 for /api/providers/test.")).toBeInTheDocument();
    expect(within(alert).getByText("Credential verification failed")).toBeInTheDocument();
    expect(within(alert).getByText("apiKey: Verify the key against the active workspace environment.")).toBeInTheDocument();
  });

  it("names the provider credential dialog and password visibility control", async () => {
    const user = userEvent.setup();
    render(
      <ProviderCredentialSetup
        providers={providers}
        onSave={vi.fn().mockResolvedValue(undefined)}
        onTest={vi.fn().mockResolvedValue(true)}
      />
    );

    await user.click(screen.getByRole("button", { name: /polygon\.io/i }));

    const dialog = screen.getByRole("dialog", { name: /polygon\.io credentials/i });
    expect(dialog).toHaveAccessibleDescription("Realtime and historical market data.");

    const toggle = within(dialog).getByRole("button", { name: /show api key/i });
    expect(toggle).toHaveAttribute("aria-pressed", "false");

    await user.click(toggle);

    expect(within(dialog).getByRole("button", { name: /hide api key/i })).toHaveAttribute("aria-pressed", "true");
  });

  it("renders structured save failures without closing the dialog", async () => {
    const user = userEvent.setup();
    render(
      <ProviderCredentialSetup
        providers={providers}
        onSave={vi.fn().mockRejectedValue(
          createApiErrorFromResponseBody(
            "/api/providers/save",
            409,
            JSON.stringify({
              title: "Credential save blocked",
              detail: "The provider record is locked by another operator session.",
              errors: {
                provider: ["Retry after the active provider update completes."]
              }
            })
          )
        )}
        onTest={vi.fn().mockResolvedValue(true)}
      />
    );

    await user.click(screen.getByRole("button", { name: /polygon\.io/i }));
    await user.type(screen.getByLabelText(/api key/i), "demo-key");
    await user.click(screen.getByRole("button", { name: /save credentials/i }));

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("The provider record is locked by another operator session.");
    expect(within(alert).getByText("Endpoint returned 409 for /api/providers/save.")).toBeInTheDocument();
    expect(within(alert).getByText("Credential save blocked")).toBeInTheDocument();
    expect(within(alert).getByText("provider: Retry after the active provider update completes.")).toBeInTheDocument();
    expect(screen.getByRole("dialog")).toBeInTheDocument();
  });
});
