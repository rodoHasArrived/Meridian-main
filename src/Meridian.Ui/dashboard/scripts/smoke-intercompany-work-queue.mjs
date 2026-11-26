import assert from "node:assert/strict";
import path from "node:path";

/** Exercise the mounted workstation with synthetic HTTP receipts; never a persistence proof. */
export async function runIntercompanyWorkQueueSmoke({ page, baseUrl, apiResolver, screenshotDir, errors = [] }) {
  const book = apiResolver.resolve(new URL("/api/ledger/books", baseUrl).href).body[0];
  assert.ok(book?.ledgerBookId && book.fundProfileId, "Queue smoke requires a scoped ledger book fixture.");
  const checks = [], writes = [], screenshots = [];
  const check = (name, condition) => { assert.ok(condition, name); checks.push(name); };
  const stamp = "2026-11-23T15:00:00Z", fingerprint = "b".repeat(64);
  const agreementId = "6e6392c7-d845-45c9-87cb-341084c72910";
  let asOf = "2026-11-23", observed = true, status = "Current";
  let item = {
    caseId: `ic-work:${"a".repeat(64)}`, ledgerBookId: book.ledgerBookId, agreementId, agreementVersion: 3,
    findingCode: "intercompany.agreement.expiring", severity: "Warning",
    message: "Synthetic bridge loan legal coverage ends on 2026-12-01.",
    suggestedAction: "Review the approved renewal evidence with the assigned operator.",
    dueDate: "2026-12-01", observedAsOf: asOf, isCurrentlyObserved: true, isTracked: false,
    state: "Open", owner: null, firstSeenAtUtc: null, lastObservedAtUtc: null, ageDays: null,
    daysOverdue: 0, lastObservedSourceFingerprint: "", version: 0, recordHash: null, history: []
  };
  const guarantee = {
    agreementId: "6a333333-3333-4444-8888-333333333333", code: "SYN-GUAR-004", name: "Synthetic operating guarantee",
    agreementVersion: 2, payloadHash: "d".repeat(64),
    partyOneId: "4f7c8a52-1a44-4f4e-9a2b-6f0d5a7c9e22", partyOneRole: "Guarantor",
    partyTwoId: "4f7c8a52-1a44-4f4e-9a2b-6f0d5a7c9e33", partyTwoRole: "Obligor",
    currency: "USD", beneficiaryReference: "Synthetic lender", limitAmount: "2500000.00",
    effectiveFrom: "2026-01-01", effectiveTo: "2027-01-01", terminationEffectiveDate: null,
    effectiveStatus: "Active", currentExposure: null, exposureStatus: "Unavailable",
    exposureUnavailableReason: "No reviewed guaranteed-obligation linkage or measured exposure source is retained."
  };
  page.on("pageerror", error => errors.push(error.message));
  page.on("console", message => { if (message.type() === "error") errors.push(message.text()); });
  page.on("requestfailed", request => {
    if (!request.failure()?.errorText?.includes("ERR_ABORTED")) errors.push(`${request.url()}: ${request.failure()?.errorText}`);
  });
  await page.route("**/api/ledger/intercompany/capabilities", route => route.fulfill({ json: {
    canManageAgreements: false, canManageLifecycle: true, canManageLedger: true, canViewLedger: true
  } }));
  await page.route("**/api/fund-structure/graph?**", route => route.fulfill({ json: {
    organizations: [], ownershipLinks: [], entities: [
      { entityId: guarantee.partyOneId, name: "Synthetic HoldCo", baseCurrency: "USD", lifecycleStatus: "Active" },
      { entityId: guarantee.partyTwoId, name: "Synthetic Operating Co", baseCurrency: "USD", lifecycleStatus: "Active" }
    ]
  } }));
  // Ledger-management permission also mounts this adjacent, read-only list.
  await page.route("**/api/ledger/intercompany/legacy-attributions**", route => route.fulfill({ json: [] }));
  await page.route("**/api/ledger/intercompany/work-queue**", async route => {
    try {
      const request = route.request(), url = new URL(request.url());
      assert.equal(url.searchParams.get("ledgerBookId"), book.ledgerBookId);
      assert.equal(url.searchParams.get("fundProfileId"), book.fundProfileId);
      assert.equal(url.searchParams.get("asOf"), asOf);
      assert.equal(url.searchParams.get("expiryHorizonDays"), "30");
      if (request.method() === "GET") {
        await route.fulfill({ json: {
          asOf, expiryHorizonDays: 30, evaluatedAtUtc: stamp, status, isComplete: status === "Current",
          sourceFingerprint: status === "Current" ? fingerprint : "",
          cases: status === "Current" ? [{ ...item, isCurrentlyObserved: observed }] : [],
          guarantees: status === "Current" ? [guarantee] : [],
          diagnostics: status === "Current" ? [] : ["Synthetic source unavailable; retained case states are unchanged."],
          ledgerBookId: book.ledgerBookId, fundProfileId: book.fundProfileId, canManage: true
        } });
        return;
      }
      assert.equal(request.method(), "POST");
      assert.equal(status, "Current");
      const command = request.postDataJSON();
      const action = url.pathname.endsWith("/observe") ? "Observe" : command.action;
      assert.equal(url.pathname, `/api/ledger/intercompany/work-queue/${action === "Observe" ? "observe" : "update"}`);
      assert.equal(action, ["Observe", "AssignOwner", "Resolve", "Reopen"][writes.length]);
      assert.equal(command.caseId, item.caseId);
      assert.match(command.commandId, /^[0-9a-f-]{36}$/i);
      assert.ok(command.reason?.trim());
      assert.equal(command.expectedVersion, item.version);
      assert.equal(command.expectedRecordHash, item.recordHash);
      assert.equal(command.expectedSourceFingerprint, fingerprint);
      if (action === "Resolve") assert.deepEqual(command.evidenceReferences, ["synthetic:approved-amendment-4"]);
      writes.push(command);
      const state = ({ Resolve: "Resolved", Reopen: "Reopened" })[action] ?? item.state;
      const owner = action === "AssignOwner" ? command.owner : item.owner;
      const version = item.version + 1, recordHash = String(version).repeat(64);
      item = { ...item, isTracked: true, state, owner, version, recordHash,
        firstSeenAtUtc: item.firstSeenAtUtc ?? stamp, lastObservedAtUtc: stamp, ageDays: 0,
        lastObservedSourceFingerprint: fingerprint, history: [...item.history, {
          commandId: command.commandId, version, recordHash, action, state, actor: "synthetic-operator",
          recordedAtUtc: stamp, reason: command.reason, owner, note: command.note ?? null,
          evidenceReferences: command.evidenceReferences ?? [], observedAsOf: asOf, sourceFingerprint: fingerprint,
          sourceVersions: [{ sourceKind: "approved-legal-revision", sourceId: `${agreementId}:3`, version: "3", contentHash: "e".repeat(64) }]
        }]
      };
      await route.fulfill({ json: item });
    } catch (error) {
      errors.push(`Synthetic queue request failed: ${error.message}`);
      await route.fulfill({ status: 500, json: { error: "Synthetic queue request validation failed." } });
    }
  });

  const routeUrl = `${baseUrl.replace(/\/$/, "")}/accounting/consolidation?entityTab=obligations&obligationAsOf=${asOf}`;
  await page.goto(routeUrl, { waitUntil: "networkidle" });
  const panel = page.getByRole("region", { name: "Intercompany lifecycle work queue", exact: true });
  const load = panel.getByRole("button", { name: "Load lifecycle queue", exact: true });
  const finding = panel.getByRole("button", { name: item.message, exact: true });
  const retain = panel.getByRole("button", { name: "Retain case action", exact: true });
  const receipt = async version => {
    await panel.getByText(`Case action retained at version ${version}.`, { exact: true }).waitFor();
    await panel.getByText(new RegExp(`^First seen .* · version ${version}$`)).waitFor();
  };
  await load.click();
  await finding.click();
  await panel.getByLabel("Lifecycle action reason").fill("Track the synthetic loan expiry.");
  await panel.getByRole("button", { name: "Track this finding", exact: true }).click();
  await receipt(1);
  check("Track with source and case authority pins", item.isTracked && writes.length === 1);
  await panel.getByLabel("Lifecycle owner", { exact: true }).fill("synthetic-finance-operator");
  await panel.getByLabel("Lifecycle action reason").fill("Assign the synthetic renewal review.");
  await retain.click();
  await receipt(2);
  check("Assign owner and preserve first observation", item.owner === "synthetic-finance-operator" && item.firstSeenAtUtc === stamp);
  await panel.getByLabel("Lifecycle case action").selectOption("Resolve");
  await panel.getByLabel("Lifecycle action reason").fill("Reviewed the synthetic amendment evidence.");
  check("Resolution requires evidence before submission", await retain.isDisabled() && writes.length === 2);
  await panel.getByLabel("Lifecycle evidence references (one per line)").fill("synthetic:approved-amendment-4");
  await retain.click();
  await receipt(3);
  check("Resolution receipt renders with retained evidence", item.state === "Resolved"
    && (await panel.getByRole("table", { name: "Agreement lifecycle cases" }).innerText()).includes("Resolved"));
  await panel.getByLabel("Lifecycle case action").selectOption("Reopen");
  await panel.getByLabel("Lifecycle action reason").fill("Reopen the synthetic renewal follow-up.");
  await retain.click();
  await receipt(4);
  observed = false;
  await load.click();
  await panel.getByRole("table", { name: "Agreement lifecycle cases" })
    .getByRole("cell", { name: /Reopened.*Not observed at this cutoff/ }).waitFor();
  check("Absent finding retains reopened state and history", item.state === "Reopened" && item.history.length === 4
    && (await panel.getByRole("table", { name: "Agreement lifecycle cases" }).innerText()).includes("Reopened"));
  await finding.click();
  await panel.getByText("Lifecycle case history and source evidence", { exact: true }).click();
  await panel.getByText("synthetic:approved-amendment-4", { exact: true }).waitFor();
  const guarantees = panel.getByRole("table", { name: "Approved guarantee disclosures" });
  check("Guarantee cap remains distinct from unavailable exposure",
    await guarantees.getByRole("cell", { name: "2500000.00 USD", exact: true }).isVisible()
    && await guarantees.getByRole("cell", { name: /^Not available/ }).isVisible());
  for (const viewport of [{ width: 1440, height: 900 }, { width: 1366, height: 768 }]) {
    await page.setViewportSize(viewport);
    await panel.scrollIntoViewIfNeeded();
    check(`No page overflow at ${viewport.width}px`, await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1));
    if (screenshotDir) {
      const file = path.join(screenshotDir, `intercompany-work-queue-${viewport.width}.png`);
      await panel.screenshot({ path: file });
      screenshots.push(file);
    }
  }
  asOf = "2026-11-24";
  await page.getByLabel("Obligation monitoring as of").fill(asOf);
  await finding.waitFor({ state: "hidden" });
  check("Date scope change clears previous case data", await retain.count() === 0);
  status = "Unavailable";
  await load.click();
  await panel.getByText("Unavailable lifecycle observation", { exact: true }).waitFor();
  check("Unavailable evidence exposes no actionable prior cases", await finding.count() === 0 && await retain.count() === 0);
  apiResolver.assertNoUnexpectedRequests();
  assert.deepEqual(errors, [], "Queue smoke must not hide runtime or API failures.");
  check("Four distinct retained command receipts", writes.length === 4 && new Set(writes.map(command => command.commandId)).size === 4);
  return { status: "passed", evidence: "Rendered workstation with synthetic HTTP responses; no backend persistence proof.",
    route: routeUrl, checks, writes: writes.length, screenshots };
}
