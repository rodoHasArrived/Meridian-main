namespace Meridian.Contracts.SecurityMaster;

public sealed record ResolveSecurityRequest(
    SecurityIdentifierKind IdentifierKind,
    string IdentifierValue,
    string? Provider,
    DateTimeOffset? AsOfUtc,
    bool ActiveOnly = false);

public sealed record SecuritySearchRequest(
    string Query,
    int Take = 50,
    int Skip = 0,
    bool ActiveOnly = false);

public sealed record SecurityHistoryRequest(
    Guid SecurityId,
    int Take = 100);
