// Meridian DateRangePicker — controlled start/end dates and a two-month calendar.
import React from "react";
import { calendarField as CalendarField } from "./date-field";

export function DateRangePicker(props) {
  return <CalendarField {...props} range />;
}
