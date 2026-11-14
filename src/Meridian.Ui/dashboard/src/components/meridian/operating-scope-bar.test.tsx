import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { buildOperatingScopeFromSearch } from "@/app-shell.operating-scope";
import { OperatingScopeBar } from "@/components/meridian/operating-scope-bar";

describe("OperatingScopeBar", () => {
  it("labels every dimension and reads All authorized or All periods, never None, when nothing is set", () => {
    render(<OperatingScopeBar operatingScope={buildOperatingScopeFromSearch("")} onEditOperatingScope={vi.fn()} />);

    const bar = screen.getByRole("button", {
      name: "Operating scope: Entity All authorized, Fund All authorized, Account All authorized, Period All periods"
    });
    expect(bar).toHaveTextContent("Scope");
    expect(bar).toHaveTextContent("EntityAll authorized›FundAll authorized›AccountAll authorized›PeriodAll periods");
    expect(within(bar).getByText("All periods").closest("[title]")).toHaveAttribute(
      "title",
      "Period: not narrowed; each screen shows its own default range"
    );
    expect(bar).toHaveAttribute("aria-haspopup", "dialog");
    expect(bar).not.toHaveTextContent(/none|no scope selected/i);
  });

  it("prints set dimensions in order and opens the scope picker", async () => {
    const user = userEvent.setup();
    const onEdit = vi.fn();
    render(
      <OperatingScopeBar
        operatingScope={buildOperatingScopeFromSearch("?fundAccountId=fund-1&symbol=MSFT&provider=Alpaca&from=2026-05-01&to=2026-05-15")}
        onEditOperatingScope={onEdit}
      />
    );

    const bar = screen.getByRole("button", {
      name: "Operating scope: Entity All authorized, Fund All authorized, Account fund-1, Subject MSFT, Provider Alpaca, Period 2026-05-01 to 2026-05-15"
    });
    expect(bar).toHaveTextContent("Accountfund-1›SubjectMSFT›ProviderAlpaca›Period2026-05-01 to 2026-05-15");

    await user.click(bar);
    expect(onEdit).toHaveBeenCalledTimes(1);
  });

  it("mutes a carried dimension the screen does not filter by and says so", () => {
    render(
      <OperatingScopeBar
        operatingScope={buildOperatingScopeFromSearch("?fundAccountId=fund-1&symbol=MSFT")}
        onEditOperatingScope={vi.fn()}
        scopeDimensionsInEffect={["symbol", "provider", "window"]}
      />
    );

    const bar = screen.getByRole("button", { name: /^Operating scope: Entity All authorized, Fund All authorized, Account fund-1/ });
    expect(bar).toHaveAccessibleDescription("Applied on this screen: Subject MSFT. Retained for other screens: Account fund-1.");
    expect(within(bar).getByText("fund-1").closest("[title]")).toHaveAttribute("title", "Account: carried, but not applied on this screen");
    expect(within(bar).getByText("MSFT").closest("[title]")).toHaveAttribute("title", "Subject: applied here");
  });

  it("renders a static, still labelled bar without an opener", () => {
    render(<OperatingScopeBar operatingScope={buildOperatingScopeFromSearch("")} />);

    expect(screen.queryByRole("button")).toBeNull();
    expect(screen.getByRole("group", {
      name: "Operating scope: Entity All authorized, Fund All authorized, Account All authorized, Period All periods"
    })).toBeInTheDocument();
  });

  it("prints entity and fund names when known and keeps a retained fund visible but muted", () => {
    render(
      <OperatingScopeBar
        operatingScope={buildOperatingScopeFromSearch("?entityId=entity-1&fundId=fund-9&from=2026-11-01&to=2026-11-14")}
        onEditOperatingScope={vi.fn()}
        scopeDimensionsInEffect={["entityId", "fundAccountId", "window"]}
        valueLabels={{ entityId: "Meridian Holdings LP", fundId: "Growth Fund I" }}
      />
    );

    const bar = screen.getByRole("button", {
      name: "Operating scope: Entity Meridian Holdings LP, Fund Growth Fund I, Account All authorized, Period 2026-11-01 to 2026-11-14"
    });
    expect(within(bar).getByText("Growth Fund I").closest("[title]")).toHaveAttribute("title", "Fund: carried, but not applied on this screen");
    expect(bar).toHaveAccessibleDescription(
      "Applied on this screen: Entity Meridian Holdings LP, Period 2026-11-01 to 2026-11-14. Retained for other screens: Fund Growth Fund I."
    );
  });
});
