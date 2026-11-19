// Settings > Preferences: which pop-up notifications the operator wants and when to hold them.
// The set follows the operator through the host preferences, so the WPF desktop applies the same
// choices to its toasts. In this browser the master switch and quiet hours hold price-alert system
// pop-ups; the notification center and alert history are unaffected.
import { useId, useState, useSyncExternalStore, type FormEvent } from "react";
import { BellRing } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { useDisplayTimeZone } from "@/components/ui/time-text";
import { useToast } from "@/components/ui/toast";
import {
  getNotificationPreferences,
  isQuietHoursActive,
  NOTIFICATION_TYPES,
  parseClockTime,
  setNotificationPreferences,
  subscribeNotificationPreferences,
  type NotificationPreferences
} from "@/lib/notification-preferences";
import { PreferenceSyncStatus } from "@/screens/settings-screen.preference-sync-status";

export function useNotificationPreferences(): NotificationPreferences {
  return useSyncExternalStore(subscribeNotificationPreferences, getNotificationPreferences, getNotificationPreferences);
}

/** The Notifications card: a master switch, five notification types, quiet hours and a test button. */
export function SettingsNotificationsSection({ now = () => new Date() }: { now?: () => Date }) {
  const preferences = useNotificationPreferences();
  const timeZone = useDisplayTimeZone();
  const toast = useToast();
  const [startDraft, setStartDraft] = useState<string | null>(null);
  const [endDraft, setEndDraft] = useState<string | null>(null);
  const [windowError, setWindowError] = useState<string | null>(null);
  const [testStatus, setTestStatus] = useState<string | null>(null);
  const startId = useId();
  const endId = useId();
  const zoneHelpId = useId();
  const windowErrorId = useId();

  const update = (change: Partial<NotificationPreferences>) => {
    setNotificationPreferences({ ...getNotificationPreferences(), ...change });
    setTestStatus(null);
  };

  const applyWindow = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const start = parseClockTime(startDraft ?? preferences.quietHoursStart);
    const end = parseClockTime(endDraft ?? preferences.quietHoursEnd);
    if (!start || !end) {
      setWindowError("Enter both times as 24-hour HH:mm, such as 22:00 and 07:00.");
      return;
    }
    update({ quietHoursStart: start, quietHoursEnd: end });
    setStartDraft(null);
    setEndDraft(null);
    setWindowError(start === end ? "Start and end are the same, so quiet hours never hold anything." : null);
  };

  const sendTest = () => {
    const current = getNotificationPreferences();
    if (!current.enabled) {
      setTestStatus("Pop-up notifications are off, so the test was not shown.");
      return;
    }
    if (isQuietHoursActive(current, now(), timeZone)) {
      setTestStatus(`Quiet hours are on until ${current.quietHoursEnd} (${timeZone}), so the test was held.`);
      return;
    }
    toast.info("Test notification", "Pop-up notifications are working.");
    setTestStatus("Test notification sent.");
  };

  const quietNow = isQuietHoursActive(preferences, now(), timeZone);

  return (
    <Card
      id="settings-notifications"
      role="region"
      aria-label="Notification preferences"
      className="panel-surface scroll-mt-6 border border-border/70"
    >
      <CardHeader>
        <div className="eyebrow-label">Operator preferences</div>
        <CardTitle className="mt-2 flex items-center gap-2 text-base">
          <BellRing className="h-4 w-4 text-primary" />
          Notifications
        </CardTitle>
        <CardDescription className="mt-2">
          Choose which pop-ups you see and when to hold them. These choices follow you to other browsers and the desktop app.
        </CardDescription>
        <PreferenceSyncStatus />
      </CardHeader>
      <CardContent className="grid gap-5 md:grid-cols-2">
        <div className="grid gap-3">
          <Checkbox
            checked={preferences.enabled}
            onCheckedChange={(checked) => update({ enabled: checked })}
            label="Show pop-up notifications"
            hint="The notification center and alert history keep every item either way."
          />
          <fieldset className="grid gap-2" disabled={!preferences.enabled}>
            <legend className="text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">Desktop app pop-ups</legend>
            {NOTIFICATION_TYPES.map((type) => (
              <Checkbox
                key={type.key}
                checked={preferences[type.key]}
                disabled={!preferences.enabled}
                onCheckedChange={(checked) => update({ [type.key]: checked })}
                label={type.label}
              />
            ))}
          </fieldset>
        </div>

        <div className="grid content-start gap-3">
          <Checkbox
            checked={preferences.quietHoursEnabled}
            onCheckedChange={(checked) => update({ quietHoursEnabled: checked })}
            label="Hold pop-ups during quiet hours"
            hint="Price-alert pop-ups here and desktop app pop-ups wait until quiet hours end."
          />
          <form className="flex flex-wrap items-end gap-2" onSubmit={applyWindow} aria-label="Quiet hours">
            <div className="grid gap-1">
              <label htmlFor={startId} className="text-xs text-muted-foreground">From</label>
              <Input
                id={startId}
                className="w-24"
                inputMode="numeric"
                placeholder="22:00"
                value={startDraft ?? preferences.quietHoursStart}
                error={windowError !== null && !parseClockTime(startDraft ?? preferences.quietHoursStart)}
                aria-describedby={windowError ? `${zoneHelpId} ${windowErrorId}` : zoneHelpId}
                onChange={(event) => {
                  setStartDraft(event.target.value);
                  setWindowError(null);
                }}
              />
            </div>
            <div className="grid gap-1">
              <label htmlFor={endId} className="text-xs text-muted-foreground">To</label>
              <Input
                id={endId}
                className="w-24"
                inputMode="numeric"
                placeholder="07:00"
                value={endDraft ?? preferences.quietHoursEnd}
                error={windowError !== null && !parseClockTime(endDraft ?? preferences.quietHoursEnd)}
                aria-describedby={windowError ? `${zoneHelpId} ${windowErrorId}` : zoneHelpId}
                onChange={(event) => {
                  setEndDraft(event.target.value);
                  setWindowError(null);
                }}
              />
            </div>
            <Button type="submit" variant="outline" size="sm" disabled={startDraft === null && endDraft === null}>
              Save hours
            </Button>
          </form>
          <p id={zoneHelpId} className="text-xs leading-5 text-muted-foreground">
            24-hour times in {timeZone}, your display time zone. A window that ends earlier than it starts runs past midnight.
            {preferences.quietHoursEnabled ? (quietNow ? " Quiet hours are on now." : " Quiet hours are not on now.") : null}
          </p>
          {windowError ? <p id={windowErrorId} role="alert" className="text-xs leading-5 text-danger">{windowError}</p> : null}
          <div className="flex flex-wrap items-center gap-3">
            <Button type="button" variant="outline" size="sm" onClick={sendTest}>Send test notification</Button>
            {testStatus ? <p role="status" aria-label="Test notification result" className="text-xs leading-5 text-muted-foreground">{testStatus}</p> : null}
          </div>
        </div>
      </CardContent>
    </Card>
  );
}
