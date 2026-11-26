using Meridian.Contracts.Workstation;
using Meridian.Domain.Reconciliation;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Ui;

/// <summary>
/// Intake authority that resolves every statement to one fixed accounting scope, or refuses with a
/// fixed code, and records what it was asked. Publication is outside these tests.
/// </summary>
internal sealed class FixedStatementIntakeAuthority : IStatementReconciliationIntakeAuthority
{
    public static readonly StatementAccountingScope DefaultScope = new(
        "11111111-1111-1111-1111-111111111111",
        Guid.Parse("22222222-2222-2222-2222-222222222222"),
        Guid.Parse("33333333-3333-3333-3333-333333333333"),
        new DateOnly(2026, 5, 31));

    private readonly StatementAccountingScope _scope;
    private readonly string? _refusalCode;

    public FixedStatementIntakeAuthority(StatementAccountingScope? scope = null, string? refusalCode = null)
    {
        _scope = scope ?? DefaultScope;
        _refusalCode = refusalCode;
    }

    public List<StatementReconciliationIntakeScopeRequest> Requests { get; } = [];

    public Task<StatementAccountingScope> ResolveAccountingScopeAsync(
        StatementReconciliationIntakeScopeRequest request,
        CancellationToken ct = default)
    {
        Requests.Add(request);
        return _refusalCode is null
            ? Task.FromResult(_scope)
            : Task.FromException<StatementAccountingScope>(new StatementReconciliationIntakeAuthorityException(
                _refusalCode, "The statement period has no matching ledger period."));
    }

    public Task<StatementReconciliationIntakeReceipt> PublishAsync(
        string statementWorkflowId,
        StatementImportCommitResultDto import,
        StatementAccountingScope accountingScope,
        string tenantId,
        string companyId,
        string actor,
        string sourceInstitution,
        IReadOnlyList<string> evidenceReferences,
        CancellationToken ct = default)
        => throw new NotSupportedException("Statement-run tests do not publish intake.");

    public static StatementReconciliationAccountingScopeDto ToDto(StatementAccountingScope scope)
        => new(scope.FundProfileId, scope.LedgerBookId, scope.AccountingPeriodId, scope.AsOfDate);
}
