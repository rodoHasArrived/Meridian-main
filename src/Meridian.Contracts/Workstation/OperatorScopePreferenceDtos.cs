namespace Meridian.Contracts.Workstation;

/// <summary>
/// The operating-scope dimensions the workstation carries across workspaces, as the browser's
/// operating-scope URL parameters and the WPF scope selector name them. Every value is optional.
/// </summary>
public sealed record OperatingScopeValuesDto(
    string? Symbol = null,
    string? FundAccountId = null,
    string? RunId = null,
    string? Provider = null,
    string? From = null,
    string? To = null,
    string? Date = null,
    string? AsOf = null);

/// <summary>
/// The signed-in operator's default operating scope (C1): applied when a session opens with no
/// scope in its link or browser, so the operator does not set it up again on every machine.
/// </summary>
public sealed record OperatorScopePreferenceDto(
    OperatingScopeValuesDto? DefaultScope,
    DateTimeOffset? UpdatedAt);

/// <summary>Sets (or, with a null or empty scope, clears) the signed-in operator's default scope.</summary>
public sealed record OperatorScopePreferenceSaveRequest(OperatingScopeValuesDto? DefaultScope);
