// Settings > Preferences: the operator's display time zone for workstation timestamps.
import { useId, useState, type FormEvent } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import { useDisplayTimeZone } from "@/components/ui/time-text";
import { COMMON_DISPLAY_TIME_ZONES, formatTimestamp, setDisplayTimeZone } from "@/lib/format";

const SAMPLE_INSTANT = "2026-06-30T18:05:00Z";

/** UTC by default; a short list of desk zones, plus any IANA zone typed in and validated. */
export function SettingsTimeZonePreference() {
  const timeZone = useDisplayTimeZone();
  const [customZone, setCustomZone] = useState("");
  const [customError, setCustomError] = useState<string | null>(null);
  const selectId = useId();
  const customId = useId();
  const helpId = useId();
  const errorId = useId();
  const zoneOptions = COMMON_DISPLAY_TIME_ZONES.includes(timeZone) ? COMMON_DISPLAY_TIME_ZONES : [timeZone, ...COMMON_DISPLAY_TIME_ZONES];

  const applyCustomZone = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (setDisplayTimeZone(customZone)) {
      setCustomZone("");
      setCustomError(null);
    } else {
      setCustomError(`"${customZone.trim() || "(blank)"}" is not an IANA time zone, e.g. Europe/Madrid.`);
    }
  };

  return (
    <div className="grid gap-2 md:col-span-2">
      <label htmlFor={selectId} className="text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">Time zone</label>
      <div className="grid gap-3 md:grid-cols-2">
        <Select id={selectId} value={timeZone} aria-describedby={helpId} onChange={(event) => setDisplayTimeZone(event.target.value)}>
          {zoneOptions.map((zone) => <option key={zone} value={zone}>{zone === "UTC" ? "UTC (default)" : zone}</option>)}
        </Select>
        <form className="flex items-start gap-2" onSubmit={applyCustomZone} aria-label="Other time zone">
          <div className="min-w-0 flex-1">
            <label htmlFor={customId} className="sr-only">Other IANA time zone</label>
            <Input
              id={customId}
              value={customZone}
              placeholder="Other IANA zone, e.g. Europe/Madrid"
              error={customError !== null}
              aria-describedby={customError ? errorId : undefined}
              onChange={(event) => {
                setCustomZone(event.target.value);
                setCustomError(null);
              }}
            />
          </div>
          <Button type="submit" variant="outline" size="sm">Use zone</Button>
        </form>
      </div>
      <p id={helpId} className="text-xs leading-5 text-muted-foreground">
        Timestamps use a 24-hour clock in this zone, e.g. {formatTimestamp(SAMPLE_INSTANT, { timeZone })}. Labels defined on the UTC clock stay in UTC.
      </p>
      {customError ? <p id={errorId} role="alert" className="text-xs leading-5 text-danger">{customError}</p> : null}
    </div>
  );
}
