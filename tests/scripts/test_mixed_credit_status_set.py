from __future__ import annotations

import csv
import json
import unittest
from pathlib import Path


FIXTURE_PATH = (
    Path(__file__).resolve().parents[1]
    / "fixtures"
    / "portfolio"
    / "mixed-credit-status-set.json"
)


class MixedCreditStatusSetTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.fixture = json.loads(FIXTURE_PATH.read_text(encoding="utf-8"))
        cls.entries = cls.fixture["securityMasterEntries"]
        cls.operational = cls.fixture["operationalInstance"]

    def test_fixture_pins_requested_portfolio_composition(self) -> None:
        summary = self.fixture["portfolioSummary"]

        self.assertEqual(50, summary["personalLoanCount"])
        self.assertEqual(3, summary["mortgageSecurityPackCount"])
        self.assertEqual(2, summary["treasurySecurityCount"])
        self.assertEqual(2, summary["corporateBondCount"])
        self.assertEqual(1, summary["equityPositionCount"])
        self.assertEqual(1, summary["fundedStandaloneLoanCount"])
        self.assertEqual(1, summary["undrawnLineOfCreditCount"])
        self.assertEqual(60, summary["securityMasterEntryCount"])
        self.assertEqual(60, summary["positionCount"])
        self.assertEqual(52, summary["borrowerPartyCount"])
        self.assertEqual(4, summary["accountCount"])

        by_id = {entry["securityId"]: entry for entry in self.entries}
        self.assertEqual(500000, by_id["66666666-6666-4666-8666-000000000001"]["assetSpecificTerms"]["drawnPrincipal"])
        self.assertEqual("INDI", by_id["55555555-5555-4555-8555-000000000001"]["classification"]["primaryIdentifierValue"])
        self.assertEqual(0, by_id["77777777-7777-4777-8777-000000000001"]["assetSpecificTerms"]["drawnPrincipal"])
        self.assertEqual(
            by_id["77777777-7777-4777-8777-000000000001"]["assetSpecificTerms"]["commitmentAmount"],
            by_id["77777777-7777-4777-8777-000000000001"]["assetSpecificTerms"]["unfundedCommitment"],
        )

    def test_personal_loans_are_individual_security_master_entries_not_a_pool(self) -> None:
        personal_loans = [
            entry for entry in self.entries
            if entry["classification"]["assetClass"] == "DirectLoan"
            and entry["classification"]["subType"] == "PersonalLoan"
        ]

        self.assertEqual(50, len(personal_loans))
        self.assertNotIn("holdings", self.fixture)
        self.assertTrue(all("underlyingLoanCount" not in entry for entry in personal_loans))
        self.assertEqual(50, len({entry["securityId"] for entry in personal_loans}))
        self.assertEqual(50, len({entry["classification"]["primaryIdentifierValue"] for entry in personal_loans}))

        for index, entry in enumerate(personal_loans, start=1):
            with self.subTest(security_id=entry["securityId"]):
                self.assertEqual("Active", entry["status"])
                self.assertEqual("DirectLoan", entry["classification"]["assetClass"])
                self.assertEqual("PersonalLoan", entry["classification"]["subType"])
                self.assertEqual(f"MPL-{index:04d}", entry["classification"]["primaryIdentifierValue"])
                self.assertEqual("PrivateCredit", entry["economicDefinition"]["assetFamily"])
                self.assertEqual("ConsumerBorrower", entry["economicDefinition"]["issuerType"])
                self.assertEqual(f"fixture-borrower-{index:03d}", entry["assetSpecificTerms"]["borrowerReference"])
                self.assertGreater(entry["assetSpecificTerms"]["outstandingPrincipal"], 0)
                self.assertEqual("Monthly", entry["assetSpecificTerms"]["paymentFrequency"])
                self.assertEqual("Installment", entry["assetSpecificTerms"]["amortizationType"])
                self.assertEqual("aaaaaaaa-aaaa-4aaa-8aaa-000000000103", entry["assetSpecificTerms"]["servicingAccountId"])
                self.assertEqual(
                    entry["classification"]["primaryIdentifierValue"],
                    entry["identifiers"][0]["value"],
                )

    def test_fixture_is_stored_as_operational_instance(self) -> None:
        self.assertEqual("ops-fixture-mixed-credit-2026-06-04", self.operational["instanceId"])
        self.assertEqual("LocalFirstOperationalSeed", self.operational["storageModel"]["mode"])
        self.assertEqual(
            ["FundStructure", "SecurityMaster", "DirectLending", "Portfolio", "Ledger", "MarketData", "CashActivity", "Risk", "Compliance", "Settlement", "SourcePayloads", "AssetLifecycle", "Evidence", "AccessControl", "Reporting", "Audit"],
            self.operational["storageModel"]["stateStores"],
        )

        fund_structure = self.operational["fundStructure"]
        self.assertEqual(1, len(fund_structure["organizations"]))
        self.assertEqual(1, len(fund_structure["businesses"]))
        self.assertEqual(1, len(fund_structure["funds"]))
        self.assertEqual(2, len(fund_structure["sleeves"]))
        self.assertEqual(1, len(fund_structure["vehicles"]))
        self.assertEqual(1, len(fund_structure["investmentPortfolios"]))
        self.assertEqual(4, len(fund_structure["accounts"]))
        self.assertEqual(4, len(fund_structure["assignments"]))

        context = self.operational["operatingContext"]
        portfolio = self.operational["portfolio"]
        ledger = self.operational["ledger"]
        self.assertEqual(context["investmentPortfolioId"], portfolio["portfolioId"])
        self.assertEqual(context["ledgerBookId"], ledger["ledgerBook"]["ledgerBookId"])
        self.assertEqual(context["fundId"], ledger["ledgerBook"]["fundId"])

    def test_operational_entities_reference_security_master_and_accounts(self) -> None:
        security_ids = {entry["securityId"] for entry in self.entries}
        accounts = {
            account["accountId"]
            for account in self.operational["fundStructure"]["accounts"]
        }
        borrower_parties = [
            party for party in self.operational["parties"]
            if party["partyType"] == "Borrower"
        ]
        loan_contracts = self.operational["directLending"]["loanContracts"]
        positions = self.operational["portfolio"]["positions"]

        self.assertEqual(52, len(borrower_parties))
        self.assertEqual(52, len(loan_contracts))
        self.assertEqual(60, len(positions))

        self.assertTrue({position["securityId"] for position in positions}.issubset(security_ids))
        self.assertTrue({position["accountId"] for position in positions}.issubset(accounts))

        for contract in loan_contracts:
            with self.subTest(loan_id=contract["loanId"]):
                reference = contract["currentTerms"]["securityMasterReference"]
                self.assertIn(reference["securityId"], security_ids)
                self.assertEqual("Active", reference["status"])
                self.assertTrue(reference["ledgerMappingReference"].startswith("ledger-map://"))
                self.assertEqual(
                    contract["servicing"]["availableToDraw"],
                    contract["servicing"]["currentCommitment"] - contract["servicing"]["totalDrawn"],
                )

        loc = next(
            contract for contract in loan_contracts
            if contract["currentTerms"]["securityMasterReference"]["symbol"] == "LOC-UNDRAWN-001"
        )
        self.assertEqual(0, loc["servicing"]["totalDrawn"])
        self.assertEqual(250000, loc["servicing"]["availableToDraw"])
        self.assertEqual([], loc["servicing"]["drawdownLots"])

    def test_ledger_workflows_and_evidence_cover_operational_records(self) -> None:
        security_ids = {entry["securityId"] for entry in self.entries}
        ledger = self.operational["ledger"]
        workflows = self.operational["workflows"]
        evidence = self.operational["evidenceRecords"]

        self.assertEqual(len(security_ids), len(ledger["mappingEvidence"]))
        self.assertTrue({item["securityId"] for item in ledger["mappingEvidence"]}.issubset(security_ids))
        self.assertGreaterEqual(len(ledger["accounts"]), 8)
        self.assertEqual(
            {"Data", "Accounting", "Portfolio", "Reporting"},
            {workflow["workspace"] for workflow in workflows},
        )
        self.assertEqual(
            {
                "SecurityMasterImport",
                "DirectLendingContracts",
                "PortfolioPositions",
                "LedgerMapping",
                "MarketDataSnapshot",
                "CashAndActivity",
                "ReportPack",
                "RiskComplianceControls",
                "ExceptionQueue",
                "AuditTrail",
                "ProviderSourcePayloads",
                "AssetLifecycleEvents",
            },
            {record["kind"] for record in evidence},
        )

    def test_realistic_environment_layers_are_present_and_referenced(self) -> None:
        security_ids = {entry["securityId"] for entry in self.entries}
        position_ids = {
            position["positionId"]
            for position in self.operational["portfolio"]["positions"]
        }
        ledger = self.operational["ledger"]
        market_data = self.operational["marketData"]
        cash_and_activity = self.operational["cashAndActivity"]
        operator_access = self.operational["operatorAccess"]
        reporting = self.operational["reporting"]

        self.assertEqual("2026-06-04", market_data["asOf"])
        self.assertEqual(8, len(market_data["priceSnapshots"]))
        self.assertEqual(3, len(market_data["factorSchedules"]))
        self.assertEqual(2, len(market_data["creditRatings"]))
        self.assertEqual(1, len(market_data["equityQuotes"]))
        self.assertTrue(
            {snapshot["securityId"] for snapshot in market_data["priceSnapshots"]}.issubset(security_ids)
        )
        self.assertTrue(
            {schedule["securityId"] for schedule in market_data["factorSchedules"]}.issubset(security_ids)
        )
        self.assertTrue(
            {rating["securityId"] for rating in market_data["creditRatings"]}.issubset(security_ids)
        )

        self.assertEqual(1, len(cash_and_activity["cashBalances"]))
        self.assertEqual(3, len(cash_and_activity["servicingEvents"]))
        self.assertEqual(2, len(cash_and_activity["incomeEvents"]))
        self.assertEqual(52, cash_and_activity["totals"]["directLoanContractCount"])
        self.assertEqual(250000, cash_and_activity["totals"]["unfundedCommitments"])

        self.assertEqual(61, len(ledger["journalEntries"]))
        for journal in ledger["journalEntries"]:
            with self.subTest(journal_entry_id=journal["journalEntryId"]):
                self.assertIn(journal["securityId"], security_ids)
                self.assertIn(journal["positionId"], position_ids)
                total_debits = sum(line["debit"] for line in journal["lines"])
                total_credits = sum(line["credit"] for line in journal["lines"])
                self.assertEqual(total_debits, total_credits)

        self.assertEqual(4, len(operator_access["users"]))
        self.assertGreaterEqual(len(operator_access["rolePermissions"]), 6)
        permissions = {
            permission
            for role in operator_access["rolePermissions"]
            for permission in role["permissions"]
        }
        self.assertIn("SecurityMaster.Write", permissions)
        self.assertIn("Ledger.Post", permissions)
        self.assertIn("Reporting.Approve", permissions)
        self.assertIn("Audit.Read", permissions)

        self.assertEqual("report-pack-mixed-credit-ops", reporting["reportPackId"])
        self.assertEqual(
            {"loan-tape", "holdings", "cash", "reconciliation", "exceptions", "evidence"},
            {section["sectionId"] for section in reporting["requiredSections"]},
        )

    def test_provider_style_source_payload_files_are_generated_and_manifested(self) -> None:
        manifest = self.operational["sourcePayloads"]
        self.assertEqual(6, len(manifest))
        self.assertEqual(
            {
                "ServicerLoanTape",
                "CustodianHoldings",
                "BrokerPositionFile",
                "PricingSnapshot",
                "BankStatement",
                "LifecycleEventFeed",
            },
            {payload["payloadKind"] for payload in manifest},
        )

        for payload in manifest:
            with self.subTest(payload_id=payload["payloadId"]):
                payload_path = FIXTURE_PATH.parent / payload["relativePath"]
                self.assertTrue(payload_path.exists(), payload_path)
                self.assertEqual(payload["relativePath"], str(payload_path.relative_to(FIXTURE_PATH.parent)).replace("\\", "/"))
                if payload["format"] == "csv":
                    with payload_path.open(newline="", encoding="utf-8") as handle:
                        rows = list(csv.DictReader(handle))
                    self.assertEqual(payload["recordCount"], len(rows))
                    self.assertTrue(rows)
                elif payload["format"] == "json":
                    data = json.loads(payload_path.read_text(encoding="utf-8"))
                    self.assertEqual(payload["recordCount"], len(data["events"]))
                else:
                    self.fail(f"Unexpected payload format: {payload['format']}")

        servicer_payload = next(payload for payload in manifest if payload["payloadKind"] == "ServicerLoanTape")
        with (FIXTURE_PATH.parent / servicer_payload["relativePath"]).open(newline="", encoding="utf-8") as handle:
            loan_rows = list(csv.DictReader(handle))
        self.assertEqual(50, len(loan_rows))
        self.assertEqual(3, sum(1 for row in loan_rows if row["delinquency_status"] == "Late"))

    def test_asset_lifecycle_events_cover_corporate_actions_and_loan_accounting(self) -> None:
        security_ids = {entry["securityId"] for entry in self.entries}
        lifecycle = self.operational["assetLifecycleEvents"]

        self.assertEqual("2026-06-04", lifecycle["asOf"])
        self.assertEqual(4, len(lifecycle["corporateActions"]))
        self.assertEqual(6, len(lifecycle["loanEvents"]))
        self.assertEqual(3, len(lifecycle["expectedAccountingActions"]))

        corporate_event_types = {event["eventType"] for event in lifecycle["corporateActions"]}
        self.assertEqual(
            {
                "IssuerEvent",
                "CreditRatingChange",
                "StructuredCreditFactorUpdate",
                "FixedIncomeCouponAccrual",
            },
            corporate_event_types,
        )
        for event in lifecycle["corporateActions"]:
            with self.subTest(corporate_event_id=event["eventId"]):
                self.assertIn(event["securityId"], security_ids)
                self.assertEqual("source-corporate-actions-and-loan-events", event["sourcePayloadId"])

        loan_events = lifecycle["loanEvents"]
        self.assertEqual(3, sum(1 for event in loan_events if event["eventType"] == "PaymentPastDue"))
        self.assertIn("PaymentBatchApplied", {event["eventType"] for event in loan_events})
        self.assertIn("UnusedCommitmentFeeAccrual", {event["eventType"] for event in loan_events})
        for event in loan_events:
            with self.subTest(loan_event_id=event["eventId"]):
                if "securityId" in event:
                    self.assertIn(event["securityId"], security_ids)
                if "securityIds" in event:
                    self.assertTrue(set(event["securityIds"]).issubset(security_ids))
                self.assertEqual("source-corporate-actions-and-loan-events", event["sourcePayloadId"])

        self.assertEqual(
            {
                "acct-accrue-treasury-interest",
                "acct-accrue-loc-unused-fee",
                "acct-apply-personal-loan-payment-batch",
            },
            {action["actionId"] for action in lifecycle["expectedAccountingActions"]},
        )

    def test_permission_persona_scenarios_cover_allow_and_deny_audit_cases(self) -> None:
        operator_access = self.operational["operatorAccess"]
        user_ids = {user["userId"] for user in operator_access["users"]}
        roles = {role["role"] for role in operator_access["rolePermissions"]}
        scenarios = operator_access["permissionScenarios"]

        self.assertIn("fixture-auditor", user_ids)
        self.assertIn("AuditReviewer", roles)
        self.assertEqual(5, len(scenarios))
        self.assertEqual({"Allow", "Deny"}, {scenario["expectedDecision"] for scenario in scenarios})
        self.assertEqual(
            {"Data", "Reporting", "Accounting", "Portfolio", "Settings"},
            {scenario["workspace"] for scenario in scenarios},
        )

        for scenario in scenarios:
            with self.subTest(permission_scenario_id=scenario["scenarioId"]):
                self.assertIn(scenario["userId"], user_ids)
                self.assertTrue(scenario["action"])
                self.assertTrue(scenario["resourceId"])
                self.assertTrue(scenario["auditExpectation"].startswith("Record"))

    def test_operational_readiness_data_covers_servicing_risk_exceptions_and_audit(self) -> None:
        security_ids = {entry["securityId"] for entry in self.entries}
        loan_ids = {
            contract["loanId"]
            for contract in self.operational["directLending"]["loanContracts"]
        }
        account_ids = {
            account["accountId"]
            for account in self.operational["fundStructure"]["accounts"]
        }

        personal_loans = [
            entry for entry in self.entries
            if entry["classification"]["subType"] == "PersonalLoan"
        ]
        late_personal_loans = [
            entry for entry in personal_loans
            if entry["assetSpecificTerms"]["delinquencyStatus"] == "Late"
        ]
        self.assertEqual(3, len(late_personal_loans))
        for entry in personal_loans:
            with self.subTest(personal_loan_id=entry["securityId"]):
                self.assertIn(entry["assetSpecificTerms"]["creditGrade"], {"A", "B", "C", "D"})
                self.assertIn(entry["assetSpecificTerms"]["riskScoreBand"], {"Prime", "NearPrime", "Subprime"})
                self.assertTrue(entry["assetSpecificTerms"]["borrowerState"])
                self.assertGreaterEqual(entry["assetSpecificTerms"]["daysPastDue"], 0)

        servicing_calendar = self.operational["directLending"]["servicingCalendar"]
        self.assertEqual(52, len(servicing_calendar))
        self.assertTrue({item["loanId"] for item in servicing_calendar}.issubset(loan_ids))
        self.assertTrue({item["securityId"] for item in servicing_calendar}.issubset(security_ids))
        self.assertIn("AvailabilityReview", {item["itemType"] for item in servicing_calendar})
        self.assertIn("PaymentDue", {item["itemType"] for item in servicing_calendar})

        settlement_instructions = self.operational["settlementInstructions"]
        self.assertEqual(3, len(settlement_instructions))
        self.assertTrue({instruction["accountId"] for instruction in settlement_instructions}.issubset(account_ids))
        self.assertTrue({instruction["cashAccountId"] for instruction in settlement_instructions}.issubset(account_ids))
        self.assertEqual({"Approved"}, {instruction["status"] for instruction in settlement_instructions})

        risk_and_compliance = self.operational["riskAndCompliance"]
        self.assertEqual(3, risk_and_compliance["exposureSummary"]["latePersonalLoanCount"])
        self.assertEqual(250000, risk_and_compliance["exposureSummary"]["undrawnCommitmentExposure"])
        self.assertEqual(4, len(risk_and_compliance["limits"]))
        self.assertEqual(5, len(risk_and_compliance["watchlist"]))
        self.assertEqual(3, len(risk_and_compliance["stressScenarios"]))
        self.assertEqual(3, len(risk_and_compliance["complianceChecks"]))
        for scenario in risk_and_compliance["stressScenarios"]:
            with self.subTest(stress_scenario_id=scenario["scenarioId"]):
                self.assertTrue(set(scenario["affectedSecurityIds"]).issubset(security_ids))

        exceptions = self.operational["exceptions"]
        self.assertEqual(6, len(exceptions))
        self.assertEqual({"Open"}, {exception["status"] for exception in exceptions})
        self.assertTrue({exception["securityId"] for exception in exceptions}.issubset(security_ids))
        self.assertEqual(
            {"LoanServicing", "Valuation", "CreditRisk", "Reporting"},
            {exception["category"] for exception in exceptions},
        )

        data_quality_controls = self.operational["dataQualityControls"]
        self.assertEqual(4, len(data_quality_controls))
        self.assertEqual(
            {"Pass", "ReviewRequired"},
            {control["status"] for control in data_quality_controls},
        )
        self.assertEqual(3, sum(control["exceptionCount"] for control in data_quality_controls))

        audit_trail = self.operational["auditTrail"]
        self.assertEqual(6, len(audit_trail))
        self.assertEqual(
            [
                "ImportSecurityMaster",
                "ApproveLedgerMappings",
                "ImportServicingTape",
                "ImportMarketData",
                "OpenExceptionQueue",
                "CreateReportPack",
            ],
            [audit["action"] for audit in audit_trail],
        )

    def test_capability_expectations_reference_real_holdings(self) -> None:
        security_ids = {entry["securityId"] for entry in self.entries}
        expectations = self.fixture["capabilityExpectations"]

        self.assertGreaterEqual(len(expectations), 5)
        for expectation in expectations:
            self.assertTrue(expectation["capabilityId"])
            self.assertTrue(expectation["description"])
            referenced_ids = expectation.get("mustSupportSecurityIds", [])
            if referenced_ids:
                self.assertTrue(set(referenced_ids).issubset(security_ids))

    def test_security_master_shape_covers_each_entry(self) -> None:
        for entry in self.entries:
            with self.subTest(security_id=entry["securityId"]):
                self.assertTrue(entry["securityId"])
                self.assertTrue(entry["displayName"])
                self.assertTrue(entry["status"])
                self.assertTrue(entry["classification"]["assetClass"])
                self.assertTrue(entry["classification"]["subType"])
                self.assertTrue(entry["classification"]["primaryIdentifierKind"])
                self.assertTrue(entry["classification"]["primaryIdentifierValue"])
                self.assertEqual(entry["commonTerms"]["currency"], entry["economicDefinition"]["currency"])
                self.assertTrue(entry["assetSpecificTerms"])
                self.assertTrue(entry["identifiers"])


if __name__ == "__main__":
    unittest.main()
