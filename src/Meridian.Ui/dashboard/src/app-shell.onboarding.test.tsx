import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { beforeEach, describe, expect, it } from "vitest";
import {
  ONBOARDING_TOUR_STEPS,
  buildOnboardingTourViewModel,
  resolveVisitedStepId
} from "./app-shell.onboarding";
import {
  ONBOARDING_STORAGE_KEY,
  emptyOnboardingState,
  readOnboardingState,
  withCompletedStep,
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
    expect(readOnboardingState()).toEqual({ version: 1, completedStepIds: [], dismissed: false });
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
});

describe("buildOnboardingTourViewModel", () => {
  it("marks the first incomplete step active and reports progress", () => {
    const vm = buildOnboardingTourViewModel({
      pathname: "/data/quotes",
      state: state({ completedStepIds: ["quote"] })
    });
    expect(vm.visible).toBe(true);
    expect(vm.completedCount).toBe(1);
    expect(vm.totalCount).toBe(ONBOARDING_TOUR_STEPS.length);
    expect(vm.progressLabel).toBe(`1 / ${ONBOARDING_TOUR_STEPS.length}`);
    expect(vm.steps[0].status).toBe("complete");
    expect(vm.steps[1].status).toBe("active");
    expect(vm.steps[2].status).toBe("upcoming");
    expect(vm.steps.find((s) => s.id === "quote")?.isCurrentRoute).toBe(true);
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
    expect(resolveVisitedStepId("/data/quotes")).toBe("quote");
    expect(resolveVisitedStepId("/accounting/ledger/")).toBe("ledger");
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
    render(<Harness initial="/data/quotes" />);
    // The coach-mark shows overall progress; visiting /data/quotes completes "quote".
    await waitFor(() => expect(screen.getByText(`1 / ${ONBOARDING_TOUR_STEPS.length} steps complete`)).toBeInTheDocument());
    expect(readOnboardingState().completedStepIds).toContain("quote");
  });

  it("dismisses permanently via Skip tour", async () => {
    const user = userEvent.setup();
    render(<Harness initial="/somewhere" />);
    expect(screen.getByRole("region", { name: "Getting started tour" })).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Skip getting-started tour" }));
    expect(screen.queryByRole("region", { name: "Getting started tour" })).not.toBeInTheDocument();
    expect(readOnboardingState().dismissed).toBe(true);
  });

  it("collapses to the header ring without dismissing", async () => {
    const user = userEvent.setup();
    render(<Harness initial="/somewhere" />);
    await user.click(screen.getByRole("button", { name: "Collapse getting-started tour" }));
    expect(screen.queryByRole("region", { name: "Getting started tour" })).not.toBeInTheDocument();
    // Ring remains and can re-open the card.
    const ring = screen.getByRole("button", { name: /Getting started/ });
    await user.click(ring);
    expect(screen.getByRole("region", { name: "Getting started tour" })).toBeInTheDocument();
  });
});
