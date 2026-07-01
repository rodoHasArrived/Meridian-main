import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { EvidenceLink } from "./evidence-link";

describe("EvidenceLink", () => {
  it("renders as an anchor when href is set", () => {
    render(<EvidenceLink label="Recon pack" status="Ready" route="evidence://recon/2026-06" href="#recon" />);
    const link = screen.getByRole("link");
    expect(link).toHaveAttribute("href", "#recon");
    expect(screen.getByText("Recon pack")).toBeInTheDocument();
    expect(screen.getByText("evidence://recon/2026-06")).toBeInTheDocument();
  });

  it("renders as a button and fires onOpen when clicked", async () => {
    const onOpen = vi.fn();
    render(<EvidenceLink label="Approval" status="Missing" onOpen={onOpen} />);
    const button = screen.getByRole("button");
    await userEvent.click(button);
    expect(onOpen).toHaveBeenCalledOnce();
  });

  it("tints the status dot by the resolved severity", () => {
    const { container } = render(<EvidenceLink label="Break" status="Blocked" />);
    expect(container.querySelector(".mds-evidence--blocked")).not.toBeNull();
  });
});
