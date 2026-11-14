import { Link } from "react-router-dom";
import { Select } from "@/components/ui/select";

type Mode = "Legacy" | "RawPricesWithCanonicalEvents";
const directCommand = /^[\t ]*Backtest\.WithCorporateActionMode\("(Legacy|RawPricesWithCanonicalEvents)"\);[\t ]*$/gm;

export function corporateActionSourceMode(source: string): { mode: Mode; manual: boolean } {
  const commands = [...source.matchAll(directCommand)];
  const calls = source.match(/\.WithCorporateActionMode\s*\(/g) ?? [];
  return { mode: commands.length === 1 ? commands[0][1] as Mode : "Legacy", manual: calls.length > 0 && (commands.length !== 1 || calls.length !== 1) };
}

/** The edited command is visible in the executed script; no request-time override is added. */
export function updateCorporateActionSource(source: string, mode: Mode): string {
  if (corporateActionSourceMode(source).manual) return source;
  const command = `Backtest.WithCorporateActionMode("${mode}");`;
  if ([...source.matchAll(directCommand)].length === 1) return source.replace(directCommand, command);
  const lines = source.split("\n");
  let insertion = 0;
  let inComment = false;
  while (insertion < lines.length) {
    const line = lines[insertion].trim();
    if (inComment || line.startsWith("/*")) {
      const end = line.indexOf("*/");
      if (end >= 0 && line.slice(end + 2).trim()) break;
      inComment = end < 0;
      insertion++;
    } else if (/^(?:$|\/\/|#r\b|#load\b|using\s+.+;\s*$)/.test(line)) insertion++;
    else break;
  }
  lines.splice(insertion, 0, command);
  return lines.join("\n");
}

export function QuantCorporateActionControl({ source, onChange, disabled }: {
  source: string; onChange: (source: string) => void; disabled: boolean;
}) {
  const current = corporateActionSourceMode(source);
  return <div className="space-y-2 rounded border border-border p-3">
    <div className="flex flex-wrap items-end justify-between gap-3">
      <label htmlFor="quant-corporate-action-mode" className="space-y-1 text-xs">Backtest corporate actions
        <Select id="quant-corporate-action-mode" aria-label="Backtest corporate actions" value={current.manual ? "SourceDefined" : current.mode} disabled={disabled || current.manual}
          aria-describedby="quant-corporate-action-help"
          onChange={event => onChange(updateCorporateActionSource(source, event.target.value as Mode))}>
          <option value="Legacy">Legacy script behavior</option>
          <option value="RawPricesWithCanonicalEvents">Raw prices + canonical events</option>
          {current.manual ? <option value="SourceDefined">Source-defined mode</option> : null}
        </Select>
      </label>
      <Link to="/strategy/run-ledger" className="text-xs text-primary underline">Open run ledger explorer</Link>
    </div>
    <p id="quant-corporate-action-help" className="text-xs text-muted-foreground">
      {current.manual ? "The script contains a custom or multiple corporate-action setting. Edit it directly in the source editor."
        : "Changing this selector edits the visible source command. Raw mode requires unadjusted bars, mastered USD equities and fully funded long trades; the server rejects unsupported inputs and conflicting event-population settings. Legacy preserves the current automatic stored-action behavior."}
    </p>
  </div>;
}
