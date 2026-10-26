using System.Text.Json;
using System.Text.Json.Nodes;
using Meridian.Application.SecurityMaster;
using Meridian.Application.SecurityMaster.Rebuild;
using Meridian.Contracts.SecurityMaster;
using Meridian.ReferenceData.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Meridian.Tests.SecurityMaster.Workbench;

/// <summary>
/// The real canonical amend seam (<see cref="SecurityMasterService"/>) over a mocked projection store,
/// so an approved field-edit merge runs the Write-mode mapping a deployed node runs. With no events and
/// no snapshot the rebuilder falls back to the stored projection; appended events and upserted
/// projections are recorded.
/// </summary>
internal sealed class CanonicalAmendHarness
{
    public CanonicalAmendHarness(Mock<ISecurityMasterStore> projectionStore, ISecurityAssetProfileCatalog? assetProfileCatalog = null)
    {
        var eventStore = new Mock<ISecurityMasterEventStore>(MockBehavior.Loose);
        eventStore
            .Setup(e => e.LoadAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<SecurityMasterEventEnvelope>());
        eventStore
            .Setup(e => e.AppendAsync(It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<IReadOnlyList<SecurityMasterEventEnvelope>>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, long, IReadOnlyList<SecurityMasterEventEnvelope>, CancellationToken>((_, _, events, _) => Appended.AddRange(events))
            .Returns(Task.CompletedTask);
        projectionStore
            .Setup(s => s.UpsertProjectionAsync(It.IsAny<SecurityProjectionRecord>(), It.IsAny<CancellationToken>()))
            .Callback<SecurityProjectionRecord, CancellationToken>((projection, _) => Upserted.Add(projection))
            .Returns(Task.CompletedTask);

        var snapshotStore = new Mock<ISecurityMasterSnapshotStore>(MockBehavior.Loose);
        Service = new SecurityMasterService(
            eventStore.Object,
            snapshotStore.Object,
            projectionStore.Object,
            new SecurityMasterAggregateRebuilder(eventStore.Object, snapshotStore.Object),
            new SecurityMasterOptions
            {
                SnapshotIntervalVersions = 50,
                ResolveInactiveByDefault = true
            },
            NullLogger<SecurityMasterService>.Instance,
            assetProfileCatalog: assetProfileCatalog);
    }

    public SecurityMasterService Service { get; }

    public List<SecurityMasterEventEnvelope> Appended { get; } = [];

    public List<SecurityProjectionRecord> Upserted { get; } = [];
}

/// <summary>
/// Forwards to the real canonical amend seam and records what it throws. A publish fan-out logs a
/// handler's exception and surfaces only the handler's name, so this is how a publish-level test sees
/// WHY the merge's amendment was refused.
/// </summary>
internal sealed class RecordingAmender(ISecurityMasterAmender inner) : ISecurityMasterAmender
{
    public List<Exception> Failures { get; } = [];

    public async Task<SecurityDetailDto> AmendTermsAsync(AmendSecurityTermsRequest request, CancellationToken ct = default)
    {
        try
        {
            return await inner.AmendTermsAsync(request, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Failures.Add(ex);
            throw;
        }
    }
}

/// <summary>
/// Stored terms and refusal texts shared by the workbench and merge tests of the readable-before-
/// writable expansion. The reasons are spelled out here rather than built by the code under test.
/// </summary>
internal static class AheadOfWriteFixtures
{
    public const string LegsPath = "assetSpecificTerms.legs";

    // A two-leg fixed/float swap as the version-1 writer persists it.
    public const string SwapAtWriteVersion =
        """{"effectiveDate":"2026-01-15","legs":[{"currency":"USD","currentIndexRate":null,"dayCount":"30/360","direction":"Pay","exchangesPrincipal":false,"fixedRate":4.25,"index":null,"legId":"fixed","legType":"Fixed","notional":10000000,"notionalSchedule":[],"paymentFrequency":"SemiAnnual","resetSchedule":[],"spreadBps":null},{"currency":"USD","currentIndexRate":null,"dayCount":"ACT/360","direction":"Receive","exchangesPrincipal":false,"fixedRate":null,"index":"SOFR","legId":"float","legType":"Floating","notional":10000000,"notionalSchedule":[],"paymentFrequency":"Quarterly","resetSchedule":[],"spreadBps":15}],"maturityDate":"2031-01-15","schemaVersion":1,"termsFormat":"flat","assetClassTermsVersion":1}""";

    // A plain fixed-rate bond as the version-1 writer persists it.
    public const string BondAtWriteVersion =
        """{"callDate":null,"capRate":null,"couponRate":4.25,"couponType":"Fixed","dayCount":"30/360","floatingIndex":null,"floorRate":null,"inflationBaseIndexValue":null,"inflationIndex":null,"inflationIndexRatio":null,"isCallable":false,"issueDate":"2024-01-01","issuerName":"ACME Corp","legalFinalMaturity":null,"mandatoryPutDate":null,"maturity":"2030-01-01","par":1000,"paymentFrequency":"SemiAnnual","preRefundDate":null,"principalSchedule":[],"schemaVersion":1,"termsFormat":"flat","assetClassTermsVersion":1,"seniority":"Senior","spreadBps":null,"stepSchedule":[],"subclass":"Corporate"}""";

    public const string PopulatedLegBlock =
        """{"businessDayConvention":"ModifiedFollowing","paymentCalendars":["USNY"],"paymentLagBusinessDays":2}""";

    // The frozen canonical block a version-2 writer emits: all twelve members, alphabetical.
    public const string PopulatedBondBlock =
        """{"accrualAdjusted":true,"businessDayConvention":"ModifiedFollowing","couponCashBasis":null,"couponRateUnit":null,"direction":"Backward","endOfMonthRule":false,"firstRegularDate":"2024-07-01","lastRegularDate":null,"paymentCalendars":["USNY","GBLO"],"paymentLagBusinessDays":2,"rollConvention":"EndOfMonth","stubPolicy":"Short"}""";

    public const string BondWriteVersionReason =
        "assetClassTermsVersion '2' for asset class 'Bond' is readable but not writable on this node: it reads versions " +
        "1 through 2 and writes version 1. Submit the terms at version 1, or apply the change from a node that writes version 2.";

    public const string StructuredCreditWriteVersionReason =
        "assetClassTermsVersion '3' for asset class 'StructuredCredit' is readable but not writable on this node: it reads " +
        "versions 1 through 3 and writes version 2. Submit the terms at version 2, or apply the change from a node that writes version 3.";

    public static string NotYetWritableReason(string assetClass, string path, int sinceVersion, int writes)
        => $"'{path}' is an asset-class terms version {sinceVersion} term of '{assetClass}'. This node reads it but writes " +
           $"assetClassTermsVersion {writes}, so a create, amend, import or field edit cannot set it yet. Leave it absent, " +
           "null or empty (ADR-024).";

    /// <summary>
    /// The version-1 bond stamped <paramref name="stamp"/> (or unstamped when null), carrying the
    /// populated block when <paramref name="withBlock"/> is set.
    /// </summary>
    public static JsonElement BondTerms(int? stamp, bool withBlock)
    {
        var terms = JsonNode.Parse(BondAtWriteVersion)!.AsObject();
        if (stamp is int version)
        {
            terms[SecurityTermsEnvelope.AssetClassTermsVersionKey] = version;
        }
        else
        {
            terms.Remove(SecurityTermsEnvelope.AssetClassTermsVersionKey);
        }

        if (withBlock)
        {
            terms["scheduleConventions"] = JsonNode.Parse(PopulatedBondBlock);
        }

        return Parse(terms.ToJsonString());
    }

    /// <summary>The stored swap's legs, with leg 1's spread changed and <paramref name="block"/> set on one leg.</summary>
    public static string SwapLegsWith(int legIndex, string block)
    {
        var legs = JsonNode.Parse(SwapAtWriteVersion)!["legs"]!.AsArray();
        legs[1]!["spreadBps"] = 20;
        legs[legIndex]!["scheduleConventions"] = JsonNode.Parse(block);
        return legs.ToJsonString();
    }

    public static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
