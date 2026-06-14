import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, render } from "@testing-library/react";
import { BackfillValidationDashboard } from "@/components/data/backfill-validation-dashboard";

beforeEach(() => {
  vi.clearAllMocks();
});

afterEach(() => {
  cleanup();
});

const completeValidation = {
  symbol: "AAPL",
  isComplete: true,
  totalDays: 252,
  coveredDays: 252,
  completeness: 1.0,
  status: "Complete",
  firstDataPoint: "2024-01-02",
  lastDataPoint: "2024-12-31",
};

const poorValidation = {
  symbol: "MSFT",
  isComplete: false,
  totalDays: 252,
  coveredDays: 150,
  completeness: 0.595,
  status: "Incomplete",
  gaps: ["2024-03-15", "2024-06-20", "2024-09-05", "2024-11-11"],
};

const summary = {
  complete: 1,
  good: 0,
  poor: 1,
  average: 0.798,
};

describe("BackfillValidationDashboard", () => {
  it("renders empty state with descriptive message when no validations", () => {
    render(<BackfillValidationDashboard />);
    expect(screen.getByText(/No symbols configured yet/i)).toBeInTheDocument();
  });

  it("renders summary card with completeness metrics", () => {
    render(<BackfillValidationDashboard validations={[completeValidation, poorValidation]} summary={summary} />);
    expect(screen.getByText("Backfill Completeness")).toBeInTheDocument();
    expect(screen.getByText("Complete (≥95%)")).toBeInTheDocument();
    expect(screen.getByText("Poor (<80%)")).toBeInTheDocument();
    expect(screen.getByText("80%")).toBeInTheDocument();
  });

  it("renders symbol validation row with completeness percentage", () => {
    render(<BackfillValidationDashboard validations={[completeValidation]} />);
    expect(screen.getByText("AAPL")).toBeInTheDocument();
    expect(screen.getByText("100%")).toBeInTheDocument();
    expect(screen.getByText("252 of 252 days")).toBeInTheDocument();
  });

  it("renders date range when firstDataPoint and lastDataPoint are present", () => {
    render(<BackfillValidationDashboard validations={[completeValidation]} />);
    expect(screen.getByText("2024-01-02 to 2024-12-31")).toBeInTheDocument();
  });

  it("renders gap summary disclosure with first 3 gaps", () => {
    render(<BackfillValidationDashboard validations={[poorValidation]} />);
    expect(screen.getByText(/4 gaps? detected/i)).toBeInTheDocument();
    expect(screen.getByText("2024-03-15")).toBeInTheDocument();
    expect(screen.getByText("2024-06-20")).toBeInTheDocument();
    expect(screen.getByText("2024-09-05")).toBeInTheDocument();
    expect(screen.getByText(/and 1 more/i)).toBeInTheDocument();
  });

  it("renders multiple symbols as separate rows", () => {
    render(<BackfillValidationDashboard validations={[completeValidation, poorValidation]} />);
    expect(screen.getByText("AAPL")).toBeInTheDocument();
    expect(screen.getByText("MSFT")).toBeInTheDocument();
  });

  it("refresh button has accessible aria-label and calls onRefresh", async () => {
    const onRefresh = vi.fn().mockResolvedValue(undefined);
    render(
      <BackfillValidationDashboard
        validations={[completeValidation]}
        summary={summary}
        onRefresh={onRefresh}
      />
    );
    const refreshBtn = screen.getByRole("button", { name: /refresh backfill data/i });
    expect(refreshBtn).toBeInTheDocument();
    await userEvent.click(refreshBtn);
    expect(onRefresh).toHaveBeenCalledOnce();
  });

  it("refresh button is disabled while loading", () => {
    render(
      <BackfillValidationDashboard
        validations={[completeValidation]}
        summary={summary}
        onRefresh={vi.fn()}
        isLoading
      />
    );
    const refreshBtn = screen.getByRole("button", { name: /refresh backfill data/i });
    expect(refreshBtn).toBeDisabled();
  });

  it("does not render summary card when summary prop is absent", () => {
    render(<BackfillValidationDashboard validations={[completeValidation]} />);
    expect(screen.queryByText("Backfill Completeness")).not.toBeInTheDocument();
  });
});
