import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { FixedIncomePaymentPreview } from "./fixed-income-payment-preview";

describe("FixedIncomePaymentPreview", () => {
  it("shows the retained exact amount beside conventions and explicitly labels sample assumptions", () => {
    render(<FixedIncomePaymentPreview mappedRecords={[{ fixedIncomePaymentPreview: {
      sampleOriginalFace: "100000", currency: "USD", coupon: "5", couponRateUnit: "Percent", couponCashBasis: "DayCount",
      paymentFrequency: "SemiAnnual", dayCount: "Act/365", accrualStart: "2026-07-15", accrualEnd: "2027-01-15",
      paymentDate: "2027-01-15", sampleCouponPayment: "2520.5479452054794520547945205", yearFraction: "0.5041095890410958904109589041",
      paymentCalendars: ["WEEKENDS"], businessDayConvention: "Following", settlementDays: "1",
      settlementConvention: "Following", settlementCalendars: ["WEEKENDS"], callScheduleCount: 0, factorHistoryCount: 2,
      normalizedTermsJson: "{\"couponRate\":4.123456789012345678}"
    } }]} />);
    expect(screen.getByText("USD 2,520.5479452054794520547945205")).toBeInTheDocument();
    expect(screen.getByText(/Illustrative cash on 100,000 original face/)).toBeInTheDocument();
    expect(screen.getByText(/0 call windows · 2 dated factors/)).toBeInTheDocument();
    expect(screen.getByText(/USD · Reviewed explicit choice/)).toBeInTheDocument();
    expect(screen.getByText('{"couponRate":4.123456789012345678}')).toBeInTheDocument();
  });

  it("ignores malformed evidence instead of rendering a provider assertion or crashing on calendar values", () => {
    const { container } = render(<FixedIncomePaymentPreview mappedRecords={[
      { fixedIncomePaymentPreview: { sampleCouponPayment: "999999", paymentCalendars: "WEEKENDS", settlementCalendars: {} } },
      { fixedIncomePaymentPreview: { sampleCouponPayment: "999999", paymentCalendars: [42] } }
    ]} />);
    expect(container).toBeEmptyDOMElement();
  });

  it("does not invent sample cash for records without a server preview", () => {
    const { container } = render(<FixedIncomePaymentPreview mappedRecords={[{}, { fixedIncomePaymentPreview: null }]} />);
    expect(container).toBeEmptyDOMElement();
  });
});
