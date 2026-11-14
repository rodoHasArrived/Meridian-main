using Meridian.Contracts.Workstation;

namespace Meridian.Contracts.Ledger;

/// <summary>Exact, retained operator delegation for synthetic fill accounting; never a reviewer identity.</summary>
public sealed record AutomatedPostingGrantReferenceDto(
    Guid GrantId,
    long Version,
    string TenantId,
    string FinancialAccountId,
    Guid LedgerBookId,
    Guid PeriodId,
    string PolicyId,
    string PolicyVersion,
    string MappingRevision,
    string SourceMode,
    string? CompanyId = null);

public sealed record ActivateAutomatedPostingGrantRequest(
    Guid GrantId,
    string FinancialAccountId,
    Guid LedgerBookId,
    Guid PeriodId,
    string PolicyId,
    string PolicyVersion,
    string MappingRevision,
    string SourceMode = "Synthetic",
    string? CompanyId = null,
    OperationsActionOriginDto ActionOrigin = OperationsActionOriginDto.HumanOperator);

public sealed record RevokeAutomatedPostingGrantRequest(
    long ExpectedVersion,
    OperationsActionOriginDto ActionOrigin = OperationsActionOriginDto.HumanOperator);

public sealed record AutomatedPostingGrantDto(
    AutomatedPostingGrantReferenceDto Reference,
    bool IsActive,
    string ActivatedBy,
    DateTimeOffset ActivatedAtUtc,
    string? RevokedBy = null,
    DateTimeOffset? RevokedAtUtc = null)
{
    /// <summary>Immutable operator-authorized policy retained for deterministic process restart.</summary>
    public AccountingPolicyDto? PolicySnapshot { get; init; }
}

public interface IAutomatedPostingGrantStore
{
    Task<AutomatedPostingGrantDto> ActivateAsync(ActivateAutomatedPostingGrantRequest request,
        string actor, string tenantId, CancellationToken ct = default);
    Task<AutomatedPostingGrantDto> ActivateWithPolicyAsync(ActivateAutomatedPostingGrantRequest request,
        AccountingPolicyDto policySnapshot, string actor, string tenantId, CancellationToken ct = default)
        => Task.FromException<AutomatedPostingGrantDto>(new NotSupportedException("This store cannot retain policy authority."));

    Task<AutomatedPostingGrantDto> RevokeAsync(Guid grantId, long expectedVersion,
        string actor, string tenantId, CancellationToken ct = default);
    Task<AutomatedPostingGrantDto?> GetAsync(Guid grantId, string tenantId, CancellationToken ct = default);
    /// <summary>Checks current grant, policy and exact mapped content without posting.</summary>
    Task ValidateCurrentAsync(AutomatedPostingGrantReferenceDto reference, CancellationToken ct = default)
        => Task.FromException(new NotSupportedException("This store cannot validate current automatic posting authority."));
}
