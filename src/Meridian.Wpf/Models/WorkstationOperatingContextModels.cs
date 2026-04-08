using Meridian.Contracts.FundStructure;
using Meridian.Ui.Services;

namespace Meridian.Wpf.Models;

/// <summary>
/// Scope kinds supported by the workstation operating-context header and persistence model.
/// </summary>
public enum OperatingContextScopeKind : byte
{
    Organization,
    Business,
    Client,
    InvestmentPortfolio,
    Fund,
    Entity,
    Sleeve,
    Vehicle,
    Account,
    LedgerGroup
}

public static class OperatingContextScopeKindExtensions
{
    public static string ToDisplayName(this OperatingContextScopeKind scopeKind)
        => scopeKind switch
        {
            OperatingContextScopeKind.Organization => "Organization",
            OperatingContextScopeKind.Business => "Business",
            OperatingContextScopeKind.Client => "Client",
            OperatingContextScopeKind.InvestmentPortfolio => "Investment Portfolio",
            OperatingContextScopeKind.Fund => "Fund",
            OperatingContextScopeKind.Entity => "Entity",
            OperatingContextScopeKind.Sleeve => "Sleeve",
            OperatingContextScopeKind.Vehicle => "Vehicle",
            OperatingContextScopeKind.Account => "Account",
            OperatingContextScopeKind.LedgerGroup => "Ledger Group",
            _ => "Context"
        };
}

/// <summary>
/// Explicit governance subareas surfaced inside the Governance workspace.
/// </summary>
public enum GovernanceSubarea : byte
{
    Operations,
    Accounting,
    Reconciliation,
    Reporting,
    Audit
}

/// <summary>
/// One selectable operating context in the workstation shell.
/// </summary>
public sealed record WorkstationOperatingContext
{
    public OperatingContextScopeKind ScopeKind { get; init; }
    public string ScopeId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public BusinessKindDto BusinessKind { get; init; } = BusinessKindDto.Hybrid;
    public string BaseCurrency { get; init; } = "USD";
    public IReadOnlyList<string> EntityIds { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> PortfolioIds { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> LedgerGroupIds { get; init; } = Array.Empty<string>();
    public string DefaultWorkspaceId { get; init; } = "research";
    public string DefaultLandingPageTag { get; init; } = "ResearchShell";
    public string? OrganizationId { get; init; }
    public string? BusinessId { get; init; }
    public string? ClientId { get; init; }
    public string? InvestmentPortfolioId { get; init; }
    public string? FundId { get; init; }
    public string? EntityId { get; init; }
    public string? SleeveId { get; init; }
    public string? VehicleId { get; init; }
    public string? AccountId { get; init; }
    public string? LedgerGroupId { get; init; }
    public string? CompatibilityFundProfileId { get; init; }
    public string? LegalEntityName { get; init; }
    public DateTimeOffset? LastOpenedAt { get; init; }

    public string ContextKey => CreateContextKey(ScopeKind, ScopeId);

    public string Subtitle
    {
        get
        {
            var parts = new List<string>
            {
                ScopeKind.ToDisplayName()
            };

            parts.Add(BusinessKind switch
            {
                BusinessKindDto.FinancialAdvisor => "Advisory",
                BusinessKindDto.FundManager => "Fund Manager",
                _ => "Hybrid"
            });

            if (!string.IsNullOrWhiteSpace(BaseCurrency))
            {
                parts.Add(BaseCurrency);
            }

            if (!string.IsNullOrWhiteSpace(LegalEntityName) &&
                !string.Equals(LegalEntityName, DisplayName, StringComparison.OrdinalIgnoreCase))
            {
                parts.Add(LegalEntityName);
            }

            return string.Join(" · ", parts);
        }
    }

    public string DefaultWindowPresetId => ScopeKind switch
    {
        OperatingContextScopeKind.Client or OperatingContextScopeKind.InvestmentPortfolio => "research-compare",
        OperatingContextScopeKind.Fund or OperatingContextScopeKind.Entity or OperatingContextScopeKind.Account or OperatingContextScopeKind.LedgerGroup => "accounting-review",
        _ => "trading-cockpit"
    };

    public static string CreateContextKey(OperatingContextScopeKind scopeKind, string scopeId)
        => $"{scopeKind}:{scopeId}";
}

public sealed class WorkstationOperatingContextChangingEventArgs : EventArgs
{
    public WorkstationOperatingContextChangingEventArgs(
        WorkstationOperatingContext? previousContext,
        WorkstationOperatingContext nextContext)
    {
        PreviousContext = previousContext;
        NextContext = nextContext ?? throw new ArgumentNullException(nameof(nextContext));
    }

    public WorkstationOperatingContext? PreviousContext { get; }

    public WorkstationOperatingContext NextContext { get; }
}

public sealed class WorkstationOperatingContextChangedEventArgs : EventArgs
{
    public WorkstationOperatingContextChangedEventArgs(WorkstationOperatingContext context)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public WorkstationOperatingContext Context { get; }
}
