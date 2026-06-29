import { render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { SecurityPassportEditorLauncher } from "@/components/meridian/security-passport-editor-launcher";

const SECURITY_ID = "11111111-1111-1111-1111-111111111111";

describe("SecurityPassportEditorLauncher", () => {
  it("shows a loading state, then mounts the editor with the fetched version", async () => {
    let resolve: (v: number) => void = () => {};
    const loadVersion = vi.fn().mockReturnValue(new Promise<number>((r) => { resolve = r; }));

    render(<SecurityPassportEditorLauncher securityId={SECURITY_ID} symbol="ACME" assetClass="Equity" loadVersion={loadVersion} />);

    expect(screen.getByRole("status")).toHaveTextContent(/loading passport for acme/i);
    resolve(7);

    await waitFor(() => expect(screen.getByTestId("security-passport-editor")).toBeInTheDocument());
    expect(loadVersion).toHaveBeenCalledWith(SECURITY_ID);
    // The version chip reflects the fetched concurrency token.
    expect(screen.getByText("v7")).toBeInTheDocument();
  });

  it("surfaces a load error without mounting the editor", async () => {
    const loadVersion = vi.fn().mockRejectedValue(new Error("snapshot unavailable"));

    render(<SecurityPassportEditorLauncher securityId={SECURITY_ID} symbol="ACME" loadVersion={loadVersion} />);

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent(/snapshot unavailable/i);
    expect(screen.queryByTestId("security-passport-editor")).not.toBeInTheDocument();
  });
});
