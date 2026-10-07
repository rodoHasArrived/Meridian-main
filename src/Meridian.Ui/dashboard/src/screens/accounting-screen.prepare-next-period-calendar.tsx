import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { FormGrid, FormRow } from "@/components/ui/form";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import type { ClosePeriodPlan } from "@/types";
import type { CaptureClosePlanTemplateRequest, CloseDeadlineRule, ClosePlanTemplate } from "@/types/close-preparation";

const weekdays = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];

export function ClosePreparationCalendarForm({ plan, previous, busy, onCapture }: {
  plan: ClosePeriodPlan;
  previous: ClosePlanTemplate | null;
  busy: boolean;
  onCapture: (request: CaptureClosePlanTemplateRequest) => void;
}) {
  const [name, setName] = useState(previous?.name ?? "");
  const [calendarId, setCalendarId] = useState(previous?.calendar.calendarId ?? "");
  const [version, setVersion] = useState(previous?.calendar.version ?? "");
  const [weekendDays, setWeekendDays] = useState(previous?.calendar.weekendDays ?? [0, 6]);
  const [holidays, setHolidays] = useState(previous?.calendar.holidays.join(", ") ?? "");
  const [rules, setRules] = useState(() => plan.tasks.map(task => {
    const retained = previous?.tasks.find(item => item.configuration.taskId === task.taskId)?.deadlineRule;
    return { taskId: task.taskId, anchor: retained?.anchor ?? "PeriodEnd", offsetDays: retained ? String(retained.offsetDays) : "",
      dayCount: retained?.dayCount ?? "BusinessDays", adjustment: retained?.adjustment ?? "FollowingBusinessDay" };
  }));
  const holidayDates = holidays.split(/[\s,]+/).filter(Boolean);
  const valid = name.trim() && calendarId.trim() && version.trim()
    && rules.every(rule => /^-?\d+$/.test(rule.offsetDays) && Number.isSafeInteger(Number(rule.offsetDays)))
    && holidayDates.every(date => /^\d{4}-\d{2}-\d{2}$/.test(date)) && weekendDays.length < 7;
  const update = (taskId: string, changes: Partial<(typeof rules)[number]>) => setRules(current => current.map(rule => rule.taskId === taskId ? { ...rule, ...changes } : rule));

  return <form className="space-y-4" onSubmit={event => {
    event.preventDefault();
    if (!valid || busy || !plan.workflowId) return;
    onCapture({ sourceWorkflowId: plan.workflowId, name: name.trim(), templateId: previous?.templateId,
      calendar: { calendarId: calendarId.trim(), version: version.trim(), weekendDays, holidays: holidayDates },
      deadlineRules: rules.map(rule => ({ ...rule, offsetDays: Number(rule.offsetDays) })) });
  }}>
    <fieldset disabled={busy} className="space-y-4">
      <legend className="mb-3 font-semibold">1. Capture reusable configuration</legend>
      <FormGrid columns={3}>
        <FormRow label="Template name" labelFor="close-template-name"><Input id="close-template-name" required value={name} onChange={event => setName(event.target.value)} /></FormRow>
        <FormRow label="Calendar name" labelFor="close-calendar-id"><Input id="close-calendar-id" required placeholder="e.g. US finance calendar" value={calendarId} onChange={event => setCalendarId(event.target.value)} /></FormRow>
        <FormRow label="Calendar version" labelFor="close-calendar-version"><Input id="close-calendar-version" required placeholder="e.g. 2026.1" value={version} onChange={event => setVersion(event.target.value)} /></FormRow>
      </FormGrid>
      <fieldset className="space-y-2">
        <legend className="text-sm font-medium">Non-working weekdays</legend>
        <div className="flex flex-wrap gap-3">{weekdays.map((day, index) => <Checkbox key={day} label={day} checked={weekendDays.includes(index)} onCheckedChange={checked => setWeekendDays(current => checked ? [...current, index].sort() : current.filter(value => value !== index))} />)}</div>
      </fieldset>
      <FormRow label="Holiday dates" labelFor="close-calendar-holidays" hint="Enter YYYY-MM-DD dates separated by commas. Include dates in the target period and deadline window; leave empty only for a calendar without holidays.">
        <Input id="close-calendar-holidays" value={holidays} onChange={event => setHolidays(event.target.value)} placeholder="2026-11-11, 2026-11-26" />
      </FormRow>
      <p className="text-sm text-muted-foreground">Define each deadline explicitly. Day offsets exclude the anchor date; the selected adjustment applies after the offset. The service calculates all target dates.</p>
      <div className="overflow-x-auto">
        <table className="w-full text-left text-sm" aria-label="Reusable deadline rules">
          <thead><tr className="border-b border-border"><th className="p-2">Task / source deadline</th><th className="p-2">Anchor</th><th className="p-2">Offset</th><th className="p-2">Day count</th><th className="p-2">Adjustment</th></tr></thead>
          <tbody>{plan.tasks.map((task, index) => {
            const rule = rules[index];
            return <tr key={task.taskId} className="border-b border-border">
              <th scope="row" className="p-2 font-medium">{task.displayName}<div className="font-mono text-xs text-muted-foreground">{task.dueDate}</div></th>
              <td className="min-w-36 p-2"><Select aria-label={`${task.displayName} deadline anchor`} value={rule.anchor} onChange={event => update(task.taskId, { anchor: event.target.value as CloseDeadlineRule["anchor"] })}><option value="PeriodEnd">Period end</option><option value="PeriodStart">Period start</option></Select></td>
              <td className="min-w-24 p-2"><Input required type="number" step="1" aria-label={`${task.displayName} day offset`} value={rule.offsetDays} onChange={event => update(task.taskId, { offsetDays: event.target.value })} /></td>
              <td className="min-w-40 p-2"><Select aria-label={`${task.displayName} day count`} value={rule.dayCount} onChange={event => update(task.taskId, { dayCount: event.target.value as CloseDeadlineRule["dayCount"] })}><option value="BusinessDays">Business days</option><option value="CalendarDays">Calendar days</option></Select></td>
              <td className="min-w-52 p-2"><Select aria-label={`${task.displayName} deadline adjustment`} value={rule.adjustment} onChange={event => update(task.taskId, { adjustment: event.target.value as CloseDeadlineRule["adjustment"] })}><option value="FollowingBusinessDay">Following business day</option><option value="PrecedingBusinessDay">Preceding business day</option><option value="None">No adjustment</option></Select></td>
            </tr>;
          })}</tbody>
        </table>
      </div>
      <Button type="submit" disabled={!valid || busy}>{busy ? "Capturing configuration…" : previous ? "Capture new template version" : "Capture template"}</Button>
    </fieldset>
  </form>;
}
