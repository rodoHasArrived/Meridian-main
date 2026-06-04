from __future__ import annotations

import json
from pathlib import Path


AS_OF = "2026-06-04"
OUT = Path(__file__).with_name("mixed-credit-status-set.json")
SOURCE_DIR = Path(__file__).with_name("source-payloads")

ORG_ID = "aaaaaaaa-aaaa-4aaa-8aaa-000000000001"
BUSINESS_ID = "aaaaaaaa-aaaa-4aaa-8aaa-000000000002"
FUND_ID = "aaaaaaaa-aaaa-4aaa-8aaa-000000000010"
PRIVATE_CREDIT_SLEEVE_ID = "aaaaaaaa-aaaa-4aaa-8aaa-000000000011"
PUBLIC_SECURITIES_SLEEVE_ID = "aaaaaaaa-aaaa-4aaa-8aaa-000000000012"
FUND_ENTITY_ID = "bbbbbbbb-bbbb-4bbb-8bbb-000000000001"
VEHICLE_ID = "bbbbbbbb-bbbb-4bbb-8bbb-000000000002"
PORTFOLIO_ID = "cccccccc-cccc-4ccc-8ccc-000000000001"
CUSTODY_ACCOUNT_ID = "aaaaaaaa-aaaa-4aaa-8aaa-000000000101"
BROKERAGE_ACCOUNT_ID = "aaaaaaaa-aaaa-4aaa-8aaa-000000000102"
LOAN_SERVICING_ACCOUNT_ID = "aaaaaaaa-aaaa-4aaa-8aaa-000000000103"
BANK_ACCOUNT_ID = "aaaaaaaa-aaaa-4aaa-8aaa-000000000104"
LEDGER_BOOK_ID = "dddddddd-dddd-4ddd-8ddd-000000000001"


def security_master_entry(
    security_id: str,
    display_name: str,
    asset_class: str,
    sub_type: str,
    primary_kind: str,
    primary_value: str,
    asset_family: str,
    issuer_type: str,
    asset_terms: dict[str, object],
) -> dict[str, object]:
    return {
        "securityId": security_id,
        "displayName": display_name,
        "status": "Active",
        "classification": {
            "assetClass": asset_class,
            "subType": sub_type,
            "primaryIdentifierKind": primary_kind,
            "primaryIdentifierValue": primary_value,
            "matchedIdentifierKind": primary_kind,
            "matchedIdentifierValue": primary_value,
            "matchedProvider": "Fixture",
        },
        "economicDefinition": {
            "currency": "USD",
            "version": 1,
            "effectiveFrom": AS_OF,
            "effectiveTo": None,
            "subType": sub_type,
            "assetFamily": asset_family,
            "issuerType": issuer_type,
        },
        "commonTerms": {
            "currency": "USD",
            "displayName": display_name,
        },
        "assetSpecificTerms": asset_terms,
        "identifiers": [
            {
                "kind": primary_kind,
                "value": primary_value,
                "isPrimary": True,
                "validFrom": AS_OF,
                "validTo": None,
                "provider": "Fixture",
            }
        ],
    }


def build_personal_loan_entries() -> list[dict[str, object]]:
    entries: list[dict[str, object]] = []
    for index in range(1, 51):
        original = 9_750 + index * 250
        outstanding = original - (225 + index * 25)
        coupon = round(0.105 + ((index - 1) % 20) * 0.001, 4)
        entries.append(
            security_master_entry(
                f"11111111-1111-4111-8111-{index:012d}",
                f"Personal Loan {index:03d}",
                "DirectLoan",
                "PersonalLoan",
                "InternalCode",
                f"MPL-{index:04d}",
                "PrivateCredit",
                "ConsumerBorrower",
                {
                    "borrowerReference": f"fixture-borrower-{index:03d}",
                    "originalPrincipal": original,
                    "outstandingPrincipal": outstanding,
                    "coupon": coupon,
                    "creditGrade": ["A", "B", "C", "D"][index % 4],
                    "riskScoreBand": ["Prime", "NearPrime", "Subprime"][index % 3],
                    "borrowerState": ["AZ", "CA", "CO", "FL", "IL", "NY", "TX", "WA"][index % 8],
                    "daysPastDue": 15 if index in {7, 23, 41} else 0,
                    "delinquencyStatus": "Late" if index in {7, 23, 41} else "Current",
                    "originationDate": "2026-01-15",
                    "maturityDate": "2029-01-15",
                    "paymentFrequency": "Monthly",
                    "amortizationType": "Installment",
                    "servicingAccountId": LOAN_SERVICING_ACCOUNT_ID,
                    "ledgerAccountCode": "1205-PERSONAL-LOANS",
                },
            )
        )
    return entries


def build_other_security_entries() -> list[dict[str, object]]:
    specs = [
        (
            "22222222-2222-4222-8222-000000000001",
            "Agency residential MBS pack A",
            "CustomAsset",
            "MortgageBackedSecurity",
            "InternalCode",
            "MBS-A-001",
            "StructuredCredit",
            "AgencyTrust",
            {
                "notionalAmount": 350_000,
                "marketValue": 341_250,
                "coupon": 0.045,
                "factorRequired": True,
                "servicerEvidenceRequired": True,
                "servicerEntityId": "eeeeeeee-eeee-4eee-8eee-000000000001",
                "ledgerAccountCode": "1210-MBS",
            },
        ),
        (
            "22222222-2222-4222-8222-000000000002",
            "Non-agency residential MBS pack B",
            "CustomAsset",
            "MortgageBackedSecurity",
            "InternalCode",
            "MBS-B-001",
            "StructuredCredit",
            "PrivateTrust",
            {
                "notionalAmount": 275_000,
                "marketValue": 259_875,
                "coupon": 0.0625,
                "factorRequired": True,
                "servicerEvidenceRequired": True,
                "valuationApprovalRequired": True,
                "servicerEntityId": "eeeeeeee-eeee-4eee-8eee-000000000001",
                "ledgerAccountCode": "1210-MBS",
            },
        ),
        (
            "22222222-2222-4222-8222-000000000003",
            "CMBS diversified pack C",
            "CustomAsset",
            "CommercialMortgageBackedSecurity",
            "InternalCode",
            "CMBS-C-001",
            "StructuredCredit",
            "PrivateTrust",
            {
                "notionalAmount": 225_000,
                "marketValue": 218_500,
                "coupon": 0.0575,
                "factorRequired": True,
                "trusteeEvidenceRequired": True,
                "valuationApprovalRequired": True,
                "trusteeEntityId": "eeeeeeee-eeee-4eee-8eee-000000000002",
                "ledgerAccountCode": "1210-MBS",
            },
        ),
        (
            "33333333-3333-4333-8333-000000000001",
            "U.S. Treasury note 2027",
            "FixedIncome",
            "TreasuryNote",
            "Cusip",
            "91282CGT2",
            "Rates",
            "Sovereign",
            {
                "faceAmount": 200_000,
                "marketValue": 198_800,
                "coupon": 0.0425,
                "maturityDate": "2027-05-31",
                "ledgerAccountCode": "1220-TREASURIES",
            },
        ),
        (
            "33333333-3333-4333-8333-000000000002",
            "U.S. Treasury bill 2026",
            "FixedIncome",
            "TreasuryBill",
            "Cusip",
            "912797KS5",
            "Rates",
            "Sovereign",
            {
                "faceAmount": 150_000,
                "marketValue": 148_950,
                "discountRate": 0.0505,
                "maturityDate": "2026-12-10",
                "ledgerAccountCode": "1220-TREASURIES",
            },
        ),
        (
            "44444444-4444-4444-8444-000000000001",
            "Investment-grade corporate bond",
            "FixedIncome",
            "CorporateBond",
            "Cusip",
            "594918CL0",
            "Credit",
            "Corporate",
            {
                "faceAmount": 125_000,
                "marketValue": 123_750,
                "coupon": 0.051,
                "maturityDate": "2031-02-15",
                "ratingEvidenceRequired": True,
                "ledgerAccountCode": "1230-CORPORATES",
            },
        ),
        (
            "44444444-4444-4444-8444-000000000002",
            "High-yield corporate bond",
            "FixedIncome",
            "CorporateBond",
            "Cusip",
            "U0237LAA8",
            "Credit",
            "Corporate",
            {
                "faceAmount": 100_000,
                "marketValue": 94_500,
                "coupon": 0.07875,
                "maturityDate": "2030-09-30",
                "ratingEvidenceRequired": True,
                "creditRiskReviewRequired": True,
                "ledgerAccountCode": "1230-CORPORATES",
            },
        ),
        (
            "55555555-5555-4555-8555-000000000001",
            "indie Semiconductor Inc. common shares",
            "Equity",
            "CommonStock",
            "Ticker",
            "INDI",
            "Equities",
            "Corporate",
            {
                "symbol": "INDI",
                "quantity": 10_000,
                "costBasis": 65_000,
                "marketValue": 78_000,
                "ledgerAccountCode": "1240-EQUITIES",
            },
        ),
        (
            "66666666-6666-4666-8666-000000000001",
            "Standalone funded term loan",
            "DirectLoan",
            "TermLoan",
            "InternalCode",
            "TERM-500K-001",
            "PrivateCredit",
            "CorporateBorrower",
            {
                "borrowerReference": "borrower-fixture-term-loan",
                "commitmentAmount": 500_000,
                "drawnPrincipal": 500_000,
                "unfundedCommitment": 0,
                "coupon": 0.095,
                "maturityDate": "2029-06-30",
                "servicingAccountId": LOAN_SERVICING_ACCOUNT_ID,
                "ledgerAccountCode": "1200-TERM-LOANS",
            },
        ),
        (
            "77777777-7777-4777-8777-000000000001",
            "Undrawn revolving line of credit",
            "DirectLoan",
            "LineOfCredit",
            "InternalCode",
            "LOC-UNDRAWN-001",
            "PrivateCredit",
            "CorporateBorrower",
            {
                "borrowerReference": "borrower-fixture-loc",
                "commitmentAmount": 250_000,
                "drawnPrincipal": 0,
                "unfundedCommitment": 250_000,
                "unusedCommitmentFeeRate": 0.0125,
                "servicingAccountId": LOAN_SERVICING_ACCOUNT_ID,
                "ledgerAccountCode": "2100-UNFUNDED-COMMITMENTS",
            },
        ),
    ]
    return [security_master_entry(*spec) for spec in specs]


def build_fund_structure() -> dict[str, object]:
    return {
        "organizations": [
            {
                "organizationId": ORG_ID,
                "code": "MIXED-OPS",
                "name": "Mixed Credit Operations Fixture",
                "baseCurrency": "USD",
                "isActive": True,
                "effectiveFrom": AS_OF,
                "effectiveTo": None,
                "businessIds": [BUSINESS_ID],
            }
        ],
        "businesses": [
            {
                "businessId": BUSINESS_ID,
                "organizationId": ORG_ID,
                "businessKind": "FundManager",
                "code": "MIXED-CREDIT-MGR",
                "name": "Mixed Credit Manager",
                "baseCurrency": "USD",
                "isActive": True,
                "effectiveFrom": AS_OF,
                "effectiveTo": None,
                "clientIds": [],
                "fundIds": [FUND_ID],
                "investmentPortfolioIds": [PORTFOLIO_ID],
            }
        ],
        "funds": [
            {
                "fundId": FUND_ID,
                "businessId": BUSINESS_ID,
                "code": "MCF-I",
                "name": "Mixed Credit Fund I",
                "baseCurrency": "USD",
                "isActive": True,
                "effectiveFrom": AS_OF,
                "effectiveTo": None,
                "sleeveIds": [PRIVATE_CREDIT_SLEEVE_ID, PUBLIC_SECURITIES_SLEEVE_ID],
                "vehicleIds": [VEHICLE_ID],
                "entityIds": [FUND_ENTITY_ID],
                "investmentPortfolioIds": [PORTFOLIO_ID],
                "accountIds": [
                    CUSTODY_ACCOUNT_ID,
                    BROKERAGE_ACCOUNT_ID,
                    LOAN_SERVICING_ACCOUNT_ID,
                    BANK_ACCOUNT_ID,
                ],
            }
        ],
        "sleeves": [
            {
                "sleeveId": PRIVATE_CREDIT_SLEEVE_ID,
                "fundId": FUND_ID,
                "code": "PRIV-CREDIT",
                "name": "Private Credit Sleeve",
                "mandate": "Personal loans, direct loans, and credit facilities",
                "isActive": True,
                "effectiveFrom": AS_OF,
                "effectiveTo": None,
                "strategyIds": [],
                "investmentPortfolioIds": [PORTFOLIO_ID],
                "accountIds": [LOAN_SERVICING_ACCOUNT_ID, BANK_ACCOUNT_ID],
            },
            {
                "sleeveId": PUBLIC_SECURITIES_SLEEVE_ID,
                "fundId": FUND_ID,
                "code": "PUBLIC-SEC",
                "name": "Public Securities Sleeve",
                "mandate": "Mortgage securities, Treasuries, corporate bonds, and equities",
                "isActive": True,
                "effectiveFrom": AS_OF,
                "effectiveTo": None,
                "strategyIds": [],
                "investmentPortfolioIds": [PORTFOLIO_ID],
                "accountIds": [CUSTODY_ACCOUNT_ID, BROKERAGE_ACCOUNT_ID],
            },
        ],
        "entities": [
            {
                "entityId": FUND_ENTITY_ID,
                "entityType": "Fund",
                "code": "MCF-I-LP",
                "name": "Mixed Credit Fund I LP",
                "jurisdiction": "US-DE",
                "baseCurrency": "USD",
                "isActive": True,
                "effectiveFrom": AS_OF,
                "effectiveTo": None,
            },
            {
                "entityId": "eeeeeeee-eeee-4eee-8eee-000000000003",
                "entityType": "Custodian",
                "code": "FIX-CUST",
                "name": "Fixture Custody Bank",
                "jurisdiction": "US-NY",
                "baseCurrency": "USD",
                "isActive": True,
                "effectiveFrom": AS_OF,
                "effectiveTo": None,
            },
            {
                "entityId": "eeeeeeee-eeee-4eee-8eee-000000000004",
                "entityType": "Broker",
                "code": "FIX-BROKER",
                "name": "Fixture Securities Broker",
                "jurisdiction": "US-NY",
                "baseCurrency": "USD",
                "isActive": True,
                "effectiveFrom": AS_OF,
                "effectiveTo": None,
            },
        ],
        "vehicles": [
            {
                "vehicleId": VEHICLE_ID,
                "fundId": FUND_ID,
                "legalEntityId": FUND_ENTITY_ID,
                "code": "MCF-MASTER",
                "name": "Mixed Credit Master Vehicle",
                "baseCurrency": "USD",
                "isActive": True,
                "effectiveFrom": AS_OF,
                "effectiveTo": None,
                "investmentPortfolioIds": [PORTFOLIO_ID],
                "accountIds": [
                    CUSTODY_ACCOUNT_ID,
                    BROKERAGE_ACCOUNT_ID,
                    LOAN_SERVICING_ACCOUNT_ID,
                    BANK_ACCOUNT_ID,
                ],
            }
        ],
        "investmentPortfolios": [
            {
                "investmentPortfolioId": PORTFOLIO_ID,
                "businessId": BUSINESS_ID,
                "code": "MCF-OPS-PORT",
                "name": "Mixed Credit Operational Portfolio",
                "baseCurrency": "USD",
                "isActive": True,
                "effectiveFrom": AS_OF,
                "effectiveTo": None,
                "clientId": None,
                "fundId": FUND_ID,
                "sleeveId": None,
                "vehicleId": VEHICLE_ID,
                "entityId": FUND_ENTITY_ID,
                "accountIds": [
                    CUSTODY_ACCOUNT_ID,
                    BROKERAGE_ACCOUNT_ID,
                    LOAN_SERVICING_ACCOUNT_ID,
                    BANK_ACCOUNT_ID,
                ],
            }
        ],
        "accounts": [
            account(CUSTODY_ACCOUNT_ID, "Custody", PUBLIC_SECURITIES_SLEEVE_ID, "CUST-MCF-001", "Custody account - fixed income and structured credit", "Fixture Custody Bank"),
            account(BROKERAGE_ACCOUNT_ID, "Brokerage", PUBLIC_SECURITIES_SLEEVE_ID, "BRK-MCF-001", "Brokerage account - listed equity", "Fixture Securities Broker"),
            account(LOAN_SERVICING_ACCOUNT_ID, "LedgerControl", PRIVATE_CREDIT_SLEEVE_ID, "LOAN-MCF-001", "Loan servicing subledger", "Meridian Direct Lending"),
            account(BANK_ACCOUNT_ID, "Bank", PRIVATE_CREDIT_SLEEVE_ID, "BANK-MCF-001", "Operating cash account", "Fixture Bank"),
        ],
        "ownershipLinks": [
            link("abababab-abab-4aba-8aba-000000000001", ORG_ID, BUSINESS_ID, "Operates", None),
            link("abababab-abab-4aba-8aba-000000000002", BUSINESS_ID, FUND_ID, "Operates", None),
            link("abababab-abab-4aba-8aba-000000000003", FUND_ID, VEHICLE_ID, "Owns", 100),
            link("abababab-abab-4aba-8aba-000000000004", VEHICLE_ID, PORTFOLIO_ID, "AllocatesTo", 100),
        ],
        "assignments": [
            assignment("cdcdcdcd-cdcd-4cdc-8cdc-000000000001", CUSTODY_ACCOUNT_ID),
            assignment("cdcdcdcd-cdcd-4cdc-8cdc-000000000002", BROKERAGE_ACCOUNT_ID),
            assignment("cdcdcdcd-cdcd-4cdc-8cdc-000000000003", LOAN_SERVICING_ACCOUNT_ID),
            assignment("cdcdcdcd-cdcd-4cdc-8cdc-000000000004", BANK_ACCOUNT_ID),
        ],
    }


def account(account_id: str, account_type: str, sleeve_id: str, code: str, name: str, institution: str) -> dict[str, object]:
    return {
        "accountId": account_id,
        "accountType": account_type,
        "entityId": FUND_ENTITY_ID,
        "fundId": FUND_ID,
        "sleeveId": sleeve_id,
        "vehicleId": VEHICLE_ID,
        "accountCode": code,
        "displayName": name,
        "baseCurrency": "USD",
        "institution": institution,
        "isActive": True,
        "effectiveFrom": AS_OF,
        "effectiveTo": None,
        "portfolioId": PORTFOLIO_ID,
        "ledgerReference": LEDGER_BOOK_ID,
        "strategyId": None,
        "runId": None,
        "operationalStatus": "Active",
    }


def link(link_id: str, parent_id: str, child_id: str, relationship_type: str, ownership_percent: int | None) -> dict[str, object]:
    return {
        "ownershipLinkId": link_id,
        "parentNodeId": parent_id,
        "childNodeId": child_id,
        "relationshipType": relationship_type,
        "ownershipPercent": ownership_percent,
        "isPrimary": True,
        "effectiveFrom": AS_OF,
        "effectiveTo": None,
    }


def assignment(assignment_id: str, node_id: str) -> dict[str, object]:
    return {
        "assignmentId": assignment_id,
        "nodeId": node_id,
        "assignmentType": "LedgerGroup",
        "assignmentReference": LEDGER_BOOK_ID,
        "effectiveFrom": AS_OF,
        "effectiveTo": None,
        "isPrimary": True,
    }


def party_records(personal_entries: list[dict[str, object]]) -> list[dict[str, object]]:
    parties = [
        {
            "partyId": f"bbbbbbbb-bbbb-4bbb-8bb1-{index:012d}",
            "partyType": "Borrower",
            "displayName": f"Synthetic Consumer Borrower {index:03d}",
            "privacyClass": "SyntheticNoPii",
            "relatedSecurityId": entry["securityId"],
            "borrowerReference": entry["assetSpecificTerms"]["borrowerReference"],
        }
        for index, entry in enumerate(personal_entries, start=1)
    ]
    parties.extend(
        [
            {
                "partyId": "bbbbbbbb-bbbb-4bbb-8bb2-000000000001",
                "partyType": "Borrower",
                "displayName": "Fixture Term Loan Borrower LLC",
                "privacyClass": "SyntheticEntity",
                "relatedSecurityId": "66666666-6666-4666-8666-000000000001",
                "borrowerReference": "borrower-fixture-term-loan",
            },
            {
                "partyId": "bbbbbbbb-bbbb-4bbb-8bb2-000000000002",
                "partyType": "Borrower",
                "displayName": "Fixture LOC Borrower LLC",
                "privacyClass": "SyntheticEntity",
                "relatedSecurityId": "77777777-7777-4777-8777-000000000001",
                "borrowerReference": "borrower-fixture-loc",
            },
            {
                "partyId": "eeeeeeee-eeee-4eee-8eee-000000000001",
                "partyType": "Servicer",
                "displayName": "Fixture Structured Credit Servicer",
                "privacyClass": "SyntheticEntity",
            },
            {
                "partyId": "eeeeeeee-eeee-4eee-8eee-000000000002",
                "partyType": "Trustee",
                "displayName": "Fixture CMBS Trustee",
                "privacyClass": "SyntheticEntity",
            },
            {
                "partyId": "eeeeeeee-eeee-4eee-8eee-000000000003",
                "partyType": "Custodian",
                "displayName": "Fixture Custody Bank",
                "privacyClass": "SyntheticEntity",
            },
            {
                "partyId": "eeeeeeee-eeee-4eee-8eee-000000000004",
                "partyType": "Broker",
                "displayName": "Fixture Securities Broker",
                "privacyClass": "SyntheticEntity",
            },
            {
                "partyId": "eeeeeeee-eeee-4eee-8eee-000000000005",
                "partyType": "PricingProvider",
                "displayName": "Fixture Pricing Provider",
                "privacyClass": "SyntheticEntity",
            },
        ]
    )
    return parties


def portfolio_position(position_id: str, security_id: str, account_id: str, quantity: int, units: str, cost_basis: int, market_value: int, status: str = "Open") -> dict[str, object]:
    return {
        "positionId": position_id,
        "portfolioId": PORTFOLIO_ID,
        "accountId": account_id,
        "securityId": security_id,
        "quantity": quantity,
        "quantityUnits": units,
        "costBasis": cost_basis,
        "marketValue": market_value,
        "currency": "USD",
        "status": status,
        "asOf": AS_OF,
        "ledgerBookId": LEDGER_BOOK_ID,
    }


def build_positions(personal_entries: list[dict[str, object]], other_entries: list[dict[str, object]]) -> list[dict[str, object]]:
    positions = [
        portfolio_position(
            f"pos-personal-loan-{index:03d}",
            entry["securityId"],
            LOAN_SERVICING_ACCOUNT_ID,
            entry["assetSpecificTerms"]["outstandingPrincipal"],
            "PrincipalOutstanding",
            entry["assetSpecificTerms"]["outstandingPrincipal"],
            entry["assetSpecificTerms"]["outstandingPrincipal"],
        )
        for index, entry in enumerate(personal_entries, start=1)
    ]
    specs = [
        ("pos-mbs-pack-a", other_entries[0], CUSTODY_ACCOUNT_ID, "notionalAmount", "FaceAmount"),
        ("pos-mbs-pack-b", other_entries[1], CUSTODY_ACCOUNT_ID, "notionalAmount", "FaceAmount"),
        ("pos-cmbs-pack-c", other_entries[2], CUSTODY_ACCOUNT_ID, "notionalAmount", "FaceAmount"),
        ("pos-ust-note-2027", other_entries[3], CUSTODY_ACCOUNT_ID, "faceAmount", "FaceAmount"),
        ("pos-tbill-2026", other_entries[4], CUSTODY_ACCOUNT_ID, "faceAmount", "FaceAmount"),
        ("pos-corp-ig", other_entries[5], CUSTODY_ACCOUNT_ID, "faceAmount", "FaceAmount"),
        ("pos-corp-hy", other_entries[6], CUSTODY_ACCOUNT_ID, "faceAmount", "FaceAmount"),
    ]
    for position_id, entry, account_id, quantity_key, units in specs:
        terms = entry["assetSpecificTerms"]
        positions.append(portfolio_position(position_id, entry["securityId"], account_id, terms[quantity_key], units, terms[quantity_key], terms["marketValue"]))
    positions.append(portfolio_position("pos-indi", other_entries[7]["securityId"], BROKERAGE_ACCOUNT_ID, 10_000, "Shares", 65_000, 78_000))
    positions.append(portfolio_position("pos-term-loan-500k", other_entries[8]["securityId"], LOAN_SERVICING_ACCOUNT_ID, 500_000, "PrincipalOutstanding", 500_000, 500_000))
    positions.append(portfolio_position("pos-undrawn-loc", other_entries[9]["securityId"], LOAN_SERVICING_ACCOUNT_ID, 250_000, "Commitment", 0, 0, "Undrawn"))
    return positions


def loan_contract(entry: dict[str, object], loan_id: str, borrower_id: str, borrower_name: str) -> dict[str, object]:
    terms = entry["assetSpecificTerms"]
    commitment = terms.get("commitmentAmount", terms.get("originalPrincipal", terms.get("outstandingPrincipal", 0)))
    drawn = terms.get("drawnPrincipal", terms.get("outstandingPrincipal", 0))
    unfunded = terms.get("unfundedCommitment", max(commitment - drawn, 0))
    identifier = entry["classification"]["primaryIdentifierValue"]
    return {
        "loanId": loan_id,
        "facilityName": entry["displayName"],
        "borrower": {"borrowerId": borrower_id, "borrowerName": borrower_name, "legalEntityId": None},
        "status": "Active",
        "effectiveDate": terms.get("originationDate", AS_OF),
        "activationDate": terms.get("originationDate", AS_OF) if drawn > 0 else None,
        "closeDate": None,
        "currentTermsVersion": 1,
        "currentTerms": {
            "originationDate": terms.get("originationDate", AS_OF),
            "maturityDate": terms.get("maturityDate", "2029-01-15"),
            "commitmentAmount": commitment,
            "baseCurrency": "USD",
            "rateTypeKind": "Fixed",
            "fixedAnnualRate": terms.get("coupon", 0),
            "interestIndexName": None,
            "spreadBps": None,
            "floorRate": None,
            "capRate": None,
            "dayCountBasis": "Actual360",
            "paymentFrequency": terms.get("paymentFrequency", "Monthly"),
            "amortizationType": terms.get("amortizationType", "Installment"),
            "commitmentFeeRate": terms.get("unusedCommitmentFeeRate"),
            "defaultRateSpreadBps": 300,
            "prepaymentAllowed": True,
            "covenantsJson": "{\"fixture\":\"synthetic\"}",
            "securityMasterReference": {
                "securityId": entry["securityId"],
                "symbol": identifier,
                "provenance": f"security-master:{entry['securityId']};fixture:{OUT.name}",
                "approvalReference": "approval://fixture/mixed-credit/status-set",
                "ledgerMappingReference": f"ledger-map://{terms.get('ledgerAccountCode', 'unmapped')}",
                "status": "Active",
            },
        },
        "servicing": {
            "loanId": loan_id,
            "status": "Active",
            "currentCommitment": commitment,
            "totalDrawn": drawn,
            "availableToDraw": unfunded,
            "balances": {
                "principalOutstanding": drawn,
                "interestAccruedUnpaid": 0,
                "commitmentFeeAccruedUnpaid": 0,
                "feesAccruedUnpaid": 0,
                "penaltyAccruedUnpaid": 0,
            },
            "drawdownLots": [] if drawn == 0 else [{
                "lotId": loan_id.replace("90000000", "90000001", 1),
                "drawdownDate": terms.get("originationDate", AS_OF),
                "settleDate": terms.get("originationDate", AS_OF),
                "originalPrincipal": drawn,
                "remainingPrincipal": drawn,
                "externalRef": f"fixture-draw-{identifier}",
            }],
            "currentRateReset": None,
            "lastAccrualDate": AS_OF if drawn > 0 else None,
            "lastPaymentDate": None,
            "servicingRevision": 1,
            "revisionHistory": [],
        },
    }


def build_loan_contracts(personal_entries: list[dict[str, object]], other_entries: list[dict[str, object]]) -> list[dict[str, object]]:
    contracts = [
        loan_contract(
            entry,
            f"90000000-0000-4000-8000-{index:012d}",
            f"bbbbbbbb-bbbb-4bbb-8bb1-{index:012d}",
            f"Synthetic Consumer Borrower {index:03d}",
        )
        for index, entry in enumerate(personal_entries, start=1)
    ]
    contracts.append(loan_contract(other_entries[8], "90000000-0000-4000-8000-000000000501", "bbbbbbbb-bbbb-4bbb-8bb2-000000000001", "Fixture Term Loan Borrower LLC"))
    contracts.append(loan_contract(other_entries[9], "90000000-0000-4000-8000-000000000502", "bbbbbbbb-bbbb-4bbb-8bb2-000000000002", "Fixture LOC Borrower LLC"))
    return contracts


def build_market_data(other_entries: list[dict[str, object]]) -> dict[str, object]:
    mortgage_entries = other_entries[:3]
    treasury_entries = other_entries[3:5]
    corporate_entries = other_entries[5:7]
    equity_entry = other_entries[7]

    return {
        "asOf": AS_OF,
        "pricingProviderId": "eeeeeeee-eeee-4eee-8eee-000000000005",
        "priceSnapshots": [
            {
                "securityId": entry["securityId"],
                "priceType": "MarketValue",
                "marketValue": entry["assetSpecificTerms"]["marketValue"],
                "currency": "USD",
                "source": "Fixture Pricing Provider",
                "freshness": "Current",
            }
            for entry in mortgage_entries + treasury_entries + corporate_entries + [equity_entry]
        ],
        "factorSchedules": [
            {
                "securityId": entry["securityId"],
                "factorDate": AS_OF,
                "currentFactor": factor,
                "nextExpectedReportDate": "2026-07-10",
                "source": "Fixture Servicer Tape",
            }
            for entry, factor in zip(mortgage_entries, [0.974125, 0.944982, 0.971111])
        ],
        "creditRatings": [
            {
                "securityId": entry["securityId"],
                "ratingAgency": agency,
                "rating": rating,
                "outlook": outlook,
                "effectiveDate": AS_OF,
                "source": "Fixture Ratings File",
            }
            for entry, agency, rating, outlook in [
                (corporate_entries[0], "Moody's", "A2", "Stable"),
                (corporate_entries[1], "S&P", "BB-", "Negative"),
            ]
        ],
        "equityQuotes": [
            {
                "securityId": equity_entry["securityId"],
                "symbol": "INDI",
                "lastPrice": 7.8,
                "quantity": equity_entry["assetSpecificTerms"]["quantity"],
                "marketValue": equity_entry["assetSpecificTerms"]["marketValue"],
                "source": "Fixture Brokerage Feed",
            }
        ],
    }


def build_cash_and_activity(personal_entries: list[dict[str, object]], other_entries: list[dict[str, object]], contracts: list[dict[str, object]]) -> dict[str, object]:
    personal_cash = sum(entry["assetSpecificTerms"]["outstandingPrincipal"] for entry in personal_entries)
    term_loan_security_id = other_entries[8]["securityId"]
    loc_security_id = other_entries[9]["securityId"]

    return {
        "cashBalances": [
            {
                "accountId": BANK_ACCOUNT_ID,
                "currency": "USD",
                "asOf": AS_OF,
                "availableCash": 175_000,
                "ledgerAccountCode": "1000-CASH",
                "source": "Fixture Bank Statement",
            }
        ],
        "servicingEvents": [
            {
                "eventId": "event-personal-loan-monthly-batch",
                "eventType": "BorrowerPaymentBatch",
                "eventDate": AS_OF,
                "accountId": LOAN_SERVICING_ACCOUNT_ID,
                "loanCount": len(personal_entries),
                "principalApplied": 37_500,
                "interestApplied": 8_950,
                "securityIds": [entry["securityId"] for entry in personal_entries],
                "source": "Fixture Servicing Tape",
            },
            {
                "eventId": "event-term-loan-draw-funded",
                "eventType": "DrawdownFunding",
                "eventDate": AS_OF,
                "accountId": LOAN_SERVICING_ACCOUNT_ID,
                "securityId": term_loan_security_id,
                "amount": 500_000,
                "source": "Fixture Wire Confirmation",
            },
            {
                "eventId": "event-loc-commitment-opened",
                "eventType": "CommitmentOpened",
                "eventDate": AS_OF,
                "accountId": LOAN_SERVICING_ACCOUNT_ID,
                "securityId": loc_security_id,
                "commitmentAmount": 250_000,
                "drawnAmount": 0,
                "source": "Fixture Credit Agreement",
            },
        ],
        "incomeEvents": [
            {
                "eventId": "event-mbs-paydown",
                "eventType": "StructuredCreditPaydown",
                "eventDate": AS_OF,
                "accountId": CUSTODY_ACCOUNT_ID,
                "securityIds": [entry["securityId"] for entry in other_entries[:3]],
                "principalPaydown": 5_875,
                "interestReceived": 2_790,
                "source": "Fixture Servicer Remittance",
            },
            {
                "eventId": "event-fixed-income-accrual",
                "eventType": "FixedIncomeAccrual",
                "eventDate": AS_OF,
                "accountId": CUSTODY_ACCOUNT_ID,
                "securityIds": [entry["securityId"] for entry in other_entries[3:7]],
                "interestAccrued": 3_215,
                "source": "Fixture Accrual Engine",
            },
        ],
        "totals": {
            "personalLoanPrincipalOutstanding": personal_cash,
            "directLoanContractCount": len(contracts),
            "unfundedCommitments": 250_000,
        },
    }


def build_source_payload_manifest(personal_entries: list[dict[str, object]], other_entries: list[dict[str, object]]) -> list[dict[str, object]]:
    return [
        {
            "payloadId": "source-servicer-personal-loans",
            "provider": "Fixture Loan Servicer",
            "workspace": "Data",
            "payloadKind": "ServicerLoanTape",
            "format": "csv",
            "relativePath": "source-payloads/servicer-personal-loans-2026-06-04.csv",
            "recordCount": len(personal_entries),
            "targetStore": "DirectLending",
            "importExpectation": "CreateOrUpdateIndividualLoanContracts",
        },
        {
            "payloadId": "source-custodian-holdings",
            "provider": "Fixture Custody Bank",
            "workspace": "Portfolio",
            "payloadKind": "CustodianHoldings",
            "format": "csv",
            "relativePath": "source-payloads/custodian-holdings-2026-06-04.csv",
            "recordCount": 7,
            "targetStore": "Portfolio",
            "importExpectation": "ReconcilePublicSecurityPositions",
        },
        {
            "payloadId": "source-broker-positions",
            "provider": "Fixture Securities Broker",
            "workspace": "Portfolio",
            "payloadKind": "BrokerPositionFile",
            "format": "csv",
            "relativePath": "source-payloads/broker-positions-2026-06-04.csv",
            "recordCount": 1,
            "targetStore": "Portfolio",
            "importExpectation": "ReconcileListedEquityPosition",
        },
        {
            "payloadId": "source-pricing-snapshot",
            "provider": "Fixture Pricing Provider",
            "workspace": "Data",
            "payloadKind": "PricingSnapshot",
            "format": "csv",
            "relativePath": "source-payloads/pricing-snapshot-2026-06-04.csv",
            "recordCount": 8,
            "targetStore": "MarketData",
            "importExpectation": "RefreshPublicSecurityValuations",
        },
        {
            "payloadId": "source-bank-statement",
            "provider": "Fixture Bank",
            "workspace": "Accounting",
            "payloadKind": "BankStatement",
            "format": "csv",
            "relativePath": "source-payloads/bank-statement-2026-06-04.csv",
            "recordCount": 3,
            "targetStore": "CashActivity",
            "importExpectation": "ReconcileOperatingCash",
        },
        {
            "payloadId": "source-corporate-actions-and-loan-events",
            "provider": "Fixture Operations Event Feed",
            "workspace": "Portfolio",
            "payloadKind": "LifecycleEventFeed",
            "format": "json",
            "relativePath": "source-payloads/corporate-actions-and-loan-events-2026-06-04.json",
            "recordCount": 10,
            "targetStore": "Portfolio",
            "importExpectation": "ApplyAssetLifecycleEvents",
        },
    ]


def build_asset_lifecycle_events(personal_entries: list[dict[str, object]], other_entries: list[dict[str, object]]) -> dict[str, object]:
    late_personal = [
        entry for entry in personal_entries
        if entry["assetSpecificTerms"]["delinquencyStatus"] == "Late"
    ]
    corporate_actions = [
        {
            "eventId": "ca-indi-issuer-event",
            "eventType": "IssuerEvent",
            "securityId": other_entries[7]["securityId"],
            "effectiveDate": AS_OF,
            "announcementDate": "2026-06-03",
            "status": "PendingReview",
            "portfolioImpact": "ReviewEquityValuationAndDisclosure",
            "sourcePayloadId": "source-corporate-actions-and-loan-events",
        },
        {
            "eventId": "ca-corp-hy-rating-downgrade",
            "eventType": "CreditRatingChange",
            "securityId": other_entries[6]["securityId"],
            "effectiveDate": AS_OF,
            "previousRating": "BB",
            "newRating": "BB-",
            "status": "AppliedToWatchlist",
            "portfolioImpact": "CreditReviewRequired",
            "sourcePayloadId": "source-corporate-actions-and-loan-events",
        },
        {
            "eventId": "ca-mbs-factor-update",
            "eventType": "StructuredCreditFactorUpdate",
            "securityId": other_entries[0]["securityId"],
            "effectiveDate": AS_OF,
            "previousFactor": 0.98125,
            "newFactor": 0.974125,
            "status": "ReadyForReconciliation",
            "portfolioImpact": "PrincipalPaydownAndMarketValueRefresh",
            "sourcePayloadId": "source-corporate-actions-and-loan-events",
        },
        {
            "eventId": "ca-treasury-coupon-accrual",
            "eventType": "FixedIncomeCouponAccrual",
            "securityId": other_entries[3]["securityId"],
            "effectiveDate": AS_OF,
            "accruedInterest": 1_180,
            "status": "ReadyForLedger",
            "portfolioImpact": "AccrueInterestIncome",
            "sourcePayloadId": "source-corporate-actions-and-loan-events",
        },
    ]
    loan_events = [
        {
            "eventId": f"loan-delinquency-{index:03d}",
            "eventType": "PaymentPastDue",
            "securityId": entry["securityId"],
            "effectiveDate": AS_OF,
            "daysPastDue": entry["assetSpecificTerms"]["daysPastDue"],
            "status": "ExceptionOpened",
            "accountingImpact": "NoChargeOff",
            "sourcePayloadId": "source-corporate-actions-and-loan-events",
        }
        for index, entry in enumerate(late_personal, start=1)
    ] + [
        {
            "eventId": "loan-term-500k-rate-review",
            "eventType": "RateReview",
            "securityId": other_entries[8]["securityId"],
            "effectiveDate": AS_OF,
            "currentCoupon": other_entries[8]["assetSpecificTerms"]["coupon"],
            "status": "NoChange",
            "accountingImpact": "ContinueCurrentAccrual",
            "sourcePayloadId": "source-corporate-actions-and-loan-events",
        },
        {
            "eventId": "loan-loc-unused-fee-accrual",
            "eventType": "UnusedCommitmentFeeAccrual",
            "securityId": other_entries[9]["securityId"],
            "effectiveDate": AS_OF,
            "unfundedCommitment": other_entries[9]["assetSpecificTerms"]["unfundedCommitment"],
            "feeRate": other_entries[9]["assetSpecificTerms"]["unusedCommitmentFeeRate"],
            "status": "ReadyForLedger",
            "accountingImpact": "AccrueCommitmentFee",
            "sourcePayloadId": "source-corporate-actions-and-loan-events",
        },
        {
            "eventId": "loan-personal-payment-batch-applied",
            "eventType": "PaymentBatchApplied",
            "securityIds": [entry["securityId"] for entry in personal_entries],
            "effectiveDate": AS_OF,
            "loanCount": len(personal_entries),
            "principalApplied": 37_500,
            "interestApplied": 8_950,
            "status": "Applied",
            "accountingImpact": "CashAndInterestIncome",
            "sourcePayloadId": "source-corporate-actions-and-loan-events",
        },
    ]
    return {
        "asOf": AS_OF,
        "corporateActions": corporate_actions,
        "loanEvents": loan_events,
        "expectedAccountingActions": [
            {"actionId": "acct-accrue-treasury-interest", "eventId": "ca-treasury-coupon-accrual", "ledgerAccountCode": "4000-INTEREST-INCOME", "status": "Ready"},
            {"actionId": "acct-accrue-loc-unused-fee", "eventId": "loan-loc-unused-fee-accrual", "ledgerAccountCode": "4000-INTEREST-INCOME", "status": "Ready"},
            {"actionId": "acct-apply-personal-loan-payment-batch", "eventId": "loan-personal-payment-batch-applied", "ledgerAccountCode": "1205-PERSONAL-LOANS", "status": "Applied"},
        ],
    }


def build_journal_entries(entries: list[dict[str, object]], positions: list[dict[str, object]]) -> list[dict[str, object]]:
    entry_by_id = {entry["securityId"]: entry for entry in entries}
    journals = []
    for index, position in enumerate(positions, start=1):
        security = entry_by_id[position["securityId"]]
        account_code = security["assetSpecificTerms"]["ledgerAccountCode"]
        amount = position["marketValue"] or position["quantity"]
        journals.append(
            {
                "journalEntryId": f"je-opening-position-{index:03d}",
                "journalDate": AS_OF,
                "source": "Fixture Opening Position Load",
                "status": "Posted",
                "securityId": position["securityId"],
                "positionId": position["positionId"],
                "lines": [
                    {"ledgerAccountCode": account_code, "debit": amount, "credit": 0, "currency": "USD"},
                    {"ledgerAccountCode": "3000-OPENING-CAPITAL", "debit": 0, "credit": amount, "currency": "USD"},
                ],
            }
        )
    journals.append(
        {
            "journalEntryId": "je-undrawn-loc-memo",
            "journalDate": AS_OF,
            "source": "Fixture Credit Agreement",
            "status": "Posted",
            "securityId": "77777777-7777-4777-8777-000000000001",
            "positionId": "pos-undrawn-loc",
            "lines": [
                {"ledgerAccountCode": "9990-MEMO-OFFSET", "debit": 250_000, "credit": 0, "currency": "USD"},
                {"ledgerAccountCode": "2100-UNFUNDED-COMMITMENTS", "debit": 0, "credit": 250_000, "currency": "USD"},
            ],
        }
    )
    return journals


def build_operator_access() -> dict[str, object]:
    return {
        "users": [
            {"userId": "fixture-operator", "displayName": "Fixture Operations Analyst", "roles": ["DataOperator", "LoanServicingOperator"]},
            {"userId": "fixture-controller", "displayName": "Fixture Fund Controller", "roles": ["FundController", "ReportApprover"]},
            {"userId": "fixture-pm", "displayName": "Fixture Portfolio Manager", "roles": ["PortfolioManager", "ReadOnlyAccounting"]},
            {"userId": "fixture-auditor", "displayName": "Fixture Internal Auditor", "roles": ["AuditReviewer"]},
        ],
        "rolePermissions": [
            {"role": "DataOperator", "permissions": ["SecurityMaster.Read", "SecurityMaster.Write", "Evidence.Attach"]},
            {"role": "LoanServicingOperator", "permissions": ["DirectLending.Read", "DirectLending.Service", "CashActivity.Apply"]},
            {"role": "FundController", "permissions": ["Ledger.Read", "Ledger.Post", "Reconciliation.Resolve"]},
            {"role": "ReportApprover", "permissions": ["Reporting.Read", "Reporting.Approve"]},
            {"role": "PortfolioManager", "permissions": ["Portfolio.Read", "Risk.Read", "Reporting.Read"]},
            {"role": "ReadOnlyAccounting", "permissions": ["Ledger.Read", "Reconciliation.Read"]},
            {"role": "AuditReviewer", "permissions": ["Audit.Read", "Evidence.Read", "AccessControl.Read"]},
        ],
        "permissionScenarios": [
            {
                "scenarioId": "persona-data-operator-imports-source-files",
                "userId": "fixture-operator",
                "workspace": "Data",
                "action": "ImportProviderSourcePayload",
                "resourceId": "source-servicer-personal-loans",
                "expectedDecision": "Allow",
                "auditExpectation": "RecordImportAttempt",
            },
            {
                "scenarioId": "persona-data-operator-cannot-approve-report",
                "userId": "fixture-operator",
                "workspace": "Reporting",
                "action": "ApproveReportPack",
                "resourceId": "report-pack-mixed-credit-ops",
                "expectedDecision": "Deny",
                "auditExpectation": "RecordDeniedCommand",
            },
            {
                "scenarioId": "persona-controller-posts-ledger",
                "userId": "fixture-controller",
                "workspace": "Accounting",
                "action": "PostJournalEntry",
                "resourceId": "je-undrawn-loc-memo",
                "expectedDecision": "Allow",
                "auditExpectation": "RecordPostedJournalApproval",
            },
            {
                "scenarioId": "persona-pm-reviews-risk-but-cannot-post",
                "userId": "fixture-pm",
                "workspace": "Portfolio",
                "action": "PostJournalEntry",
                "resourceId": "je-undrawn-loc-memo",
                "expectedDecision": "Deny",
                "auditExpectation": "RecordDeniedCommand",
            },
            {
                "scenarioId": "persona-auditor-reads-evidence-only",
                "userId": "fixture-auditor",
                "workspace": "Settings",
                "action": "ReadAuditTrail",
                "resourceId": "ops-fixture-mixed-credit-2026-06-04",
                "expectedDecision": "Allow",
                "auditExpectation": "RecordReadOnlyAuditAccess",
            },
        ],
    }


def build_reporting(entries: list[dict[str, object]]) -> dict[str, object]:
    return {
        "reportPackId": "report-pack-mixed-credit-ops",
        "asOf": AS_OF,
        "status": "Draft",
        "requiredSections": [
            {"sectionId": "loan-tape", "workspace": "Reporting", "recordCount": 52, "sourceStore": "DirectLending"},
            {"sectionId": "holdings", "workspace": "Portfolio", "recordCount": len(entries), "sourceStore": "Portfolio"},
            {"sectionId": "cash", "workspace": "Accounting", "recordCount": 1, "sourceStore": "Ledger"},
            {"sectionId": "reconciliation", "workspace": "Accounting", "recordCount": 4, "sourceStore": "Evidence"},
            {"sectionId": "exceptions", "workspace": "Data", "recordCount": 4, "sourceStore": "Evidence"},
            {"sectionId": "evidence", "workspace": "Reporting", "recordCount": 7, "sourceStore": "Evidence"},
        ],
        "recipients": [
            {"recipientId": "fixture-controller", "delivery": "InternalReview", "requiresApproval": True},
            {"recipientId": "fixture-pm", "delivery": "InternalReview", "requiresApproval": False},
        ],
    }


def build_servicing_calendar(contracts: list[dict[str, object]]) -> list[dict[str, object]]:
    calendar = []
    for index, contract in enumerate(contracts, start=1):
        drawn = contract["servicing"]["totalDrawn"]
        next_payment = round(drawn * 0.0125, 2) if drawn else 0
        calendar.append(
            {
                "servicingItemId": f"servicing-calendar-{index:03d}",
                "loanId": contract["loanId"],
                "securityId": contract["currentTerms"]["securityMasterReference"]["securityId"],
                "borrowerId": contract["borrower"]["borrowerId"],
                "dueDate": "2026-07-15" if drawn else "2026-07-01",
                "itemType": "PaymentDue" if drawn else "AvailabilityReview",
                "expectedPrincipal": round(next_payment * 0.72, 2),
                "expectedInterest": round(next_payment * 0.28, 2),
                "status": "Scheduled",
            }
        )
    return calendar


def build_settlement_instructions() -> list[dict[str, object]]:
    return [
        {
            "instructionId": "ssi-custody-fixed-income",
            "accountId": CUSTODY_ACCOUNT_ID,
            "partyId": "eeeeeeee-eeee-4eee-8eee-000000000003",
            "assetClasses": ["FixedIncome", "CustomAsset"],
            "cashAccountId": BANK_ACCOUNT_ID,
            "settlementMethod": "DVP",
            "status": "Approved",
        },
        {
            "instructionId": "ssi-broker-equity",
            "accountId": BROKERAGE_ACCOUNT_ID,
            "partyId": "eeeeeeee-eeee-4eee-8eee-000000000004",
            "assetClasses": ["Equity"],
            "cashAccountId": BANK_ACCOUNT_ID,
            "settlementMethod": "BrokerNetSettlement",
            "status": "Approved",
        },
        {
            "instructionId": "ssi-loan-servicing",
            "accountId": LOAN_SERVICING_ACCOUNT_ID,
            "partyId": "eeeeeeee-eeee-4eee-8eee-000000000001",
            "assetClasses": ["DirectLoan"],
            "cashAccountId": BANK_ACCOUNT_ID,
            "settlementMethod": "WireAndAch",
            "status": "Approved",
        },
    ]


def build_risk_and_compliance(personal_entries: list[dict[str, object]], other_entries: list[dict[str, object]], positions: list[dict[str, object]]) -> dict[str, object]:
    exposure_by_security = {position["securityId"]: position["marketValue"] for position in positions}
    public_credit_ids = [entry["securityId"] for entry in other_entries[:7]]
    direct_loan_ids = [entry["securityId"] for entry in personal_entries + other_entries[8:10]]
    late_personal_ids = [
        entry["securityId"]
        for entry in personal_entries
        if entry["assetSpecificTerms"]["delinquencyStatus"] == "Late"
    ]

    return {
        "policySetId": "risk-policy-mixed-credit-fixture",
        "exposureSummary": {
            "directLoanExposure": sum(exposure_by_security[security_id] for security_id in direct_loan_ids),
            "publicSecurityExposure": sum(exposure_by_security[security_id] for security_id in public_credit_ids + [other_entries[7]["securityId"]]),
            "latePersonalLoanCount": len(late_personal_ids),
            "undrawnCommitmentExposure": 250_000,
        },
        "limits": [
            {"limitId": "limit-personal-loan-max-single", "scope": "Security", "metric": "PrincipalOutstanding", "threshold": 25_000, "status": "Pass"},
            {"limitId": "limit-structured-credit-exposure", "scope": "Sleeve", "metric": "MarketValue", "threshold": 1_000_000, "status": "Pass"},
            {"limitId": "limit-high-yield-review", "scope": "Security", "metric": "CreditRating", "threshold": "BelowInvestmentGrade", "status": "ReviewRequired", "securityId": other_entries[6]["securityId"]},
            {"limitId": "limit-undrawn-commitment", "scope": "Facility", "metric": "UnfundedCommitment", "threshold": 500_000, "status": "Pass", "securityId": other_entries[9]["securityId"]},
        ],
        "watchlist": [
            {"watchlistId": f"watch-personal-loan-{index:03d}", "securityId": security_id, "reason": "PaymentPastDue", "severity": "Medium"}
            for index, security_id in enumerate(late_personal_ids, start=1)
        ] + [
            {"watchlistId": "watch-corp-hy", "securityId": other_entries[6]["securityId"], "reason": "BelowInvestmentGradeCredit", "severity": "High"},
            {"watchlistId": "watch-non-agency-mbs", "securityId": other_entries[1]["securityId"], "reason": "PrivateTrustValuationReview", "severity": "Medium"},
        ],
        "stressScenarios": [
            {"scenarioId": "stress-rates-up-100bps", "description": "Rates up 100 bps", "estimatedPnl": -18_750, "affectedSecurityIds": public_credit_ids},
            {"scenarioId": "stress-consumer-credit-deterioration", "description": "Personal loan default stress", "estimatedPnl": -42_500, "affectedSecurityIds": late_personal_ids},
            {"scenarioId": "stress-indi-minus-20pct", "description": "INDI equity down 20 pct", "estimatedPnl": -15_600, "affectedSecurityIds": [other_entries[7]["securityId"]]},
        ],
        "complianceChecks": [
            {"checkId": "compliance-kyb-servicer", "scope": "Party", "subjectId": "eeeeeeee-eeee-4eee-8eee-000000000001", "status": "Pass"},
            {"checkId": "compliance-synthetic-borrower-pii", "scope": "Borrowers", "subjectId": LOAN_SERVICING_ACCOUNT_ID, "status": "Pass"},
            {"checkId": "compliance-investment-guidelines", "scope": "Portfolio", "subjectId": PORTFOLIO_ID, "status": "ReviewRequired"},
        ],
    }


def build_exception_queue(personal_entries: list[dict[str, object]], other_entries: list[dict[str, object]]) -> list[dict[str, object]]:
    late_personal = [
        entry for entry in personal_entries
        if entry["assetSpecificTerms"]["delinquencyStatus"] == "Late"
    ]
    return [
        {
            "exceptionId": f"ex-late-personal-loan-{index:03d}",
            "category": "LoanServicing",
            "severity": "Medium",
            "status": "Open",
            "securityId": entry["securityId"],
            "assignedRole": "LoanServicingOperator",
            "summary": "Synthetic personal loan is past due and needs collection status review.",
        }
        for index, entry in enumerate(late_personal, start=1)
    ] + [
        {
            "exceptionId": "ex-non-agency-mbs-valuation",
            "category": "Valuation",
            "severity": "High",
            "status": "Open",
            "securityId": other_entries[1]["securityId"],
            "assignedRole": "FundController",
            "summary": "Non-agency mortgage security requires valuation approval evidence.",
        },
        {
            "exceptionId": "ex-high-yield-credit-review",
            "category": "CreditRisk",
            "severity": "High",
            "status": "Open",
            "securityId": other_entries[6]["securityId"],
            "assignedRole": "PortfolioManager",
            "summary": "High-yield corporate bond requires portfolio manager credit review.",
        },
        {
            "exceptionId": "ex-report-pack-draft",
            "category": "Reporting",
            "severity": "Low",
            "status": "Open",
            "securityId": other_entries[7]["securityId"],
            "assignedRole": "ReportApprover",
            "summary": "Draft report pack requires approval before release.",
        },
    ]


def build_data_quality_controls(entries: list[dict[str, object]], positions: list[dict[str, object]]) -> list[dict[str, object]]:
    return [
        {
            "controlId": "dq-security-master-position-coverage",
            "controlType": "ReferentialIntegrity",
            "status": "Pass",
            "checkedRecordCount": len(positions),
            "exceptionCount": 0,
            "description": "Every operational position resolves to a Security Master entry.",
        },
        {
            "controlId": "dq-ledger-mapping-coverage",
            "controlType": "Completeness",
            "status": "Pass",
            "checkedRecordCount": len(entries),
            "exceptionCount": 0,
            "description": "Every Security Master entry has an operational ledger account code.",
        },
        {
            "controlId": "dq-market-data-coverage",
            "controlType": "Completeness",
            "status": "ReviewRequired",
            "checkedRecordCount": 8,
            "exceptionCount": 0,
            "description": "Public securities have price evidence; direct loans are valued through servicing balances.",
        },
        {
            "controlId": "dq-delinquency-review",
            "controlType": "BusinessRule",
            "status": "ReviewRequired",
            "checkedRecordCount": 50,
            "exceptionCount": 3,
            "description": "Late personal loans are expected fixture exceptions for servicing workflow tests.",
        },
    ]


def build_audit_trail() -> list[dict[str, object]]:
    return [
        {"auditId": "audit-load-security-master", "timestamp": "2026-06-04T08:00:00Z", "actor": "fixture-operator", "action": "ImportSecurityMaster", "subjectId": "ops-fixture-mixed-credit-2026-06-04"},
        {"auditId": "audit-map-ledger", "timestamp": "2026-06-04T08:05:00Z", "actor": "fixture-controller", "action": "ApproveLedgerMappings", "subjectId": LEDGER_BOOK_ID},
        {"auditId": "audit-load-servicing", "timestamp": "2026-06-04T08:10:00Z", "actor": "fixture-operator", "action": "ImportServicingTape", "subjectId": LOAN_SERVICING_ACCOUNT_ID},
        {"auditId": "audit-load-pricing", "timestamp": "2026-06-04T08:15:00Z", "actor": "fixture-operator", "action": "ImportMarketData", "subjectId": PORTFOLIO_ID},
        {"auditId": "audit-open-exceptions", "timestamp": "2026-06-04T08:20:00Z", "actor": "fixture-controller", "action": "OpenExceptionQueue", "subjectId": PORTFOLIO_ID},
        {"auditId": "audit-create-report-pack", "timestamp": "2026-06-04T08:30:00Z", "actor": "fixture-controller", "action": "CreateReportPack", "subjectId": "report-pack-mixed-credit-ops"},
    ]


def csv_line(values: list[object]) -> str:
    escaped = []
    for value in values:
        text = str(value)
        if "," in text or "\"" in text:
            text = "\"" + text.replace("\"", "\"\"") + "\""
        escaped.append(text)
    return ",".join(escaped)


def write_csv(path: Path, headers: list[str], rows: list[list[object]]) -> None:
    path.write_text(
        "\n".join([csv_line(headers), *[csv_line(row) for row in rows]]) + "\n",
        encoding="utf-8",
    )


def write_source_payload_files(fixture: dict[str, object]) -> None:
    SOURCE_DIR.mkdir(parents=True, exist_ok=True)
    entries = fixture["securityMasterEntries"]
    personal_entries = [
        entry for entry in entries
        if entry["classification"]["subType"] == "PersonalLoan"
    ]
    other_entries = [
        entry for entry in entries
        if entry["classification"]["subType"] != "PersonalLoan"
    ]
    positions_by_security = {
        position["securityId"]: position
        for position in fixture["operationalInstance"]["portfolio"]["positions"]
    }
    public_entries = other_entries[:7]
    equity_entry = other_entries[7]

    write_csv(
        SOURCE_DIR / "servicer-personal-loans-2026-06-04.csv",
        ["loan_number", "security_id", "borrower_reference", "outstanding_principal", "coupon", "payment_frequency", "days_past_due", "delinquency_status"],
        [
            [
                entry["classification"]["primaryIdentifierValue"],
                entry["securityId"],
                entry["assetSpecificTerms"]["borrowerReference"],
                entry["assetSpecificTerms"]["outstandingPrincipal"],
                entry["assetSpecificTerms"]["coupon"],
                entry["assetSpecificTerms"]["paymentFrequency"],
                entry["assetSpecificTerms"]["daysPastDue"],
                entry["assetSpecificTerms"]["delinquencyStatus"],
            ]
            for entry in personal_entries
        ],
    )
    write_csv(
        SOURCE_DIR / "custodian-holdings-2026-06-04.csv",
        ["account_id", "security_id", "identifier_kind", "identifier_value", "quantity", "quantity_units", "market_value"],
        [
            [
                positions_by_security[entry["securityId"]]["accountId"],
                entry["securityId"],
                entry["classification"]["primaryIdentifierKind"],
                entry["classification"]["primaryIdentifierValue"],
                positions_by_security[entry["securityId"]]["quantity"],
                positions_by_security[entry["securityId"]]["quantityUnits"],
                positions_by_security[entry["securityId"]]["marketValue"],
            ]
            for entry in public_entries
        ],
    )
    write_csv(
        SOURCE_DIR / "broker-positions-2026-06-04.csv",
        ["account_id", "symbol", "security_id", "quantity", "cost_basis", "market_value"],
        [[BROKERAGE_ACCOUNT_ID, "INDI", equity_entry["securityId"], 10_000, 65_000, 78_000]],
    )
    write_csv(
        SOURCE_DIR / "pricing-snapshot-2026-06-04.csv",
        ["security_id", "price_type", "market_value", "currency", "source"],
        [
            [
                snapshot["securityId"],
                snapshot["priceType"],
                snapshot["marketValue"],
                snapshot["currency"],
                snapshot["source"],
            ]
            for snapshot in fixture["operationalInstance"]["marketData"]["priceSnapshots"]
        ],
    )
    write_csv(
        SOURCE_DIR / "bank-statement-2026-06-04.csv",
        ["account_id", "transaction_date", "description", "amount", "currency", "external_reference"],
        [
            [BANK_ACCOUNT_ID, AS_OF, "Loan payment batch sweep", 46_450, "USD", "bank-fixture-payments-001"],
            [BANK_ACCOUNT_ID, AS_OF, "Term loan funding wire", -500_000, "USD", "bank-fixture-wire-500k"],
            [BANK_ACCOUNT_ID, AS_OF, "Structured credit remittance", 8_665, "USD", "bank-fixture-mbs-remit-001"],
        ],
    )
    lifecycle = fixture["operationalInstance"]["assetLifecycleEvents"]
    (SOURCE_DIR / "corporate-actions-and-loan-events-2026-06-04.json").write_text(
        json.dumps(
            {
                "asOf": AS_OF,
                "events": lifecycle["corporateActions"] + lifecycle["loanEvents"],
            },
            indent=2,
        ) + "\n",
        encoding="utf-8",
    )


def build_fixture() -> dict[str, object]:
    personal_entries = build_personal_loan_entries()
    other_entries = build_other_security_entries()
    entries = personal_entries + other_entries
    positions = build_positions(personal_entries, other_entries)
    contracts = build_loan_contracts(personal_entries, other_entries)
    market_data = build_market_data(other_entries)
    cash_and_activity = build_cash_and_activity(personal_entries, other_entries, contracts)
    journal_entries = build_journal_entries(entries, positions)
    reporting = build_reporting(entries)
    servicing_calendar = build_servicing_calendar(contracts)
    risk_and_compliance = build_risk_and_compliance(personal_entries, other_entries, positions)
    exception_queue = build_exception_queue(personal_entries, other_entries)
    data_quality_controls = build_data_quality_controls(entries, positions)
    audit_trail = build_audit_trail()
    source_payload_manifest = build_source_payload_manifest(personal_entries, other_entries)
    asset_lifecycle_events = build_asset_lifecycle_events(personal_entries, other_entries)
    security_ids = {entry["securityId"] for entry in entries}

    fixture = {
        "schema": "meridian.test-portfolio-status-set",
        "schemaVersion": "1.5.0",
        "scenarioId": "mixed-credit-personal-loans-fixed-income-equity-credit-line",
        "name": "Mixed credit and public securities operational status set",
        "purpose": "Operational-instance seed data for checking whether portfolio, Security Master, direct-lending, fixed-income, equity, credit-facility, accounting, reconciliation, and reporting functionality is present.",
        "asOf": AS_OF,
        "baseCurrency": "USD",
        "assumptions": [
            "The 50 personal loans are individual DirectLoan Security Master entries, not a pool.",
            "Borrowers are synthetic fixture parties so tests can exercise operational relationships without carrying borrower PII.",
            "The undrawn line of credit uses a 250000 commitment amount because the request did not specify the commitment size.",
            "Mortgage packs are represented as mortgage-backed security records with retained servicer, trustee, factor, and valuation evidence requirements.",
        ],
        "portfolioSummary": {
            "personalLoanCount": 50,
            "mortgageSecurityPackCount": 3,
            "treasurySecurityCount": 2,
            "corporateBondCount": 2,
            "equityPositionCount": 1,
            "fundedStandaloneLoanCount": 1,
            "undrawnLineOfCreditCount": 1,
            "securityMasterEntryCount": len(entries),
            "positionCount": len(positions),
            "borrowerPartyCount": 52,
            "accountCount": 4,
            "requiresPrivateCreditSupport": True,
            "requiresStructuredCreditSupport": True,
            "requiresFixedIncomeSupport": True,
            "requiresEquitySupport": True,
            "requiresCommitmentAccounting": True,
        },
        "operationalInstance": {
            "instanceId": "ops-fixture-mixed-credit-2026-06-04",
            "storageModel": {
                "mode": "LocalFirstOperationalSeed",
                "dataRoot": "tests/fixtures/portfolio/mixed-credit-status-set.json",
                "stateStores": ["FundStructure", "SecurityMaster", "DirectLending", "Portfolio", "Ledger", "MarketData", "CashActivity", "Risk", "Compliance", "Settlement", "SourcePayloads", "AssetLifecycle", "Evidence", "AccessControl", "Reporting", "Audit"],
            },
            "operatingContext": {
                "organizationId": ORG_ID,
                "businessId": BUSINESS_ID,
                "fundId": FUND_ID,
                "vehicleId": VEHICLE_ID,
                "investmentPortfolioId": PORTFOLIO_ID,
                "ledgerBookId": LEDGER_BOOK_ID,
                "asOf": AS_OF,
                "baseCurrency": "USD",
            },
            "fundStructure": build_fund_structure(),
            "parties": party_records(personal_entries),
            "directLending": {
                "loanContracts": contracts,
                "servicingAccountId": LOAN_SERVICING_ACCOUNT_ID,
                "servicingCalendar": servicing_calendar,
            },
            "portfolio": {
                "portfolioId": PORTFOLIO_ID,
                "name": "Mixed Credit Operational Portfolio",
                "positions": positions,
            },
            "sourcePayloads": source_payload_manifest,
            "marketData": market_data,
            "cashAndActivity": cash_and_activity,
            "assetLifecycleEvents": asset_lifecycle_events,
            "settlementInstructions": build_settlement_instructions(),
            "riskAndCompliance": risk_and_compliance,
            "ledger": {
                "ledgerBook": {"ledgerBookId": LEDGER_BOOK_ID, "fundId": FUND_ID, "portfolioId": PORTFOLIO_ID, "baseCurrency": "USD", "status": "Open"},
                "accounts": [
                    {"accountCode": "1000-CASH", "name": "Operating cash", "normalBalance": "Debit", "accountType": "Asset"},
                    {"accountCode": "1200-TERM-LOANS", "name": "Term loans receivable", "normalBalance": "Debit", "accountType": "Asset"},
                    {"accountCode": "1205-PERSONAL-LOANS", "name": "Personal loans receivable", "normalBalance": "Debit", "accountType": "Asset"},
                    {"accountCode": "1210-MBS", "name": "Mortgage-backed securities", "normalBalance": "Debit", "accountType": "Asset"},
                    {"accountCode": "1220-TREASURIES", "name": "Treasury securities", "normalBalance": "Debit", "accountType": "Asset"},
                    {"accountCode": "1230-CORPORATES", "name": "Corporate bonds", "normalBalance": "Debit", "accountType": "Asset"},
                    {"accountCode": "1240-EQUITIES", "name": "Equity investments", "normalBalance": "Debit", "accountType": "Asset"},
                    {"accountCode": "2100-UNFUNDED-COMMITMENTS", "name": "Unfunded commitments memorandum", "normalBalance": "Credit", "accountType": "MemoLiability"},
                    {"accountCode": "3000-OPENING-CAPITAL", "name": "Opening capital", "normalBalance": "Credit", "accountType": "Equity"},
                    {"accountCode": "4000-INTEREST-INCOME", "name": "Interest income", "normalBalance": "Credit", "accountType": "Income"},
                    {"accountCode": "9990-MEMO-OFFSET", "name": "Memorandum offset", "normalBalance": "Debit", "accountType": "MemoOffset"},
                ],
                "mappingEvidence": [
                    {
                        "securityId": entry["securityId"],
                        "ledgerAccountCode": entry["assetSpecificTerms"].get("ledgerAccountCode"),
                        "approvalReference": "approval://fixture/mixed-credit/status-set",
                    }
                    for entry in entries
                ],
                "journalEntries": journal_entries,
            },
            "reconciliation": {
                "cases": [
                    {"caseId": "recon-personal-loans", "status": "Open", "category": "LoanScheduleEvidence", "accountId": LOAN_SERVICING_ACCOUNT_ID, "securityCount": 50},
                    {"caseId": "recon-mbs-factor", "status": "Open", "category": "FactorScheduleEvidence", "accountId": CUSTODY_ACCOUNT_ID, "securityCount": 3},
                    {"caseId": "recon-fixed-income-pricing", "status": "ReviewRequired", "category": "PriceEvidence", "accountId": CUSTODY_ACCOUNT_ID, "securityCount": 4},
                    {"caseId": "recon-indi-position", "status": "Ready", "category": "BrokerPositionEvidence", "accountId": BROKERAGE_ACCOUNT_ID, "securityCount": 1},
                ]
            },
            "exceptions": exception_queue,
            "dataQualityControls": data_quality_controls,
            "workflows": [
                {"workflowId": "wf-security-master-coverage", "workspace": "Data", "status": "ReviewRequired", "summary": "Security Master contains all 60 instrument records and needs evidence review for private and structured credit."},
                {"workflowId": "wf-direct-lending-servicing", "workspace": "Accounting", "status": "ReviewRequired", "summary": "52 direct-lending contracts require servicing, accrual, and cash application coverage."},
                {"workflowId": "wf-portfolio-status", "workspace": "Portfolio", "status": "ReviewRequired", "summary": "60 positions are linked to accounts, portfolio, ledger book, and Security Master records."},
                {"workflowId": "wf-report-pack", "workspace": "Reporting", "status": "ReviewRequired", "summary": "Report pack must include loan tape, commitment schedule, fixed-income holdings, INDI lot, and reconciliation evidence."},
            ],
            "operatorAccess": build_operator_access(),
            "reporting": reporting,
            "auditTrail": audit_trail,
            "evidenceRecords": [
                {"evidenceId": "evidence-security-master-import", "kind": "SecurityMasterImport", "subjectId": "ops-fixture-mixed-credit-2026-06-04", "status": "Ready", "recordCount": len(entries)},
                {"evidenceId": "evidence-loan-contracts", "kind": "DirectLendingContracts", "subjectId": LOAN_SERVICING_ACCOUNT_ID, "status": "ReviewRequired", "recordCount": len(contracts)},
                {"evidenceId": "evidence-portfolio-positions", "kind": "PortfolioPositions", "subjectId": PORTFOLIO_ID, "status": "Ready", "recordCount": len(positions)},
                {"evidenceId": "evidence-ledger-mapping", "kind": "LedgerMapping", "subjectId": LEDGER_BOOK_ID, "status": "Ready", "recordCount": len(entries)},
                {"evidenceId": "evidence-market-data", "kind": "MarketDataSnapshot", "subjectId": PORTFOLIO_ID, "status": "Ready", "recordCount": len(market_data["priceSnapshots"])},
                {"evidenceId": "evidence-cash-activity", "kind": "CashAndActivity", "subjectId": BANK_ACCOUNT_ID, "status": "Ready", "recordCount": len(cash_and_activity["servicingEvents"]) + len(cash_and_activity["incomeEvents"])},
                {"evidenceId": "evidence-report-pack", "kind": "ReportPack", "subjectId": reporting["reportPackId"], "status": "Draft", "recordCount": len(reporting["requiredSections"])},
                {"evidenceId": "evidence-risk-compliance", "kind": "RiskComplianceControls", "subjectId": PORTFOLIO_ID, "status": "ReviewRequired", "recordCount": len(risk_and_compliance["limits"]) + len(risk_and_compliance["complianceChecks"])},
                {"evidenceId": "evidence-exception-queue", "kind": "ExceptionQueue", "subjectId": PORTFOLIO_ID, "status": "Open", "recordCount": len(exception_queue)},
                {"evidenceId": "evidence-audit-trail", "kind": "AuditTrail", "subjectId": "ops-fixture-mixed-credit-2026-06-04", "status": "Ready", "recordCount": len(audit_trail)},
                {"evidenceId": "evidence-source-payloads", "kind": "ProviderSourcePayloads", "subjectId": "ops-fixture-mixed-credit-2026-06-04", "status": "Ready", "recordCount": len(source_payload_manifest)},
                {"evidenceId": "evidence-asset-lifecycle", "kind": "AssetLifecycleEvents", "subjectId": PORTFOLIO_ID, "status": "ReviewRequired", "recordCount": len(asset_lifecycle_events["corporateActions"]) + len(asset_lifecycle_events["loanEvents"])},
            ],
        },
        "securityMasterEntries": entries,
        "portfolioPositions": [
            {"positionId": "pos-personal-loans", "label": "50 individual personal loans", "securityIds": [entry["securityId"] for entry in personal_entries]},
            {"positionId": "pos-mbs", "label": "Mortgage security packs", "securityIds": [entry["securityId"] for entry in other_entries[:3]]},
            {"positionId": "pos-treasuries", "label": "Treasury securities", "securityIds": [entry["securityId"] for entry in other_entries[3:5]]},
            {"positionId": "pos-corporates", "label": "Corporate bonds", "securityIds": [entry["securityId"] for entry in other_entries[5:7]]},
            {"positionId": "pos-indi", "label": "INDI equity", "securityIds": [other_entries[7]["securityId"]]},
            {"positionId": "pos-funded-loan", "label": "Funded $500,000 loan", "securityIds": [other_entries[8]["securityId"]]},
            {"positionId": "pos-undrawn-loc", "label": "Undrawn line of credit", "securityIds": [other_entries[9]["securityId"]]},
        ],
        "capabilityExpectations": [
            {"capabilityId": "security-master-direct-loan-registration", "description": "Each personal loan is registered as its own DirectLoan Security Master entry.", "mustSupportSecurityIds": [personal_entries[0]["securityId"], personal_entries[-1]["securityId"]]},
            {"capabilityId": "private-credit-servicing", "description": "Create, service, accrue, reconcile, and report individual personal loans and standalone loans.", "mustSupportAssetClasses": ["DirectLoan"], "mustSupportSubTypes": ["PersonalLoan", "TermLoan"]},
            {"capabilityId": "structured-credit-servicing", "description": "Represent mortgage-backed packs with factor, servicer/trustee, paydown, valuation, and evidence support.", "mustSupportSubTypes": ["MortgageBackedSecurity", "CommercialMortgageBackedSecurity"]},
            {"capabilityId": "fixed-income-analytics", "description": "Represent Treasury and corporate bonds with accrued interest, maturity cash flows, price evidence, and credit evidence where applicable.", "mustSupportSubTypes": ["TreasuryNote", "TreasuryBill", "CorporateBond"]},
            {"capabilityId": "listed-equity-positioning", "description": "Represent INDI equity shares with quote, tax-lot, PnL, and corporate-action support.", "mustSupportSecurityIds": [other_entries[7]["securityId"]]},
            {"capabilityId": "undrawn-credit-line", "description": "Track an undrawn line of credit as a commitment with zero drawn principal and full unfunded availability.", "mustSupportSecurityIds": [other_entries[9]["securityId"]]},
            {"capabilityId": "operational-instance-storage", "description": "Store the scenario as a Meridian operational instance with fund structure, parties, accounts, positions, loans, ledger mapping, workflows, and evidence records.", "mustSupportOperationalStores": ["FundStructure", "SecurityMaster", "DirectLending", "Portfolio", "Ledger", "MarketData", "CashActivity", "Risk", "Compliance", "Settlement", "Evidence", "AccessControl", "Reporting", "Audit"]},
            {"capabilityId": "operational-readiness-controls", "description": "Exercise servicing calendars, settlement instructions, risk limits, compliance checks, exception queues, data-quality controls, and audit trail review.", "mustSupportOperationalStores": ["DirectLending", "Settlement", "Risk", "Compliance", "Evidence", "Audit"]},
            {"capabilityId": "provider-source-file-import", "description": "Import provider-style source payload files for servicer loans, custodian holdings, broker positions, pricing, bank activity, and lifecycle events.", "mustSupportOperationalStores": ["SourcePayloads", "DirectLending", "Portfolio", "MarketData", "CashActivity"]},
            {"capabilityId": "asset-lifecycle-event-processing", "description": "Apply corporate-action, issuer-event, factor-update, rating-change, loan-delinquency, payment, and commitment-fee events into portfolio and accounting workflows.", "mustSupportOperationalStores": ["AssetLifecycle", "Portfolio", "Accounting", "Evidence"]},
            {"capabilityId": "persona-permission-scenarios", "description": "Exercise allow and deny decisions for data operator, controller, portfolio manager, report approver, and auditor personas.", "mustSupportOperationalStores": ["AccessControl", "Audit"]},
        ],
    }

    assert len(entries) == 60
    assert len(personal_entries) == 50
    assert len(positions) == 60
    assert len(contracts) == 52
    assert len(journal_entries) == 61
    assert len(servicing_calendar) == 52
    assert len(exception_queue) == 6
    assert len(source_payload_manifest) == 6
    assert len(asset_lifecycle_events["corporateActions"]) == 4
    assert len(asset_lifecycle_events["loanEvents"]) == 6
    assert all(ref in security_ids for group in fixture["portfolioPositions"] for ref in group["securityIds"])
    return fixture


def main() -> None:
    fixture = build_fixture()
    OUT.write_text(json.dumps(fixture, indent=2) + "\n", encoding="utf-8")
    write_source_payload_files(fixture)


if __name__ == "__main__":
    main()
