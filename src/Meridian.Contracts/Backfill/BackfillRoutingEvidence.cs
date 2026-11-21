namespace Meridian.Contracts.Backfill;

/// <summary>Sanitized outcome for one candidate in a historical backfill request.</summary>
public sealed record BackfillRoutingAttempt(string Symbol, string ProviderId, string Reason, int Bars, bool Attempted);

/// <summary>Bars retained by a backfill request from one serving provider.</summary>
public sealed record BackfillProviderContribution(string ProviderId, long Bars);
