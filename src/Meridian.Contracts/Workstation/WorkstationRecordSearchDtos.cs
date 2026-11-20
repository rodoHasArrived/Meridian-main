namespace Meridian.Contracts.Workstation;

/// <summary>A navigation projection of an authorized record. Financial values stay in the detail view.</summary>
public sealed record WorkstationRecordSearchHit(
    string RecordType,
    string RecordId,
    string Label,
    string Scope,
    string AsOfPosture,
    string Status,
    string Href);

public sealed record WorkstationRecordSearchResult(
    IReadOnlyList<WorkstationRecordSearchHit> Items,
    bool IsPartial);
