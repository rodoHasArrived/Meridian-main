import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { beforeEach, describe, expect, it } from "vitest";
import {
  ONBOARDING_TOUR_STEPS,
  ONBOARDING_JOURNEYS,
  buildOnboardingTourViewModel,
  resolveVisitedStepId
} from "./app-shell.onboarding";
import {
  ONBOARDING_STORAGE_KEY,
  emptyOnboardingState,
  readOnboardingState,
  withCompletedStep,
  withSelectedOnboardingJourney,
  writeOnboardingState,
  type OnboardingState
} from "@/lib/onboarding";
import {
  OnboardingCoachMark,
  OnboardingHeaderProgress,
  useOnboardingTour
} from "@/components/meridian/onboarding-tour";

function state(overrides: Partial<OnboardingState> = {}): OnboardingState {
  return { ...emptyOnboardingState(), ...overrides };
}

describe("onboarding persistence", () => {
  beforeEach(() => localStorage.clear());

  it("returns an empty state when nothing is stored", () => {
    expect(readOnboardingState()).toEqual({ version: 1, journeyId: "financial-operations", completedStepIds: [], dismissed: false });
  });

  it("round-trips and normalizes stored state", () => {
    writeOnboardingState(state({ completedStepIds: ["quote", "quote", "backtest"], dismissed: true }));
    const loaded = readOnboardingState();
    expect(loaded.completedStepIds).toEqual(["quote", "backtest"]);
    expect(loaded.dismissed).toBe(true);
  });

  it("tolerates corrupt payloads", () => {
    localStorage.setItem(ONBOARDING_STORAGE_KEY, "{not json");
    expect(readOnboardingState()).toEqual(emptyOnboardingState());
  });

  it("withCompletedStep is idempotent and immutable", () => {
    const base = state({ completedStepIds: ["quote"] });
    expect(withCompletedStep(base, "quote")).toBe(base);
    expect(withCompletedStep(base, "backtest").completedStepIds).toEqual(["quote", "backtest"]);
  });

  it("selects a task journey without discarding progress", () => {
    const base = state({ completedStepIds: ["financial-operations:import"] });
    const next = withSelectedOnboardingJourney(base, "administration");
    expect(next.journeyId).toBe("administration");
    expect(next.completedStepIds).toEqual(base.completedStepIds);
  });
});

describe("buildOnboardingTourViewModel", () => {
  it("marks the first incomplete step active and reports progress", () => {
    const vm = buildOnboardingTourViewModel({
      state: state({ completedStepIds: ["financial-operations:import"] }),
      pathname: "/accounting/statement-import"
    });
    expect(vm.visible).toBe(true);
    expect(vm.completedCount).toBe(1);
    expect(vm.totalCount).toBe(ONBOARDING_TOUR_STEPS.length);
    expect(vm.progressLabel).toBe(`1 / ${ONBOARDING_TOUR_STEPS.length}`);
    expect(vm.steps[0].status).toBe("complete");
    expect(vm.steps[1].status).toBe("active");
    expect(vm.steps[2].status).toBe("upcoming");
    expect(vm.steps.find((s) => s.id === "financial-operations:import")?.isCurrentRoute).toBe(true);
  });

  it("hides once every step is complete", () => {
    const vm = buildOnboardingTourViewModel({
      pathname: "/",
      state: state({ completedStepIds: ONBOARDING_TOUR_STEPS.map((s) => s.id) })
    });
    expect(vm.allComplete).toBe(true);
    expect(vm.visible).toBe(false);
    expect(vm.progressFraction).toBe(1);
  });

  it("hides when dismissed even with steps outstanding", () => {
    const vm = buildOnboardingTourViewModel({ pathname: "/", state: state({ dismissed: true }) });
    expect(vm.visible).toBe(false);
    expect(vm.dismissed).toBe(true);
  });

  it("resolves the visited step id from a route, ignoring trailing slashes", () => {
    expect(resolveVisitedStepId("/accounting/statement-import")).toBe("financial-operations:import");
    expect(resolveVisitedStepId("/accounting/ledger/")).toBe("financial-operations:validate");
    expect(resolveVisitedStepId("/data/quotes", "trading-portfolio")).toBe("trading-portfolio:quotes");
    expect(resolveVisitedStepId("/nowhere")).toBeNull();
  });
});

describe("onboarding tour rendering", () => {
  beforeEach(() => localStorage.clear());

  function Harness({ initial }: { initial: string }) {
    return (
      <MemoryRouter initialEntries={[initial]}>
        <TourHost />
        <Routes>
          <Route path="*" element={<div>screen</div>} />
        </Routes>
      </MemoryRouter>
    );
  }

  function TourHost() {
    const controller = useOnboardingTour();
    return (
      <>
        <OnboardingHeaderProgress controller={controller} />
        <OnboardingCoachMark controller={controller} />
      </>
    );
  }

  it("auto-completes a step when its route is visited", async () => {
    const user = userEvent.setup();
    render(<Harness initial="/accounting/statement-import" />);
    await waitFor(() => expect(readOnboardingState().completedStepIds).toContain("financial-operations:import"));
    // Progress is visible once the operator opens the coach mark from the ring.
    await user.click(screen.getByRole("button", { name: /Getting started/ }));
    expect(screen.getByText(`1 / ${ONBOARDING_TOUR_STEPS.length} steps complete`)).toBeInTheDocument();
  });

  it("switches to a role-relevant journey and updates its guidance", async () => {
    const user = userEvent.setup();
    render(<Harness initial="/somewhere" />);
    await user.click(screen.getByRole("button", { name: /Getting started/ }));

    await user.selectOptions(screen.getByLabelText("Choose your task journey"), "administration");

    expect(screen.getByText(ONBOARDING_JOURNEYS.find((journey) => journey.id === "administration")!.description))
      .toBeInTheDocument();
    expect(readOnboardingState().journeyId).toBe("administration");
  });

  it("stays docked to the header ring until the operator opens it", () => {
    render(<Harness initial="/somewhere" />);
    // The coach mark floats over route content, so it must never auto-open.
    expect(screen.queryByRole("region", { name: "Getting started tour" })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Getting started/ })).toBeInTheDocument();
  });

  it("dismisses permanently via Skip tour", async () => {
    const user = userEvent.setup();
    render(<Harness initial="/somewhere" />);
    await user.click(screen.getByRole("button", { name: /Getting started/ }));
    expect(screen.getByRole("region", { name: "Getting started tour" })).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Skip getting-started tour" }));
    expect(screen.queryByRole("region", { name: "Getting started tour" })).not.toBeInTheDocument();
    expect(readOnboardingState().dismissed).toBe(true);
  });

  it("collapses to the header ring without dismissing", async () => {
    const user = userEvent.setup();
    render(<Harness initial="/somewhere" />);
    const ring = screen.getByRole("button", { name: /Getting started/ });
    await user.click(ring);
    await user.click(screen.getByRole("button", { name: "Collapse getting-started tour" }));
    expect(screen.queryByRole("region", { name: "Getting started tour" })).not.toBeInTheDocument();
    // Ring remains and can re-open the card.
    await user.click(ring);
    expect(screen.getByRole("region", { name: "Getting started tour" })).toBeInTheDocument();
  });
});
