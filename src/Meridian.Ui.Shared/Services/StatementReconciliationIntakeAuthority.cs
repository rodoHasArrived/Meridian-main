using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.Domain.Reconciliation;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.PortfolioRecords.Accounts;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Contracts.Reconciliation;

namespace Meridian.Ui.Shared.Services;

public sealed record StatementReconciliationIntakeScopeRequest(
    string TenantId,
    string CompanyId,
    string FundAccountId,
    string ExternalAccountId,
    string SourceInstitution,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    StatementAccountingScope? RequestedScope = null)
{
    /// <summary>
    /// Allows an already-retained workflow to revalidate its immutable account, tenant, company,
    /// book, and period binding after close. New intake and any pre-publication resume must leave
    /// this false.
    /// </summary>
    public bool AllowClosedPeriodForRetainedWorkflow { get; init; }
}

public sealed record StatementReconciliationIntakeReceipt(
    StatementAccountingScope AccountingScope,
    Guid OperationsWorkflowId,
    int PublishedCaseCount,
    IReadOnlyList<string> EvidenceReferences);

public interface IStatementReconciliationIntakeAuthority
{
    Task<StatementAccountingScope> ResolveAccountingScopeAsync(
        StatementReconciliationIntakeScopeRequest request,
        CancellationToken ct = default);

    Task<StatementReconciliationIntakeReceipt> PublishAsync(
        string statementWorkflowId,
        StatementImportCommitResultDto import,
        StatementAccountingScope accountingScope,
        string tenantId,
        string companyId,
        string actor,
        string sourceInstitution,
        IReadOnlyList<string> evidenceReferences,
        CancellationToken ct = default);
}

public sealed class StatementReconciliationIntakeAuthorityException(
    string code,
    string message,
    Exception? innerException = null) : InvalidOperationException(message, innerException)
{
    public string Code { get; } = code;
}

/// <summary>
/// Connects retained statement intake to the existing Operations Continuity and governed casework
/// authorities. It creates no parallel lifecycle: posting, close, report certification, approval,
/// release, distribution, and receipt retention remain owned by their existing services.
/// </summary>
public sealed class StatementReconciliationIntakeAuthority : IStatementReconciliationIntakeAuthority
{
    private readonly IAccountQueryService? _accounts;
    private readonly IFundProfileTenancyRegistry? _tenancy;
    private readonly ILedgerBookService? _ledgerBooks;
    private readonly IOperationsContinuityWorkflowService? _operations;
    private readonly IStatementRunWorkflowService? _statementRuns;
    private readonly IReconciliationApiService? _reconciliation;
    private readonly IReconciliationBreakQueueRepository? _breakQueue;

    public StatementReconciliationIntakeAuthority(
        IAccountQueryService? accounts,
        IFundProfileTenancyRegistry? tenancy,
        ILedgerBookService? ledgerBooks,
        IOperationsContinuityWorkflowService? operations,
        IStatementRunWorkflowService? statementRuns,
        IReconciliationApiService? reconciliation,
        IReconciliationBreakQueueRepository? breakQueue)
    {
        _accounts = accounts;
        _tenancy = tenancy;
        _ledgerBooks = ledgerBooks;
        _operations = operations;
        _statementRuns = statementRuns;
        _reconciliation = reconciliation;
        _breakQueue = breakQueue;
    }

    public async Task<StatementAccountingScope> ResolveAccountingScopeAsync(
        StatementReconciliationIntakeScopeRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureConfigured();
        var tenantId = Require(request.TenantId, "STATEMENT_TENANT_REQUIRED");
        var companyId = Require(request.CompanyId, "STATEMENT_COMPANY_REQUIRED");
        if (!Guid.TryParse(request.FundAccountId, out var fundAccountId)
            || fundAccountId == Guid.Empty)
        {
            throw Failure(
                "STATEMENT_FUND_ACCOUNT_REQUIRED",
                "Statement-to-close intake requires a canonical fund-account id.");
        }

        var account = await _accounts!.GetAccountAsync(fundAccountId, ct).ConfigureAwait(false);
        if (account is null
            || !account.IsActive
            || !account.FundId.HasValue
            || account.FundId.Value == Guid.Empty
            || !SourceMatchesAccount(request, account))
        {
            throw Failure(
                "STATEMENT_ACCOUNT_NOT_AUTHORIZED",
                "The statement source is not bound to an active account and fund.");
        }

        var fundProfileId = account.FundId.Value.ToString("D");
        var owner = await _tenancy!.ResolveAsync(fundProfileId, ct).ConfigureAwait(false);
        if (owner is null
            || !owner.IsHeldBy(tenantId)
            || string.IsNullOrWhiteSpace(owner.CompanyId)
            || !string.Equals(owner.CompanyId.Trim(), companyId, StringComparison.OrdinalIgnoreCase))
        {
            throw Failure(
                "STATEMENT_FUND_NOT_AUTHORIZED",
                "The statement fund is not owned by the authenticated tenant and company.");
        }

        var requested = request.RequestedScope;
        if (requested is not null
            && !string.Equals(
                requested.FundProfileId?.Trim(),
                fundProfileId,
                StringComparison.OrdinalIgnoreCase))
        {
            throw Failure(
                "STATEMENT_FUND_SCOPE_MISMATCH",
                "The requested reporting fund profile does not own the statement account.");
        }

        var books = await _ledgerBooks!
            .ListBooksAsync(
                new LedgerBookQuery(
                    FundProfileId: fundProfileId,
                    AccountingBasis: AccountingBasisKindDto.Primary),
                ct)
            .ConfigureAwait(false);
        var matchingBooks = books
            .Where(book => requested is null || book.LedgerBookId == requested.LedgerBookId)
            .DistinctBy(static book => book.LedgerBookId)
            .ToArray();
        if (matchingBooks.Length != 1)
        {
            throw Failure(
                matchingBooks.Length == 0
                    ? "STATEMENT_LEDGER_BOOK_REQUIRED"
                    : "STATEMENT_LEDGER_BOOK_AMBIGUOUS",
                matchingBooks.Length == 0
                    ? "No primary ledger book matches the statement fund and requested scope."
                    : "More than one primary ledger book matches the statement fund; select an exact book.");
        }

        var book = matchingBooks[0];
        var periods = await _ledgerBooks
            .ListPeriodsAsync(new LedgerPeriodQuery(LedgerBookId: book.LedgerBookId), ct)
            .ConfigureAwait(false);
        var matchingPeriods = periods
            .Where(period =>
                period.StartDate == request.PeriodStart
                && period.EndDate == request.PeriodEnd
                && (requested is null || period.PeriodId == requested.AccountingPeriodId))
            .DistinctBy(static period => period.PeriodId)
            .ToArray();
        if (matchingPeriods.Length != 1)
        {
            throw Failure(
                matchingPeriods.Length == 0
                    ? "STATEMENT_ACCOUNTING_PERIOD_REQUIRED"
                    : "STATEMENT_ACCOUNTING_PERIOD_AMBIGUOUS",
                matchingPeriods.Length == 0
                    ? "No ledger period exactly matches the retained statement period."
                    : "More than one ledger period matches the retained statement period.");
        }

        var period = matchingPeriods[0];
        if (period.Status != LedgerPeriodStatusDto.Open
            && !request.AllowClosedPeriodForRetainedWorkflow)
        {
            throw Failure(
                "STATEMENT_ACCOUNTING_PERIOD_CLOSED",
                $"Ledger period '{period.PeriodId:D}' is {period.Status} and cannot accept a new statement intake. Reopen it through governed accounting close controls before importing replacement evidence.");
        }

        if (requested is not null && requested.AsOfDate != period.EndDate)
        {
            throw Failure(
                "STATEMENT_AS_OF_SCOPE_MISMATCH",
                "The requested statement as-of date does not match the exact ledger-period end.");
        }

        return new StatementAccountingScope(
            fundProfileId,
            book.LedgerBookId,
            period.PeriodId,
            period.EndDate);
    }

    public async Task<StatementReconciliationIntakeReceipt> PublishAsync(
        string statementWorkflowId,
        StatementImportCommitResultDto import,
        StatementAccountingScope accountingScope,
        string tenantId,
        string companyId,
        string actor,
        string sourceInstitution,
        IReadOnlyList<string> evidenceReferences,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(statementWorkflowId);
        ArgumentNullException.ThrowIfNull(import);
        ArgumentNullException.ThrowIfNull(accountingScope);
        EnsureConfigured();
        var normalizedActor = Require(actor, "STATEMENT_ACTOR_REQUIRED");
        var queueScope = new ReconciliationBreakQueueScope(tenantId, companyId);
        var statementRun = await _statementRuns!.GetAsync(import.RunId, ct).ConfigureAwait(false);
        if (statementRun is null
            || !Guid.TryParse(statementRun.Import.FundAccountId, out var fundAccountId)
            || fundAccountId == Guid.Empty)
        {
            throw Failure(
                "STATEMENT_RUN_SCOPE_NOT_FOUND",
                $"Statement run '{import.RunId}' does not expose its canonical fund-account scope.");
        }

        var authorizedScope = await ResolveAccountingScopeAsync(
                new StatementReconciliationIntakeScopeRequest(
                    tenantId,
                    companyId,
                    statementRun.Import.FundAccountId,
                    statementRun.Import.ExternalAccountId,
                    sourceInstitution,
                    statementRun.Import.StatementPeriodStart,
                    statementRun.Import.StatementPeriodEnd,
                    accountingScope),
                ct)
            .ConfigureAwait(false);
        if (!AccountingScopesEqual(authorizedScope, accountingScope))
        {
            throw Failure(
                "STATEMENT_ACCOUNTING_SCOPE_CHANGED",
                "The statement accounting scope changed before Operations and casework publication.");
        }

        var evidence = BuildEvidence(
            statementWorkflowId,
            import.RunId,
            evidenceReferences,
            sourceInstitution);
        var operationsWorkflow = await EnsureOperationsWorkflowAsync(
                fundAccountId,
                accountingScope,
                normalizedActor,
                sourceInstitution,
                statementWorkflowId,
                evidence,
                ct)
            .ConfigureAwait(false);

        var sourceBreaks = (await _reconciliation!
                .ListOpenStatementBreaksAsync(queueScope, ct)
                .ConfigureAwait(false))
            .Where(item => IsForImport(item, import.RunId))
            .ToArray();
        foreach (var sourceBreak in sourceBreaks)
        {
            var item = ReconciliationBreakQueueProjection.ProjectStatement(
                sourceBreak,
                accountingScope,
                statementRun.Import.FundAccountId,
                queueScope);
            await _breakQueue!
                .CreateIfMissingAsync(queueScope, item, ct)
                .ConfigureAwait(false);
        }

        if (sourceBreaks.Length < Math.Max(import.BreakCount, import.CaseCount))
        {
            throw Failure(
                "STATEMENT_CASEWORK_PUBLICATION_INCOMPLETE",
                $"Published {sourceBreaks.Length} of {Math.Max(import.BreakCount, import.CaseCount)} retained statement casework obligations.");
        }

        var retainedItems = (await _breakQueue!.GetAllAsync(queueScope, ct: ct).ConfigureAwait(false))
            .Where(item =>
                string.Equals(item.SourceType, "statement", StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.SourceImportId, import.RunId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (retainedItems.Length < sourceBreaks.Length
            || retainedItems.Any(item =>
                !string.Equals(
                    item.FundProfileId,
                    accountingScope.FundProfileId,
                    StringComparison.OrdinalIgnoreCase)
                || item.LedgerBookId != accountingScope.LedgerBookId
                || !string.Equals(
                    item.AccountingPeriodId,
                    accountingScope.AccountingPeriodId.ToString("D"),
                    StringComparison.OrdinalIgnoreCase)
                || item.AsOfDate != accountingScope.AsOfDate))
        {
            throw Failure(
                "STATEMENT_CASEWORK_SCOPE_NOT_RETAINED",
                "The canonical reconciliation queue did not retain the exact statement accounting scope.");
        }

        return new StatementReconciliationIntakeReceipt(
            accountingScope,
            operationsWorkflow.WorkflowId,
            retainedItems.Length,
            evidence.Select(static item => item.EvidenceId).ToArray());
    }

    private async Task<OperationsContinuityWorkflowDto> EnsureOperationsWorkflowAsync(
        Guid fundAccountId,
        StatementAccountingScope scope,
        string actor,
        string sourceInstitution,
        string correlationId,
        IReadOnlyList<OperationsEvidenceLinkDto> evidence,
        CancellationToken ct)
    {
        var periodId = scope.AccountingPeriodId.ToString("D");
        var candidates = await _operations!
            .ListAsync(fundAccountId, periodId, status: null, ct: ct, ledgerBookId: scope.LedgerBookId)
            .ConfigureAwait(false);
        if (candidates.Count > 1)
        {
            throw Failure(
                "OPERATIONS_WORKFLOW_AMBIGUOUS",
                "More than one Operations Continuity workflow matches the exact statement close scope.");
        }

        OperationsContinuityWorkflowDto workflow;
        if (candidates.Count == 0)
        {
            var started = await _operations
                .StartWorkflowAsync(
                    new OperationsStartWorkflowRequestDto(
                        fundAccountId,
                        periodId,
                        SecurityMasterSnapshotId: null,
                        BrokerSource: sourceInstitution,
                        Actor: actor,
                        Rationale: "Retain the authoritative statement intake for governed reconciliation and close.",
                        CorrelationId: correlationId,
                        EvidenceLinks: evidence,
                        LedgerBookId: scope.LedgerBookId),
                    ct)
                .ConfigureAwait(false);
            if (!started.Success || started.Workflow is null)
            {
                candidates = await _operations
                    .ListAsync(
                        fundAccountId,
                        periodId,
                        status: null,
                        ct: ct,
                        ledgerBookId: scope.LedgerBookId)
                    .ConfigureAwait(false);
                if (candidates.Count != 1)
                {
                    throw Failure(
                        started.ErrorCode ?? "OPERATIONS_WORKFLOW_START_FAILED",
                        started.ErrorMessage
                        ?? "Operations Continuity did not retain the exact statement workflow.");
                }

                workflow = await _operations
                    .GetAsync(candidates[0].WorkflowId, ct)
                    .ConfigureAwait(false)
                    ?? throw Failure(
                        "OPERATIONS_WORKFLOW_NOT_FOUND",
                        "The concurrently created Operations Continuity workflow could not be read back.");
            }
            else
            {
                workflow = started.Workflow;
            }
        }
        else
        {
            workflow = await _operations
                .GetAsync(candidates[0].WorkflowId, ct)
                .ConfigureAwait(false)
                ?? throw Failure(
                    "OPERATIONS_WORKFLOW_NOT_FOUND",
                    "The matching Operations Continuity workflow could not be read.");
        }

        if (workflow.Status == OperationsWorkflowStatusDto.Closed)
        {
            throw Failure(
                "OPERATIONS_WORKFLOW_CLOSED",
                "The exact Operations Continuity workflow is already closed and cannot accept a new statement.");
        }

        var timeline = await _operations.GetTimelineAsync(workflow.WorkflowId, ct).ConfigureAwait(false);
        var evidenceAlreadyRetained = timeline
            .SelectMany(static entry => entry.References)
            .Any(reference =>
                string.Equals(
                    reference.EvidenceId,
                    evidence[0].EvidenceId,
                    StringComparison.Ordinal));
        if (evidenceAlreadyRetained
            && workflow.BrokerIntakeState != OperationsBrokerIntakeStateDto.Pending)
        {
            return workflow;
        }

        OperationsTransitionResultDto retained;
        if (workflow.BrokerIntakeState == OperationsBrokerIntakeStateDto.Pending)
        {
            retained = await _operations
                .ImportBrokerDataAsync(
                    workflow.WorkflowId,
                    new OperationsTransitionRequestDto(
                        workflow.Version,
                        actor,
                        "Retained source statement and Evidence Vault lineage.",
                        correlationId,
                        evidence),
                    ct)
                .ConfigureAwait(false);
        }
        else
        {
            retained = await _operations
                .RefreshGatePostureAsync(
                    workflow.WorkflowId,
                    new OperationsGatePostureRequestDto(
                        workflow.Version,
                        actor,
                        "Attach retained statement intake evidence without advancing human close gates.",
                        correlationId,
                        EvidenceLinks: evidence),
                    ct)
                .ConfigureAwait(false);
        }

        return retained.Success && retained.Workflow is not null
            ? retained.Workflow
            : throw Failure(
                retained.ErrorCode ?? "OPERATIONS_STATEMENT_HANDOFF_FAILED",
                retained.ErrorMessage
                ?? "Operations Continuity did not retain the statement intake evidence.");
    }

    private static IReadOnlyList<OperationsEvidenceLinkDto> BuildEvidence(
        string workflowId,
        string statementRunId,
        IReadOnlyList<string> references,
        string sourceInstitution)
    {
        var capturedAt = DateTimeOffset.UtcNow;
        var evidence = new List<OperationsEvidenceLinkDto>
        {
            new(
                $"statement-intake:{statementRunId}",
                $"Retained {sourceInstitution} statement",
                $"/api/workstation/reconciliation/statement-reconciliation-report/{Uri.EscapeDataString(workflowId)}",
                "statement-reconciliation-report",
                capturedAt)
        };
        evidence.AddRange((references ?? [])
            .Where(static reference => !string.IsNullOrWhiteSpace(reference))
            .Select((reference, index) => new OperationsEvidenceLinkDto(
                $"statement-intake-reference:{statementRunId}:{index + 1}",
                "Statement intake evidence",
                reference.Trim(),
                "statement-evidence-vault",
                capturedAt)));
        return evidence;
    }

    private static bool IsForImport(StatementBreakDto item, string importId)
        => string.Equals(item.InternalReference, importId, StringComparison.OrdinalIgnoreCase)
           || (!string.IsNullOrWhiteSpace(item.StatementReference)
               && item.StatementReference.StartsWith(
                   $"{importId}:",
                   StringComparison.OrdinalIgnoreCase));

    private static bool SourceMatchesAccount(
        StatementReconciliationIntakeScopeRequest request,
        AccountSummaryDto account)
    {
        var externalAccountMatches = new[]
        {
            account.AccountCode,
            account.CustodianDetails?.SubAccountNumber,
            account.BankDetails?.AccountNumber,
            account.BankDetails?.Iban
        }.Any(candidate =>
            string.Equals(
                candidate?.Trim(),
                request.ExternalAccountId?.Trim(),
                StringComparison.OrdinalIgnoreCase));
        var institutionMatches = new[]
        {
            account.Institution,
            account.BankDetails?.BankName
        }.Any(candidate =>
            string.Equals(
                candidate?.Trim(),
                request.SourceInstitution?.Trim(),
                StringComparison.OrdinalIgnoreCase));
        return externalAccountMatches && institutionMatches;
    }

    private static string Require(string? value, string code)
        => !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : throw Failure(code, "Required statement authority scope is missing.");

    private void EnsureConfigured()
    {
        if (_accounts is null
            || _tenancy is null
            || _ledgerBooks is null
            || _operations is null
            || _statementRuns is null
            || _reconciliation is null
            || _breakQueue is null)
        {
            throw Failure(
                "STATEMENT_INTAKE_AUTHORITY_UNAVAILABLE",
                "Statement-to-close intake requires account ownership, fund tenancy, ledger books, Operations Continuity, statement runs, reconciliation, and the canonical casework queue.");
        }
    }

    private static StatementReconciliationIntakeAuthorityException Failure(
        string code,
        string message,
        Exception? innerException = null)
        => new(code, message, innerException);

    private static bool AccountingScopesEqual(
        StatementAccountingScope left,
        StatementAccountingScope right) =>
        string.Equals(left.FundProfileId, right.FundProfileId, StringComparison.OrdinalIgnoreCase)
        && left.LedgerBookId == right.LedgerBookId
        && left.AccountingPeriodId == right.AccountingPeriodId
        && left.AsOfDate == right.AsOfDate;
}
