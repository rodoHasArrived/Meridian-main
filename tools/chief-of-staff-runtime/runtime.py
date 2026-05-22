"""Meridian Chief of Staff runtime scaffold.

This module is intentionally lightweight and dependency-free. It models the ADK node pipeline
and can be wrapped by a real HTTP host in follow-up changes.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from datetime import datetime, timezone
from typing import Any
import uuid


@dataclass(slots=True)
class RuntimeRequest:
    session_id: str
    operator_request: str
    intent_kind: str
    workspace: str | None = None
    fund_profile_id: str | None = None
    fund_account_id: str | None = None
    context_tags: dict[str, str] = field(default_factory=dict)


@dataclass(slots=True)
class RuntimeResponse:
    intent_kind: str
    markdown_summary: str
    structured_payload: dict[str, Any]
    recommendations: list[dict[str, Any]]
    actions: list[dict[str, Any]]
    warnings: list[str]
    generated_at: str


class IntentAnalysisNode:
    def run(self, request: RuntimeRequest) -> str:
        text = request.operator_request.lower()
        if "reconcil" in text:
            return "AccountingReconciliationReview"
        if "readiness" in text or "paper" in text:
            return "TradingReadinessReview"
        if "report" in text:
            return "ReportPackApproval"
        return request.intent_kind or "GeneralOperatorAssistance"


class ContextAssemblyNode:
    def run(self, request: RuntimeRequest, intent_kind: str) -> dict[str, Any]:
        return {
            "sessionId": request.session_id,
            "intentKind": intent_kind,
            "workspace": request.workspace,
            "fundProfileId": request.fund_profile_id,
            "fundAccountId": request.fund_account_id,
            "contextTags": request.context_tags,
        }


class EvidenceAggregationNode:
    def run(self, context: dict[str, Any]) -> dict[str, Any]:
        return {
            "sources": ["mcp", "workstation-api", "evidence-endpoints"],
            "evidenceStatus": "ReviewRequired",
            "relatedWorkflowIds": [],
            "relatedReconciliationRunIds": [],
            "warnings": [],
            "context": context,
        }


class RecommendationSynthesisNode:
    def run(self, request: RuntimeRequest, intent_kind: str, evidence: dict[str, Any]) -> tuple[str, list[dict[str, Any]], list[dict[str, Any]]]:
        action_id = f"cos-action-{uuid.uuid4().hex[:12]}"
        action = {
            "actionId": action_id,
            "label": "Review recommended next step",
            "targetWorkflow": "accounting-reconciliation-review" if intent_kind == "AccountingReconciliationReview" else "paper-trading-readiness",
            "targetRoute": None,
            "requiredSignoffRole": "operator",
            "approvalRequired": True,
            "impactSummary": "Routes operator-approved action through existing Meridian governance services.",
            "evidencePrerequisites": [],
        }
        recommendation = {
            "recommendationId": f"cos-rec-{uuid.uuid4().hex[:12]}",
            "title": "Chief of Staff recommendation",
            "detail": "Review evidence and confirm whether to route this action.",
            "tone": "Warning",
            "actions": [action],
        }
        markdown = (
            f"## Chief of Staff Briefing\n\n"
            f"- Intent: **{intent_kind}**\n"
            f"- Request: {request.operator_request}\n"
            f"- Evidence posture: {evidence['evidenceStatus']}\n"
        )
        return markdown, [recommendation], [action]


class DecisionPreparationNode:
    def run(self, recommendations: list[dict[str, Any]]) -> dict[str, Any]:
        return {
            "pendingApproval": any(
                action.get("approvalRequired", False)
                for rec in recommendations
                for action in rec.get("actions", [])
            )
        }


class TraceEmissionNode:
    def run(self, request: RuntimeRequest, response: RuntimeResponse) -> dict[str, Any]:
        return {
            "traceId": f"cos-{request.session_id}",
            "capturedAt": response.generated_at,
            "runtimeName": "chief-of-staff-runtime",
            "runtimeVersion": "scaffold",
            "warnings": response.warnings,
        }


def execute(request: RuntimeRequest) -> RuntimeResponse:
    intent = IntentAnalysisNode().run(request)
    context = ContextAssemblyNode().run(request, intent)
    evidence = EvidenceAggregationNode().run(context)
    markdown, recommendations, actions = RecommendationSynthesisNode().run(request, intent, evidence)
    decision = DecisionPreparationNode().run(recommendations)
    generated_at = datetime.now(timezone.utc).isoformat()
    response = RuntimeResponse(
        intent_kind=intent,
        markdown_summary=markdown,
        structured_payload={
            "context": context,
            "evidence": evidence,
            "decisionPreparation": decision,
        },
        recommendations=recommendations,
        actions=actions,
        warnings=[],
        generated_at=generated_at,
    )
    _ = TraceEmissionNode().run(request, response)
    return response


def health() -> dict[str, Any]:
    return {
        "status": "Healthy",
        "detail": "Chief of Staff runtime scaffold is reachable.",
        "checkedAt": datetime.now(timezone.utc).isoformat(),
    }
