import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import { TechnicalDetails } from "@/components/ui/technical-details";

describe("TechnicalDetails", () => {
  it("keeps technical content collapsed until the operator opens it", async () => {
    const user = userEvent.setup();
    render(
      <TechnicalDetails label="Audit details" description="Retained proof identifiers.">
        <code>case-8e19</code>
      </TechnicalDetails>
    );

    const disclosure = screen.getByText("Audit details").closest("details");
    expect(disclosure).not.toHaveAttribute("open");

    await user.click(screen.getByText("Audit details"));

    expect(disclosure).toHaveAttribute("open");
    expect(screen.getByText("case-8e19")).toBeInTheDocument();
  });

  it("supports an intentionally open advanced section", () => {
    render(
      <TechnicalDetails label="Advanced" open>
        <span>Rule expression</span>
      </TechnicalDetails>
    );

    expect(screen.getByText("Advanced").closest("details")).toHaveAttribute("open");
  });
});
