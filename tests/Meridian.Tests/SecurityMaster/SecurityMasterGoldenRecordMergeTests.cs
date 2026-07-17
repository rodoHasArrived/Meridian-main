using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Field-level provenance and golden-record merge: resolving a field-value conflict must rewrite the
/// winning value into the stored terms and stamp per-field provenance (source/authority/confidence/as-of),
/// not merely annotate a winner on the conflict row. Identifier-ambiguity conflicts keep their existing
/// annotate-only behavior. These are pure, DB-free tests over the shared merge helpers and the in-memory
/// conflict store.
/// </summary>
public sealed class SecurityMasterGoldenRecordMergeTests
{
    [Fact]
    public void TryResolveWinningValue_PicksCandidateBySource_AndIgnoresNonFieldValueConflicts()
    {
        var conflict = FieldValueConflict("common.currency", "PrimeVendor", "USD", "Backup", "EUR");

        SecurityMasterGoldenRecordMerge.TryResolveWinningValue(conflict, "PrimeVendor", out var winnerA).Should().BeTrue();
        winnerA.Should().Be("USD");

        SecurityMasterGoldenRecordMerge.TryResolveWinningValue(conflict, "Backup", out var winnerB).Should().BeTrue();
        winnerB.Should().Be("EUR");

        // A source that is not one of the two candidates cannot select a value.
        SecurityMasterGoldenRecordMerge.TryResolveWinningValue(conflict, "Unrelated", out _).Should().BeFalse();

        // Identifier-ambiguity conflicts never merge a value.
        var identifierConflict = conflict with { ConflictKind = "IdentifierAmbiguity" };
        SecurityMasterGoldenRecordMerge.TryResolveWinningValue(identifierConflict, "PrimeVendor", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("common.currency", true, "currency")]
    [InlineData("assetSpecific.issuerName", false, "issuerName")]
    [InlineData("issuerName", false, "issuerName")]
    public void ResolveFieldTarget_SplitsScopeAndProperty(string fieldPath, bool expectCommon, string expectProperty)
    {
        var (isCommon, property) = SecurityMasterGoldenRecordMerge.ResolveFieldTarget(fieldPath);
        isCommon.Should().Be(expectCommon);
        property.Should().Be(expectProperty);
    }

    [Fact]
    public void ApplyFieldValue_WritesStringNumberAndBool_ByLiteralShape()
    {
        var terms = JsonSerializer.SerializeToElement(new { currency = "USD", lotSize = 1, isCallable = false });

        var withCurrency = SecurityMasterGoldenRecordMerge.ApplyFieldValue(terms, "currency", "EUR");
        withCurrency.GetProperty("currency").GetString().Should().Be("EUR");

        var withNumber = SecurityMasterGoldenRecordMerge.ApplyFieldValue(terms, "lotSize", "100");
        withNumber.GetProperty("lotSize").ValueKind.Should().Be(JsonValueKind.Number);
        withNumber.GetProperty("lotSize").GetInt32().Should().Be(100);

        var withBool = SecurityMasterGoldenRecordMerge.ApplyFieldValue(terms, "isCallable", "true");
        withBool.GetProperty("isCallable").ValueKind.Should().Be(JsonValueKind.True);

        // Existing sibling properties are preserved when one field is rewritten.
        withCurrency.GetProperty("lotSize").GetInt32().Should().Be(1);
    }

    [Fact]
    public void FieldProvenance_RoundTripsThroughProvenanceBlob_AndUpsertsByPath()
    {
        var provenance = JsonSerializer.SerializeToElement(new { sourceSystem = "PrimeVendor", updatedBy = "operator" });
        var asOf = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);

        var set = SecurityFieldProvenanceSet.Empty
            .Upsert(new SecurityFieldProvenance("common.currency", "PrimeVendor", 0, 1m, asOf, "operator chose prime", "op-1"));

        var stamped = SecurityMasterGoldenRecordMerge.WriteFieldProvenance(provenance, set);

        // The base provenance survives alongside the embedded field set.
        stamped.GetProperty("sourceSystem").GetString().Should().Be("PrimeVendor");

        var readBack = SecurityMasterGoldenRecordMerge.ReadFieldProvenance(stamped);
        var entry = readBack.Find("common.currency");
        entry.Should().NotBeNull();
        entry!.Source.Should().Be("PrimeVendor");
        entry.Confidence.Should().Be(1m);
        entry.AsOf.Should().Be(asOf);
        entry.Reason.Should().Be("operator chose prime");

        // Upsert replaces (not duplicates) an entry for the same path.
        var replaced = readBack.Upsert(new SecurityFieldProvenance("common.currency", "Backup", 1, 0.5m, asOf));
        replaced.Fields.Count(f => f.FieldPath == "common.currency").Should().Be(1);
        replaced.Find("common.currency")!.Source.Should().Be("Backup");
    }

    [Fact]
    public void WriteFieldProvenance_EmptySet_LeavesBlobUnchanged()
    {
        var provenance = JsonSerializer.SerializeToElement(new { sourceSystem = "PrimeVendor" });
        var result = SecurityMasterGoldenRecordMerge.WriteFieldProvenance(provenance, SecurityFieldProvenanceSet.Empty);
        result.TryGetProperty(SecurityFieldProvenanceSet.EmbeddedPropertyName, out _).Should().BeFalse();
    }

    [Fact]
    public void DetectFieldConflictsForProjection_EmitsFieldValueConflict_WhenSharedIdentifierHasDivergentCurrency()
    {
        var a = MakeProjection(Guid.NewGuid(), "Isin", "US0378331005", "PrimeVendor", currency: "USD");
        var b = MakeProjection(Guid.NewGuid(), "Isin", "US0378331005", "Backup", currency: "EUR");

        var conflicts = SecurityMasterConflictDetection.DetectFieldConflictsForProjection(b, new[] { a, b }, DateTimeOffset.UtcNow);

        conflicts.Should().ContainSingle();
        var conflict = conflicts[0];
        conflict.ConflictKind.Should().Be(SecurityMasterGoldenRecordMerge.FieldValueConflictKind);
        conflict.FieldPath.Should().Be("common.currency");
        conflict.SecurityId.Should().Be(b.SecurityId);
        new[] { conflict.ValueA, conflict.ValueB }.Should().BeEquivalentTo(new[] { "EUR", "USD" });
    }

    [Fact]
    public void DetectFieldConflictsForProjection_NoConflict_WhenCurrencyAgreesOrNoSharedIdentifier()
    {
        var a = MakeProjection(Guid.NewGuid(), "Isin", "US0378331005", "PrimeVendor", currency: "USD");
        var sameCurrency = MakeProjection(Guid.NewGuid(), "Isin", "US0378331005", "Backup", currency: "USD");
        SecurityMasterConflictDetection
            .DetectFieldConflictsForProjection(sameCurrency, new[] { a, sameCurrency }, DateTimeOffset.UtcNow)
            .Should().BeEmpty();

        var unrelated = MakeProjection(Guid.NewGuid(), "Isin", "GB0002634946", "Backup", currency: "GBP");
        SecurityMasterConflictDetection
            .DetectFieldConflictsForProjection(unrelated, new[] { a, unrelated }, DateTimeOffset.UtcNow)
            .Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveAsync_FieldValueConflict_MergesWinningValueAndStampsFieldProvenance()
    {
        var winner = MakeProjection(Guid.NewGuid(), "Isin", "US0378331005", "PrimeVendor", currency: "USD");
        var loser = MakeProjection(Guid.NewGuid(), "Isin", "US0378331005", "Backup", currency: "EUR");

        var store = Substitute.For<ISecurityMasterStore>();
        store.LoadAllAsync(Arg.Any<CancellationToken>()).Returns(new[] { winner, loser });
        store.GetProjectionAsync(loser.SecurityId, Arg.Any<CancellationToken>()).Returns(loser);

        var service = new SecurityMasterConflictService(store, NullLogger<SecurityMasterConflictService>.Instance);

        // Recording conflicts for the loser surfaces the currency field-value conflict against the winner.
        await service.RecordConflictsForProjectionAsync(loser, CancellationToken.None);
        var open = await service.GetOpenConflictsAsync(CancellationToken.None);
        var fieldConflict = open.Single(c => c.ConflictKind == SecurityMasterGoldenRecordMerge.FieldValueConflictKind);

        var resolved = await service.ResolveAsync(
            new ResolveConflictRequest(
                fieldConflict.ConflictId,
                Resolution: "Accept",
                ResolvedBy: "operator-1",
                Reason: "prime vendor authoritative",
                ChosenWinnerSource: "PrimeVendor"),
            CancellationToken.None);

        resolved.Should().NotBeNull();

        SecurityProjectionRecord? merged = null;
        await store.Received(1).UpsertProjectionAsync(
            Arg.Do<SecurityProjectionRecord>(r => merged = r),
            Arg.Any<CancellationToken>());
        merged.Should().NotBeNull("resolving a field-value conflict must rewrite the projection");
        merged!.Currency.Should().Be("USD");
        merged.CommonTerms.GetProperty("currency").GetString().Should().Be("USD");

        var fieldProvenance = SecurityMasterGoldenRecordMerge.ReadFieldProvenance(merged.Provenance).Find("common.currency");
        fieldProvenance.Should().NotBeNull();
        fieldProvenance!.Source.Should().Be("PrimeVendor");
        fieldProvenance.UpdatedBy.Should().Be("operator-1");
    }

    [Fact]
    public async Task ResolveAsync_IdentifierConflict_DoesNotRewriteProjection()
    {
        // Two securities claiming the same ticker with the same currency: only an identifier-ambiguity
        // conflict arises, and resolving it must stay annotate-only (no projection rewrite).
        var a = MakeProjection(Guid.NewGuid(), "Ticker", "AAPL", "PrimeVendor", currency: "USD");
        var b = MakeProjection(Guid.NewGuid(), "Ticker", "AAPL", "Backup", currency: "USD");

        var store = Substitute.For<ISecurityMasterStore>();
        store.LoadAllAsync(Arg.Any<CancellationToken>()).Returns(new[] { a, b });
        store.GetProjectionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(b);

        var service = new SecurityMasterConflictService(store, NullLogger<SecurityMasterConflictService>.Instance);
        await service.RecordConflictsForProjectionAsync(b, CancellationToken.None);
        var open = await service.GetOpenConflictsAsync(CancellationToken.None);
        var identifierConflict = open.Single(c => c.ConflictKind == "IdentifierAmbiguity");

        await service.ResolveAsync(
            new ResolveConflictRequest(
                identifierConflict.ConflictId,
                Resolution: "Accept",
                ResolvedBy: "operator-1",
                ChosenWinnerSource: "PrimeVendor"),
            CancellationToken.None);

        await store.DidNotReceive().UpsertProjectionAsync(Arg.Any<SecurityProjectionRecord>(), Arg.Any<CancellationToken>());
    }

    private static SecurityMasterConflict FieldValueConflict(
        string fieldPath, string providerA, string valueA, string providerB, string valueB)
        => new(
            ConflictId: Guid.NewGuid(),
            SecurityId: Guid.NewGuid(),
            ConflictKind: SecurityMasterGoldenRecordMerge.FieldValueConflictKind,
            FieldPath: fieldPath,
            ProviderA: providerA,
            ValueA: valueA,
            ProviderB: providerB,
            ValueB: valueB,
            DetectedAt: DateTimeOffset.UtcNow,
            Status: "Open");

    private static SecurityProjectionRecord MakeProjection(
        Guid securityId, string identifierKind, string identifierValue, string provider, string currency)
    {
        var identifier = new SecurityIdentifierDto(
            Enum.Parse<SecurityIdentifierKind>(identifierKind, ignoreCase: true),
            identifierValue,
            IsPrimary: true,
            ValidFrom: DateTimeOffset.UtcNow.AddDays(-30),
            Provider: provider);

        return new SecurityProjectionRecord(
            SecurityId: securityId,
            AssetClass: "Equity",
            Status: SecurityStatusDto.Active,
            DisplayName: $"Test Security {securityId:N}",
            Currency: currency,
            PrimaryIdentifierKind: identifierKind,
            PrimaryIdentifierValue: identifierValue,
            CommonTerms: JsonSerializer.SerializeToElement(new { currency }),
            AssetSpecificTerms: JsonSerializer.SerializeToElement(new { schemaVersion = 1 }),
            Provenance: JsonSerializer.SerializeToElement(new { sourceSystem = provider, updatedBy = "ingest" }),
            Version: 1,
            EffectiveFrom: DateTimeOffset.UtcNow.AddDays(-30),
            EffectiveTo: null,
            Identifiers: new[] { identifier },
            Aliases: Array.Empty<SecurityAliasDto>());
    }
}
