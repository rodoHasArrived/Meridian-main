// Meridian DatePicker — date-only values with an accessible calendar popover.
import React from "react";
import { calendarField as CalendarField } from "./date-field";

export function DatePicker(props) {
  return <CalendarField {...props} />;
}
