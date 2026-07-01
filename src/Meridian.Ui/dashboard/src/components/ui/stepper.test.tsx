import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { Stepper } from "@/components/ui/stepper";

const steps = [{ label: "Ingest" }, { label: "Mapping" }, { label: "Review" }];

describe("Stepper", () => {
  it("marks the active step and reports jumps", async () => {
    const user = userEvent.setup();
    const onStepChange = vi.fn();

    render(<Stepper steps={steps} activeStep={1} onStepChange={onStepChange} />);

    expect(screen.getByRole("tab", { name: /Mapping/ })).toHaveAttribute("aria-selected", "true");

    await user.click(screen.getByRole("tab", { name: /Review/ }));
    expect(onStepChange).toHaveBeenCalledWith(2);
  });

  it("renders completed steps with a check mark", () => {
    render(<Stepper steps={steps} activeStep={2} />);
    // Ingest (index 0) and Mapping (index 1) are complete.
    expect(screen.getAllByText("✓")).toHaveLength(2);
  });
});
