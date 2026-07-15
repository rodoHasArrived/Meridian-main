import { render, screen } from "@testing-library/react";
import { EntitySetupWizard } from "@/features/fund-structure/entity-setup-wizard";

describe("EntitySetupWizard", () => {
  it("renders setup, validation, review, and ownership preview sections", () => {
    render(<EntitySetupWizard />);

    expect(screen.getByRole("heading", { name: /entity setup wizard/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /validate draft/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /review and create/i })).toBeDisabled();
    expect(screen.getByText("Not validated")).toBeInTheDocument();
    expect(screen.getByText("Organization and fund")).toBeInTheDocument();
    expect(screen.getByText("Legal entity")).toBeInTheDocument();
    expect(screen.getByText("Advanced structure and handoff")).toBeInTheDocument();
    expect(screen.getByText(/draft validation summary/i)).toBeInTheDocument();
    expect(screen.getByText(/resulting graph \/ ownership preview/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/legal form/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/lifecycle status/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/beneficial owner/i)).toHaveValue("Flagship GP");
  });
});
