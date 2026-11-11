using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Xunit;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// ADR-027 stage 3 (S3-D), the completed migration: the schema-bound decode ledger is held equal
/// to the catalog, and the reads the schema cannot bind are held to their committed ledger
/// (<see cref="SecurityMasterMapping.UnboundCompatibilityReads"/>) from both sides — an entry
/// must stay unbindable and must still be a read the decode makes.
/// </summary>
public sealed partial class SecurityAssetTermsSchemaDecodeSweepTests
{
    [Fact]
    public void SchemaBoundDecodeClasses_AreExactlyTheCatalogClasses()
    {
        SecurityMasterMapping.SchemaBoundDecodeClasses.Should().OnlyHaveUniqueItems();
        SecurityMasterMapping.SchemaBoundDecodeClasses.Should().BeEquivalentTo(
            SecurityAssetClassCatalog.AssetClasses,
            "every catalog class decodes its scalar terms through the schema-bound reader; a class added to the " +
            "catalog must be migrated into the decode switch and ledgered, not left reading past the schema");
        SecurityMasterMapping.SchemaBoundDecodeClasses.Should().BeEquivalentTo(
            SecurityAssetTermsSchema.AssetClasses,
            "the bound reader verifies reads against the schema table, so the ledger and the table name the same classes");
    }

    // ── The unbound-read ledger ─────────────────────────────────────────────────────────────────

    private static string LedgerKey(SecurityMasterMapping.UnboundCompatibilityRead read) => $"{read.AssetClass}.{read.Path}";

    /// <summary>The schema's declaration for a ledger path: a root field, or a member of a registered inner shape.</summary>
    private static SecurityAssetTermField? DeclaredAt(string assetClass, string path)
    {
        var split = path.LastIndexOf('.');
        return split < 0
            ? SecurityAssetTermsSchema.Field(assetClass, path)
            : SecurityAssetTermsSchema.ElementFields(assetClass, path[..split])
                .FirstOrDefault(field => string.Equals(field.Key, path[(split + 1)..], StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void UnboundCompatibilityReads_NameOnlyReadsTheSchemaCannotBind()
    {
        var ledger = SecurityMasterMapping.UnboundCompatibilityReads;
        ledger.Select(LedgerKey).Should().OnlyHaveUniqueItems("a read is ledgered once, as the one type the codec reads it as");

        foreach (var read in ledger)
        {
            SecurityMasterMapping.SchemaBoundDecodeClasses.Should().Contain(read.AssetClass,
                "the ledger records exceptions to a schema-bound class decode");
            read.Reason.Should().NotBeNullOrWhiteSpace($"{LedgerKey(read)} must say why it stays unbound");

            var declared = DeclaredAt(read.AssetClass, read.Path);
            (declared is null || declared.Type != read.ReadAs).Should().BeTrue(
                $"the schema now declares {LedgerKey(read)} as {read.ReadAs}, the type the codec reads it as: " +
                "bind the read through the schema-bound view and retire the ledger entry");
        }
    }

    /// <summary>
    /// One value per ledgered read that differs from the base document's, so a decode that still
    /// makes the read produces a different record. The keys are the ledger, exactly: a new entry
    /// needs a probe, and an entry whose read is gone cannot keep one that passes.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> UnboundReadProbes =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["CustomAsset.maturity"] = "\"2031-03-15\"",
            ["CustomAsset.issuerName"] = "\"Probe Issuer\"",
            ["CustomAsset.settlementType"] = "\"Physical\"",
            ["Equity.preferredTerms.dividendRate"] = "7.75",
            ["Equity.preferredTerms.dividendType"] = "\"Fixed\"",
            ["Equity.preferredTerms.redemptionPrice"] = "101.25",
            ["Equity.preferredTerms.redemptionDate"] = "\"2032-06-30\"",
            ["Equity.preferredTerms.callableDate"] = "\"2029-06-30\"",
            ["Equity.preferredTerms.participationTerms"] = "null",
            ["Equity.preferredTerms.participationTerms.participatesInCommonDividends"] = "false",
            ["Equity.preferredTerms.participationTerms.additionalDividendThreshold"] = "2.5",
            ["Equity.preferredTerms.liquidationPreference"] = "{\"kind\":\"Pari\"}",
            ["Equity.preferredTerms.liquidationPreference.kind"] = "\"Subordinated\"",
            ["Equity.preferredTerms.liquidationPreference.multiple"] = "3.5"
        };

    /// <summary>
    /// The stored row each class's unbound reads are reached on: the envelope-less legacy
    /// CustomAsset row the read path salvages as OtherSecurity, and otherwise the class twin (the
    /// Equity twin is convertible-preferred with every preferred term populated).
    /// </summary>
    private static JsonObject UnboundReadBase(string assetClass)
        => assetClass == "CustomAsset"
            ? new JsonObject { ["category"] = "LegacyCustom", ["subType"] = "Bespoke" }
            : MutableTwin(assetClass);

    [Fact]
    public void UnboundCompatibilityReads_AreEachALiveDecodeRead()
    {
        var ledger = SecurityMasterMapping.UnboundCompatibilityReads;
        UnboundReadProbes.Keys.Should().BeEquivalentTo(ledger.Select(LedgerKey),
            "every ledgered read needs a probe proving the decode still makes it, and a probe without a ledger entry is stale");

        foreach (var read in ledger)
        {
            var probed = UnboundReadBase(read.AssetClass);
            var segments = read.Path.Split('.');
            var container = probed;
            foreach (var segment in segments[..^1])
            {
                container = container[segment]!.AsObject();
            }

            container[segments[^1]] = JsonNode.Parse(UnboundReadProbes[LedgerKey(read)]);

            // Decoding the probed row also runs the read through the ledgered view, which throws
            // codec/schema drift if the entry were missing or had become bindable.
            ReadBytes(read.AssetClass, probed).Should().NotBe(ReadBytes(read.AssetClass, UnboundReadBase(read.AssetClass)),
                $"{LedgerKey(read)} is ledgered as a read the decode makes outside the schema; if the decode no longer " +
                "reads it, the entry is stale and must be removed");
        }
    }

    [Fact]
    public void EnvelopeLessCustomAssetRow_StillSalvagesAsOtherSecurity_ThroughTheLedgeredReads()
    {
        // The read tolerance the three CustomAsset ledger entries exist for, pinned on its values:
        // a legacy row with no profile envelope reads as OtherSecurity carrying the OtherSecurity
        // shape, although the CustomAsset schema declares only category and subType of it.
        var legacy = new JsonObject
        {
            ["category"] = "LegacyCustom",
            ["subType"] = "Bespoke",
            ["maturity"] = "2031-03-15",
            ["issuerName"] = "Probe Issuer",
            ["settlementType"] = "Physical"
        };

        var record = SecurityMasterMapping.ToRecord(
            SecurityAssetTermsSchemaRoundTripTests.LegacyProjection("CustomAsset", JsonDocument.Parse(legacy.ToJsonString()).RootElement.Clone()));

        record.Kind.IsOtherSecurity.Should().BeTrue("an envelope-less CustomAsset row degrades to OtherSecurity on read");
        using var written = JsonDocument.Parse(new Meridian.FSharp.SecurityMasterInterop.SecurityMasterSnapshotWrapper(record).AssetSpecificTermsJson);
        written.RootElement.GetProperty("category").GetString().Should().Be("LegacyCustom");
        written.RootElement.GetProperty("subType").GetString().Should().Be("Bespoke");
        written.RootElement.GetProperty("maturity").GetString().Should().Be("2031-03-15");
        written.RootElement.GetProperty("issuerName").GetString().Should().Be("Probe Issuer");
        written.RootElement.GetProperty("settlementType").GetString().Should().Be("Physical");
    }

    // ── The reader's drift refusals ─────────────────────────────────────────────────────────────

    private static readonly Type SchemaBoundView =
        typeof(SecurityMasterMapping).GetNestedType("SchemaBoundTerms", BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("SecurityMasterMapping.SchemaBoundTerms was renamed or removed; update the drift-refusal pins.");

    /// <summary>
    /// The reader is private to the mapping and its refusals only fire on drift, which a green
    /// tree never has; reflection is the one way to prove each refusal actually refuses.
    /// </summary>
    private static object View(string assetClass, string json)
    {
        var mode = typeof(SecurityMasterMapping).GetNestedType("SecurityKindMappingMode", BindingFlags.NonPublic)!;
        return Activator.CreateInstance(
            SchemaBoundView,
            [assetClass, JsonDocument.Parse(json).RootElement.Clone(), Enum.Parse(mode, "Read"), null, null, false])!;
    }

    private static object Unbound(object view) => SchemaBoundView.GetProperty("Unbound")!.GetValue(view)!;

    private static object? Read(object view, string reader, string key)
    {
        try
        {
            return SchemaBoundView.GetMethod(reader)!.Invoke(view, [key]);
        }
        catch (TargetInvocationException invocation) when (invocation.InnerException is not null)
        {
            throw invocation.InnerException;
        }
    }

    [Fact]
    public void SchemaBoundReader_RefusesAReadTheSchemaDoesNotDeclare_OrDeclaresAsAnotherType()
    {
        var equity = View("Equity", """{"shareClass":"B","convertibleTerms":{"conversionRatio":4.2},"preferredTerms":{"dividendRate":6.25}}""");

        FluentActions.Invoking(() => Read(equity, "OptionalString", "notDeclared"))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("Codec/schema drift: the codec reads 'Equity.notDeclared', which the schema does not declare.");
        FluentActions.Invoking(() => Read(equity, "OptionalDecimal", "shareClass"))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("Codec/schema drift: 'Equity.shareClass' is declared String but the codec reads it as Decimal.");
        Read(equity, "OptionalString", "shareClass").Should().Be("B");

        // A nested object is bound to the inner shape the schema registers for its path.
        var convertible = Read(equity, "RequiredObject", "convertibleTerms")!;
        Read(convertible, "RequiredDecimal", "conversionRatio").Should().Be(4.2m);
        FluentActions.Invoking(() => Read(convertible, "OptionalString", "conversionRatio"))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("Codec/schema drift: 'Equity.convertibleTerms.conversionRatio' is declared Decimal but the codec reads it as String.");

        // preferredTerms has no registered inner shape, which is why its members are ledgered:
        // read as bound, they are drift.
        var preferred = Read(equity, "RequiredObject", "preferredTerms")!;
        FluentActions.Invoking(() => Read(preferred, "OptionalDecimal", "dividendRate"))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("Codec/schema drift: the codec reads 'Equity.preferredTerms.dividendRate', which the schema does not declare.");
        Read(Unbound(preferred), "OptionalDecimal", "dividendRate").Should().Be(6.25m);
    }

    [Fact]
    public void UnboundView_RefusesAReadThatIsNotLedgered_OrHasBecomeBindable()
    {
        var customAsset = Unbound(View("CustomAsset", """{"maturity":"2031-03-15","category":"LegacyCustom"}"""));

        Read(customAsset, "OptionalDateOnly", "maturity").Should().Be(new DateOnly(2031, 3, 15));
        FluentActions.Invoking(() => Read(customAsset, "OptionalString", "notLedgered"))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("Codec/schema drift: the codec reads 'CustomAsset.notLedgered' as String outside the schema, and UnboundCompatibilityReads does not ledger it.");
        FluentActions.Invoking(() => Read(customAsset, "OptionalString", "maturity"))
            .Should().Throw<InvalidOperationException>("the ledger commits the type a read is made as, not just its key")
            .WithMessage("*'CustomAsset.maturity' as String outside the schema*does not ledger it.");
        FluentActions.Invoking(() => Read(customAsset, "OptionalString", "category"))
            .Should().Throw<InvalidOperationException>("a read the schema declares as read must be bound, not ledgered")
            .WithMessage("Codec/schema drift: 'CustomAsset.category' is read as an unbound compatibility read, but the schema declares it String; bind the read and retire its UnboundCompatibilityReads entry.");
    }
}
