import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import type { CorporateAction } from "@/types";
import { AssetDetailEvents, corporateActionDateGroup } from "./asset-detail-events";

const action: CorporateAction = {
  corpActId: "event-1",
  securityId: "sec-a",
  eventType: "Dividend",
  exDate: "2020-01-01",
  payDate: "2099-01-01",
  recordDate: "2020-01-02",
  dividendPerShare: 0.25,
  currency: "USD",
  splitRatio: null,
  newSecurityId: null,
  distributionRatio: null,
  acquirerSecurityId: null,
  exchangeRatio: null,
  subscriptionPricePerShare: null,
  rightsPerShare: null,
  lifecycleState: "Announced",
  supersedesCorpActId: null,
  redemptionPricePercentOfPar: null,
  payload: null,
  payloadSchemaVersion: 1
};

it("groups remaining payment dates without inferring lifecycle or payment completion", () => {
  expect(corporateActionDateGroup(action, "2026-11-21")).toBe("Upcoming");
  expect(corporateActionDateGroup({ ...action, payDate: "2020-01-15" }, "2026-11-21")).toBe("Past dates");
  expect(corporateActionDateGroup({ ...action, lifecycleState: "Cancelled" }, "2026-11-21")).toBe("Cancelled");
  expect(
    corporateActionDateGroup({ ...action, exDate: "unknown", payDate: null, recordDate: null }, "2026-11-21")
  ).toBe("Dates unavailable");
});

it("lets users filter and inspect retained event evidence", async () => {
  const { container } = render(
    <AssetDetailEvents
      resource={{
        status: "ready",
        data: [
          action,
          {
            ...action,
            corpActId: "event-2",
            eventType: "Split",
            payDate: "2020-02-01",
            lifecycleState: "Confirmed"
          }
        ],
        error: null
      }}
      retry={vi.fn()}
    />
  );
  expect(screen.getByText(/past payment date does not establish payment/)).toBeInTheDocument();
  await userEvent.selectOptions(screen.getByRole("combobox", { name: "Event dates" }), "Past dates");
  expect(screen.queryByRole("heading", { name: "Dividend" })).not.toBeInTheDocument();
  expect(screen.getByRole("heading", { name: "Split" })).toBeInTheDocument();
  await userEvent.click(screen.getByText("Event evidence"));
  expect(screen.getByText("Confirmed")).toBeVisible();
  expect(screen.getByText("event-2")).toBeVisible();
  expect((await axe(container)).violations).toHaveLength(0);
});

it("distinguishes unavailable events from an empty successful query", async () => {
  const retry = vi.fn();
  const result = render(
    <AssetDetailEvents
      resource={{
        status: "error",
        data: null,
        error: new Error("offline")
      }}
      retry={retry}
    />
  );
  expect(screen.queryByText("No corporate actions recorded for this security.")).not.toBeInTheDocument();
  await userEvent.click(screen.getByRole("button", { name: "Retry corporate actions" }));
  expect(retry).toHaveBeenCalledOnce();
  result.rerender(<AssetDetailEvents resource={{ status: "ready", data: [], error: null }} retry={retry} />);
  expect(screen.getByText("No corporate actions recorded for this security.")).toBeInTheDocument();
});
