using System;
using System.Collections.Generic;

namespace Meridian.Wpf.Models;

public enum OperationalExceptionSeverity
{
    Critical,
    High,
    Medium,
    Low
}

public enum OperationalExceptionStatus
{
    New,
    InReview,
    ActionRequired,
    WaitingForEvidence,
    Resolved
}

public sealed record OperationalExceptionAuditEntry(
    DateTimeOffset Timestamp,
    string Actor,
    string Action,
    string Detail);

public sealed record OperationalExceptionRecord(
    string Id,
    OperationalExceptionSeverity Severity,
    string SourceWorkspace,
    string SourceCategory,
    string AffectedAccount,
    string AffectedPortfolio,
    string AffectedInstrument,
    string AffectedReport,
    OperationalExceptionStatus Status,
    string RecommendedAction,
    string EvidenceLink,
    IReadOnlyList<OperationalExceptionAuditEntry> AuditHistory);
