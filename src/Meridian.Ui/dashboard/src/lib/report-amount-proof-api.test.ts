import { apiGetJson } from "@/lib/api";
import { getReportAmountBindings } from "@/lib/report-amount-proof-api";

vi.mock("@/lib/api", () => ({ apiGetJson: vi.fn() }));

it("requests only the exact retained run and prohibits development fallback", () => {
  const signal = new AbortController().signal;
  getReportAmountBindings("run / & two", { signal, allowDevelopmentFallback: true });
  expect(apiGetJson).toHaveBeenCalledWith(
    "/api/fund-structure/reporting/runs/run%20%2F%20%26%20two/amounts",
    { signal, allowDevelopmentFallback: false }
  );
});
