import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { axe } from "jest-axe";
import {
  resetSectionDescriptionChoices,
  SectionDescription,
  SectionDescriptionPreferences,
  useSectionDescriptionsHidden
} from "./section-description";

function Toggle() {
  const [hidden, setHidden] = useSectionDescriptionsHidden();
  return <button type="button" onClick={() => setHidden(!hidden)}>{hidden ? "Show descriptions" : "Hide descriptions"}</button>;
}

function Help({ operator = "Operator A" }: { operator?: string | null }) {
  return <SectionDescriptionPreferences operatorKey={operator}>
    <Toggle />
    <SectionDescription descriptionKey="queue" label="About the queue">Inspect source evidence before acting.</SectionDescription>
    <SectionDescription descriptionKey="journal" label="About the journal">Post only balanced entries.</SectionDescription>
  </SectionDescriptionPreferences>;
}

describe("SectionDescription", () => {
  beforeEach(() => { localStorage.clear(); resetSectionDescriptionChoices(); });

  it("shows every description with no inline hide control, on every visit", () => {
    const first = render(<Help />);
    expect(screen.getByText("Inspect source evidence before acting.")).toBeVisible();
    expect(screen.queryByRole("button", { name: /hide description:/i })).not.toBeInTheDocument();
    first.unmount();

    render(<Help />);
    // Seeing a description once no longer collapses it on the next visit.
    expect(screen.getByText("Inspect source evidence before acting.")).toBeVisible();
    expect(screen.getByText("Post only balanced entries.")).toBeVisible();
  });

  it("moves all descriptions behind an information icon when the operator turns them off", async () => {
    const user = userEvent.setup();
    const view = render(<Help />);
    await user.click(screen.getByRole("button", { name: "Hide descriptions" }));

    const summaries = view.container.querySelectorAll("summary");
    expect(summaries).toHaveLength(2);
    expect(screen.getByText("Inspect source evidence before acting.")).not.toBeVisible();
    // JSDOM does not implement native summary keyboard activation; Chromium QA covers Enter.
    await user.click(summaries[0]);
    expect(screen.getByText("Inspect source evidence before acting.")).toBeVisible();
    await user.keyboard("{Escape}");
    expect(summaries[0]).toHaveFocus();
    expect(summaries[0].parentElement).not.toHaveAttribute("open");
    expect((await axe(view.container)).violations).toEqual([]);

    // The choice is remembered for this operator on the next visit.
    view.unmount();
    resetSectionDescriptionChoices();
    const next = render(<Help />);
    expect(next.container.querySelectorAll("summary")).toHaveLength(2);
  });

  it("does not share the preference between operators", async () => {
    const view = render(<Help />);
    await userEvent.setup().click(screen.getByRole("button", { name: "Hide descriptions" }));
    expect(view.container.querySelector("summary")).toBeInTheDocument();
    view.rerender(<Help operator="Operator B" />);
    expect(view.container.querySelector("summary")).not.toBeInTheDocument();
    expect(screen.getByText("Inspect source evidence before acting.")).toBeVisible();
  });

  it("remains usable when local storage is unavailable", async () => {
    const get = vi.spyOn(Storage.prototype, "getItem").mockImplementation(() => { throw new Error("blocked"); });
    const set = vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => { throw new Error("blocked"); });
    try {
      const view = render(<Help />);
      expect(screen.getByText("Inspect source evidence before acting.")).toBeVisible();
      await userEvent.setup().click(screen.getByRole("button", { name: "Hide descriptions" }));
      // The choice holds for the visit even though it cannot be stored.
      expect(view.container.querySelectorAll("summary")).toHaveLength(2);
    } finally { get.mockRestore(); set.mockRestore(); }
  });
});
