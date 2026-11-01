using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Xunit;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Guards ADR-024 decision 3 — a per-class terms evolution adds a terms version, ships an upcaster,
/// and is readable on every node before any node writes it — against committed per-class version
/// manifests under <c>tests/fixtures/security-master/terms-versions</c>.
/// <para>A manifest records, for every terms version of one class up to the newest it READS
/// (<see cref="SecurityAssetClassDescriptor.ReadsTermsThrough"/>), the top-level flat keys with their
/// <see cref="SecurityAssetTermFieldType"/> and whether they are required. The committed manifests are
/// the authoritative history: versions are append-only and the live schema is compared only against
/// the latest one. A version above the write version is reached only through identity steps. Each
/// field's <see cref="SecurityAssetTermField.SinceTermsVersion"/> must be the first version that lists
/// it, each declared upgrade step must be legal for the manifest diff it spans, and the steps must be
/// contiguous.</para>
/// <para>To expand a class, raise its ReadableTermsVersion to n, tag each new field <c>.Since(n)</c> and
/// declare the identity step from n-1. A class may read more than one version ahead of the one it
/// writes: Bond writes version 1 and reads version 2 (the scheduleConventions block) and version 3 (the
/// callSchedule and putSchedule arrays). Each readable-only version is reached through its own identity
/// step and introduces its own fields, and the contract step later raises TermsVersion to meet it.</para>
/// <para>A missing manifest, or one without the class's newest version, writes
/// <c>&lt;AssetClass&gt;.received.json</c> beside the test binaries and fails. The received file holds
/// the committed versions unchanged, followed by each missing version derived from the live schema
/// and the fields' <c>.Since(n)</c> tags. Commit it as <c>&lt;AssetClass&gt;.json</c> only after review.
/// Element shapes (a swap's legs), aliases, vocabularies and conventions are outside the manifests;
/// the shapes of versioned containers are frozen by <c>versioned-containers.json</c> under
/// <c>tests/fixtures/security-master/terms-element-shapes</c>. The term-schema round-trip suites guard
/// the rest, and the element-field guard below keeps a versioned element field inside the readable
/// range.</para>
/// </summary>
[Trait("Category", "Unit")]
public sealed class SecurityAssetClassTermsHistoryTests
{
    private const string ManifestFolder = "terms-versions";
    private const string UpgradeFixtureFolder = "terms-upgrades";
    private const string ElementShapeFolder = "terms-element-shapes";
    private const string VersionedContainersFile = "versioned-containers";
    private const string ReceivedSuffix = ".received.json";
    private const string RepositoryManifestPath = "tests/fixtures/security-master/terms-versions";
    private const string RepositoryUpgradeFixturePath = "tests/fixtures/security-master/terms-upgrades";
    private const string RepositoryElementShapePath = "tests/fixtures/security-master/terms-element-shapes";

    private static readonly JsonSerializerOptions CanonicalJson = new()
    {
        WriteIndented = true,
        IndentSize = 2,
        // JsonSerializerOptions.NewLine defaults to Environment.NewLine; the manifests are LF.
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly Regex RetiredTermsVersionRead = new(
        @"SecurityAssetClassCatalog\s*\.\s*GetOrDefault\s*\([^()]*\)\s*\.\s*(TermsVersion|ReadableTermsVersion|ReadsTermsThrough)\b",
        RegexOptions.CultureInvariant);

    private const string ExpandThenContract =
        "To add a term, raise the class's ReadableTermsVersion to n, tag the field .Since(n), declare the identity step from n-1 in " +
        "TermsUpgrades, and let EveryCatalogClass_HasACommittedManifest_ForVersionsOneThroughWhatItReads append version n. Once " +
        "every node reads version n, a contract step raises TermsVersion to n. Never edit a committed version";

    private static readonly string[] SkippedPathSegments = ["/bin/", "/obj/", "/node_modules/", "/wwwroot/"];

    // ── Manifests ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void EveryCatalogClass_HasACommittedManifest_ForVersionsOneThroughWhatItReads()
    {
        var directory = LocateFixtureDirectory(ManifestFolder);
        var failures = new List<string>();
        var received = new List<string>();

        foreach (var assetClass in SecurityAssetClassCatalog.AssetClasses)
        {
            var termsVersion = SecurityAssetClassCatalog.GetReadsTermsThrough(assetClass);
            var path = directory is null ? null : Path.Combine(directory, assetClass + ".json");
            if (path is null || !File.Exists(path))
            {
                failures.Add($"{assetClass}: no committed manifest.");
                received.Add(WriteReceived(assetClass, committed: null, termsVersion));
                continue;
            }

            TermsVersionManifest manifest;
            try
            {
                manifest = ParseManifest(File.ReadAllText(path));
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
            {
                failures.Add($"{assetClass}: the manifest cannot be read ({exception.Message}).");
                continue;
            }

            if (!string.Equals(manifest.AssetClass, assetClass, StringComparison.Ordinal))
            {
                failures.Add($"{assetClass}: the manifest names asset class '{manifest.AssetClass}'; it must name its file's canonical class.");
            }

            var versions = manifest.Versions.Select(static version => version.TermsVersion).ToArray();
            if (versions.SequenceEqual(Enumerable.Range(1, termsVersion)))
            {
                continue;
            }

            if (versions.Length < termsVersion && versions.SequenceEqual(Enumerable.Range(1, versions.Length)))
            {
                failures.Add($"{assetClass}: the manifest records versions 1 to {versions.Length}, but the class reads terms versions 1 through {termsVersion}.");
                received.Add(WriteReceived(assetClass, manifest, termsVersion));
                continue;
            }

            failures.Add(
                $"{assetClass}: the manifest records versions [{string.Join(", ", versions)}]; it must record exactly 1 to {termsVersion}, " +
                "ascending with no gaps. Committed versions are append-only history and are never edited or removed.");
        }

        if (directory is not null)
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
            {
                var name = Path.GetFileName(file);
                if (name.EndsWith(ReceivedSuffix, StringComparison.Ordinal))
                {
                    continue;
                }

                var stem = Path.GetFileNameWithoutExtension(file);
                if (!SecurityAssetClassCatalog.AssetClasses.Contains(stem, StringComparer.Ordinal))
                {
                    failures.Add($"{name}: names no catalog asset class; a manifest file is named by its class's canonical name.");
                }
            }
        }

        failures.Should().BeEmpty(
            "every catalog class needs a committed terms-version manifest for every version it reads, 1 to its ReadsTermsThrough" +
            (received.Count == 0
                ? string.Empty
                : $". Received manifests were written to {string.Join(", ", received)}; review them and copy each to " +
                  $"{RepositoryManifestPath}/<AssetClass>.json"));
    }

    [Fact]
    public void LatestManifest_EqualsTheLiveSchema()
    {
        var failures = new List<string>();
        foreach (var (assetClass, manifest) in LoadCommittedManifests())
        {
            var termsVersion = SecurityAssetClassCatalog.GetReadsTermsThrough(assetClass);
            var latest = manifest.Versions.SingleOrDefault(version => version.TermsVersion == termsVersion);
            if (latest is null)
            {
                failures.Add($"{assetClass}: the manifest has no version {termsVersion} (see EveryCatalogClass_HasACommittedManifest_ForVersionsOneThroughWhatItReads).");
                continue;
            }

            var committed = latest.Fields.Select(Describe).ToHashSet(StringComparer.Ordinal);
            var live = LiveVersion(assetClass, termsVersion).Fields.Select(Describe).ToHashSet(StringComparer.Ordinal);
            if (committed.SetEquals(live))
            {
                continue;
            }

            failures.Add(
                $"{assetClass} version {termsVersion}: only in the schema [{string.Join(", ", live.Except(committed).Order(StringComparer.Ordinal))}], " +
                $"only in the manifest [{string.Join(", ", committed.Except(live).Order(StringComparer.Ordinal))}].");
        }

        failures.Should().BeEmpty(
            "the live SecurityAssetTermsSchema must equal the latest committed manifest version, the newest version the class reads. " +
            ExpandThenContract);
    }

    [Fact]
    public void EachFieldsSinceTermsVersion_IsTheFirstManifestVersionListingIt()
    {
        var failures = new List<string>();
        foreach (var (assetClass, manifest) in LoadCommittedManifests())
        {
            var termsVersion = SecurityAssetClassCatalog.GetReadsTermsThrough(assetClass);
            foreach (var field in SecurityAssetTermsSchema.Fields(assetClass))
            {
                if (field.SinceTermsVersion > termsVersion)
                {
                    failures.Add($"{assetClass}.{field.Key} is tagged .Since({field.SinceTermsVersion}) but the class reads through version {termsVersion}.");
                    continue;
                }

                var first = manifest.Versions
                    .Where(version => version.Fields.Any(entry => string.Equals(entry.Key, field.Key, StringComparison.Ordinal)))
                    .Select(static version => (int?)version.TermsVersion)
                    .Min();
                if (first != field.SinceTermsVersion)
                {
                    failures.Add(
                        $"{assetClass}.{field.Key} is tagged .Since({field.SinceTermsVersion}) but first appears in manifest version " +
                        $"{(first is null ? "none" : first.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))}.");
                }
            }
        }

        failures.Should().BeEmpty("a field's SinceTermsVersion is the terms version that introduced it, as the committed manifests record");
    }

    [Fact]
    public void StructuredCreditVersions_TwoAddsTheTrancheTerms_ThreeTheBlock_FourTheSchedules()
    {
        var manifest = LoadCommittedManifests().Should().ContainKey("StructuredCredit").WhoseValue;

        manifest.Versions.Select(static version => version.TermsVersion).Should().Equal(1, 2, 3, 4, 5);
        manifest.Versions[0].Fields.Select(Describe).Should().Equal(
            "collateralType:String:required",
            "couponOrIndex:String:required",
            "currentFactor:Decimal:optional",
            "factorSchedule:String:optional",
            "factorScheduleEntries:Array:optional",
            "maturity:Date:optional",
            "originalFace:Decimal:required",
            "poolId:String:optional",
            "tranche:String:required");
        manifest.Versions[1].Fields.Select(Describe).Except(manifest.Versions[0].Fields.Select(Describe)).Should().BeEquivalentTo(
        new[]
        {
            "attachmentPoint:Decimal:optional",
            "detachmentPoint:Decimal:optional",
            "nonCallPeriodEnd:Date:optional",
            "reinvestmentPeriodEnd:Date:optional",
            "seniority:String:optional",
            "structuredCreditKind:String:optional",
            "weightedAverageCoupon:Decimal:optional",
            "weightedAverageLoanAgeMonths:Integer:optional",
            "weightedAverageMaturityMonths:Integer:optional",
        });
        manifest.Versions[1].Fields.Should().HaveCount(18);
        manifest.Versions[2].Fields.Select(Describe).Except(manifest.Versions[1].Fields.Select(Describe))
            .Should().Equal("scheduleConventions:Object:optional");
        manifest.Versions[1].Fields.Select(Describe).Except(manifest.Versions[2].Fields.Select(Describe))
            .Should().BeEmpty("version 3 adds the schedule conventions block and changes nothing version 2 declares");
        manifest.Versions[3].Fields.Select(Describe).Except(manifest.Versions[2].Fields.Select(Describe))
            .Should().Equal("callSchedule:Array:optional", "putSchedule:Array:optional");
        manifest.Versions[2].Fields.Select(Describe).Except(manifest.Versions[3].Fields.Select(Describe))
            .Should().BeEmpty("version 4 adds the call and put schedules and changes nothing version 3 declares");
    }

    [Theory]
    [InlineData("Bond")]
    [InlineData("CertificateOfDeposit")]
    [InlineData("Swap")]
    public void VersionTwo_AddsOnlyTheScheduleConventionsBlock(string assetClass)
    {
        var manifest = LoadCommittedManifests().Should().ContainKey(assetClass).WhoseValue;

        // A bond and a certificate of deposit go on to read version 3 (VersionThree_AddsOnlyTheCallAndPutSchedules).
        manifest.Versions.Select(static version => version.TermsVersion)
            .Should().Equal(assetClass == "Swap" ? new[] { 1, 2, 3 } : new[] { 1, 2, 3, 4 });
        var added = manifest.Versions[1].Fields.Select(Describe).Except(manifest.Versions[0].Fields.Select(Describe)).ToArray();
        var removed = manifest.Versions[0].Fields.Select(Describe).Except(manifest.Versions[1].Fields.Select(Describe)).ToArray();
        removed.Should().BeEmpty();
        if (assetClass == "Swap")
        {
            // A swap adds its block to each leg, an element shape the top-level manifest does not
            // record; ElementFields_SinceIsWithinWhatTheClassReads_AndOptionalAboveOne covers it.
            added.Should().BeEmpty();
            SecurityAssetTermsSchema.ElementFields(assetClass, "legs")
                .Should().ContainSingle(static field => field.Key == "scheduleConventions" && field.SinceTermsVersion == 2);
        }
        else
        {
            added.Should().Equal("scheduleConventions:Object:optional");
        }
    }

    [Theory]
    [InlineData("Bond")]
    [InlineData("CertificateOfDeposit")]
    public void VersionThree_AddsOnlyTheCallAndPutSchedules(string assetClass)
    {
        var manifest = LoadCommittedManifests().Should().ContainKey(assetClass).WhoseValue;

        manifest.Versions.Select(static version => version.TermsVersion).Should().Equal(1, 2, 3, 4);
        manifest.Versions[2].Fields.Select(Describe).Except(manifest.Versions[1].Fields.Select(Describe))
            .Should().Equal("callSchedule:Array:optional", "putSchedule:Array:optional");
        manifest.Versions[1].Fields.Select(Describe).Except(manifest.Versions[2].Fields.Select(Describe))
            .Should().BeEmpty("version 3 adds the call and put schedules and changes nothing version 2 declares");
    }

    [Theory]
    [InlineData("Bond", 4)]
    [InlineData("CertificateOfDeposit", 4)]
    [InlineData("StructuredCredit", 5)]
    [InlineData("Swap", 3)]
    [InlineData("DirectLoan", 2)]
    public void ContractualCompanion_AddsOnlyAnOptionalObject_WithoutChangingEarlierTerms(string assetClass, int version)
    {
        var manifest = LoadCommittedManifests().Should().ContainKey(assetClass).WhoseValue;
        var before = manifest.Versions[version - 2].Fields.Select(Describe).ToArray();
        var after = manifest.Versions[version - 1].Fields.Select(Describe).ToArray();
        after.Except(before).Should().Equal("cashFlowContract:Object:optional");
        before.Except(after).Should().BeEmpty();
        SecurityAssetClassCatalog.GetReadsTermsThrough(assetClass).Should().Be(version);
        SecurityAssetClassCatalog.GetTermsVersion(assetClass).Should().BeLessThan(version);
    }

    [Fact]
    public void CommittedManifests_AreCanonical()
    {
        var directory = LocateFixtureDirectory(ManifestFolder);
        directory.Should().NotBeNull($"the committed manifests live under {RepositoryManifestPath}");

        foreach (var file in Directory.EnumerateFiles(directory!, "*.json").Where(static file => !file.EndsWith(ReceivedSuffix, StringComparison.Ordinal)))
        {
            // .gitattributes declares every file text=auto, so a Windows checkout may carry CRLF.
            var committed = File.ReadAllText(file).Replace("\r\n", "\n", StringComparison.Ordinal);
            WriteManifest(ParseManifest(committed)).Should().Be(
                committed,
                $"{Path.GetFileName(file)} must be in canonical form: properties in schema order, versions ascending, fields ordered by key, " +
                "two-space indentation, LF line endings and a trailing newline");
        }
    }

    // ── Upgrade steps ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void UpgradeSteps_AreContiguous_AndRationalesAreText()
    {
        foreach (var assetClass in SecurityAssetClassCatalog.AssetClasses)
        {
            var descriptor = SecurityAssetClassCatalog.GetOrDefault(assetClass);
            var steps = descriptor.TermsUpgrades ?? [];

            steps.Select(static step => step.FromVersion).Should().Equal(
                Enumerable.Range(1, descriptor.ReadsTermsThrough - 1),
                $"{assetClass} reads terms versions 1 through {descriptor.ReadsTermsThrough} and must declare exactly one step from each " +
                $"version 1 to {descriptor.ReadsTermsThrough - 1}, in ascending order (none while it reads only version 1)");
            steps.Should().OnlyContain(step => !string.IsNullOrWhiteSpace(step.Rationale));
            steps.Should().OnlyContain(step => (step.Kind == SecurityTermsUpgradeKind.Transform) == (step.Transform != null));
        }

        var structuredCredit = SecurityAssetClassCatalog.GetOrDefault("StructuredCredit").TermsUpgrades;
        structuredCredit.Should().HaveCount(4);
        structuredCredit!.Select(static step => (step.FromVersion, step.ToVersion)).Should().Equal((1, 2), (2, 3), (3, 4), (4, 5));
        structuredCredit.Should().OnlyContain(static step => step.Kind == SecurityTermsUpgradeKind.Identity && step.Transform == null);

        foreach (var assetClass in new[] { "Bond", "CertificateOfDeposit" })
        {
            var steps = SecurityAssetClassCatalog.GetOrDefault(assetClass).TermsUpgrades;
            steps.Should().HaveCount(3, $"{assetClass} reads terms versions 1 through 4");
            steps!.Select(static step => (step.FromVersion, step.ToVersion)).Should().Equal((1, 2), (2, 3), (3, 4));
            steps.Should().OnlyContain(static step => step.Kind == SecurityTermsUpgradeKind.Identity && step.Transform == null);
        }
    }

    [Fact]
    public void StepsAtOrAboveTheWriteVersion_AreIdentity()
    {
        foreach (var assetClass in SecurityAssetClassCatalog.AssetClasses)
        {
            var descriptor = SecurityAssetClassCatalog.GetOrDefault(assetClass);
            (descriptor.TermsUpgrades ?? [])
                .Where(step => step.FromVersion >= descriptor.TermsVersion)
                .Should().OnlyContain(
                    static step => step.Kind == SecurityTermsUpgradeKind.Identity && step.Transform == null,
                    $"{assetClass} writes version {descriptor.TermsVersion}; a version it only reads is reached through identity steps, because " +
                    "the upgrader brings payloads up to the write version and never runs a step above it");
        }
    }

    [Fact]
    public void ReadableTermsVersion_IsNullOrAboveTheWriteVersion()
    {
        foreach (var assetClass in SecurityAssetClassCatalog.AssetClasses)
        {
            var descriptor = SecurityAssetClassCatalog.GetOrDefault(assetClass);
            if (descriptor.ReadableTermsVersion is { } readable)
            {
                readable.Should().BeGreaterThan(
                    descriptor.TermsVersion,
                    $"{assetClass}'s ReadableTermsVersion names a version it reads ahead of writing; at or below the write version it must be null");
            }

            SecurityAssetClassCatalog.GetReadsTermsThrough(assetClass).Should().BeGreaterThanOrEqualTo(SecurityAssetClassCatalog.GetTermsVersion(assetClass));
        }
    }

    [Fact]
    public void EachReadableOnlyVersion_IntroducesATopLevelOrElementField()
    {
        foreach (var assetClass in SecurityAssetClassCatalog.AssetClasses)
        {
            var descriptor = SecurityAssetClassCatalog.GetOrDefault(assetClass);
            for (var version = descriptor.TermsVersion + 1; version <= descriptor.ReadsTermsThrough; version++)
            {
                var introduced = SecurityAssetTermsSchema.Fields(assetClass).Any(field => field.SinceTermsVersion == version)
                                 || ElementShapesOf(assetClass).SelectMany(static shape => shape.Fields).Any(field => field.SinceTermsVersion == version);
                introduced.Should().BeTrue($"{assetClass} reads version {version}, so some top-level or element field must be tagged .Since({version})");
            }
        }
    }

    [Fact]
    public void ElementFields_SinceIsWithinWhatTheClassReads_AndOptionalAboveOne()
    {
        var failures = new List<string>();
        foreach (var assetClass in SecurityAssetClassCatalog.AssetClasses)
        {
            var reads = SecurityAssetClassCatalog.GetReadsTermsThrough(assetClass);
            foreach (var (path, container, fields) in ElementShapesOf(assetClass))
            {
                foreach (var field in fields)
                {
                    if (field.SinceTermsVersion > reads)
                    {
                        failures.Add($"{path}.{field.Key} is tagged .Since({field.SinceTermsVersion}) but {assetClass} reads through version {reads}.");
                    }

                    if (field.SinceTermsVersion > SecurityTermsEnvelope.DefaultAssetClassTermsVersion && field.Required)
                    {
                        failures.Add($"{path}.{field.Key} is introduced at version {field.SinceTermsVersion} and so must be optional.");
                    }

                    // A versioned container carries the version itself; its members cannot be versioned
                    // separately, so a member added later needs a new container version instead.
                    if (container.SinceTermsVersion > SecurityTermsEnvelope.DefaultAssetClassTermsVersion
                        && (field.SinceTermsVersion != SecurityTermsEnvelope.DefaultAssetClassTermsVersion || field.Required))
                    {
                        failures.Add($"{path}.{field.Key} sits in a container introduced at version {container.SinceTermsVersion}, so it must be optional and untagged.");
                    }
                }
            }
        }

        failures.Should().BeEmpty("an element field above version 1 is optional and within the versions the class reads");
    }

    [Fact]
    public void VersionedContainerShapes_AreFrozenOnceReadable()
    {
        // A container introduced above version 1 carries the version itself and its members are
        // untagged (the element-field guard above), so nothing versions a member added to it later:
        // one node would read it at that version and another drop it. The committed fixture therefore
        // freezes every such container's members, in order, once readable.
        var live = new SortedDictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var assetClass in SecurityAssetClassCatalog.AssetClasses)
        {
            foreach (var (path, container, fields) in ElementShapesOf(assetClass))
            {
                if (container.SinceTermsVersion > SecurityTermsEnvelope.DefaultAssetClassTermsVersion)
                {
                    live[path] = DescribeVersionedContainer(path, container, fields);
                }
            }
        }

        var directory = LocateFixtureDirectory(ElementShapeFolder);
        var fixturePath = directory is null ? null : Path.Combine(directory, VersionedContainersFile + ".json");
        var committed = new List<JsonObject>();
        var failures = new List<string>();
        if (fixturePath is null || !File.Exists(fixturePath))
        {
            failures.Add($"no committed {VersionedContainersFile}.json.");
        }
        else
        {
            committed = (JsonNode.Parse(File.ReadAllText(fixturePath))?["containers"]?.AsArray()
                         ?? throw new InvalidOperationException($"{VersionedContainersFile}.json has no containers array."))
                .Select(static entry => entry!.AsObject())
                .ToList();
        }

        var committedPaths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in committed)
        {
            var path = entry["path"]!.GetValue<string>();
            if (!committedPaths.Add(path))
            {
                failures.Add($"{path}: committed twice.");
                continue;
            }

            if (!live.TryGetValue(path, out var current))
            {
                failures.Add($"{path}: committed, but no longer registered as a container introduced above version 1.");
                continue;
            }

            if (!JsonNode.DeepEquals(entry["containerSince"], current["containerSince"]))
            {
                failures.Add($"{path}: committed with containerSince {entry["containerSince"]?.ToJsonString()}, but the live container is introduced at {current["containerSince"]!.ToJsonString()}.");
            }

            var frozen = entry["members"]!.AsArray();
            var members = current["members"]!.AsArray();
            if (frozen.Count == members.Count && frozen.Zip(members).All(static pair => JsonNode.DeepEquals(pair.First, pair.Second)))
            {
                continue;
            }

            var index = Enumerable.Range(0, Math.Min(frozen.Count, members.Count))
                .FirstOrDefault(position => !JsonNode.DeepEquals(frozen[position], members[position]), Math.Min(frozen.Count, members.Count));
            failures.Add(
                $"{path}: the live members [{MemberKeys(members)}] differ from the frozen [{MemberKeys(frozen)}], first at member {index}: " +
                $"frozen {(index < frozen.Count ? frozen[index]!.ToJsonString() : "none")}, live {(index < members.Count ? members[index]!.ToJsonString() : "none")}.");
        }

        var missing = live.Keys.Where(path => !committedPaths.Contains(path)).ToArray();
        string? received = null;
        if (missing.Length > 0)
        {
            failures.Add($"[{string.Join(", ", missing)}]: registered above version 1 with no committed shape.");
            received = WriteReceivedContainerShapes(committed, missing.Select(path => live[path]));
        }

        failures.Should().BeEmpty(
            "the member shape of a readable versioned container is append-only history: a committed entry is never edited or removed, " +
            "and a member added later needs a new container key at a new readable version (ADR-024 decision 3)" +
            (received is null
                ? string.Empty
                : $". The received shapes were written to {received}; review them and copy the file to {RepositoryElementShapePath}/{VersionedContainersFile}.json"));
    }

    // One frozen-shape entry: the container's path and version, and every member as the guard compares it.
    private static JsonObject DescribeVersionedContainer(string path, SecurityAssetTermField container, IReadOnlyList<SecurityAssetTermField> fields)
    {
        var members = new JsonArray();
        foreach (var field in fields)
        {
            members.Add(new JsonObject
            {
                ["key"] = field.Key,
                ["type"] = field.Type.ToString(),
                ["required"] = field.Required,
                ["since"] = field.SinceTermsVersion,
                ["aliases"] = new JsonArray(field.Aliases.Select(static alias => (JsonNode?)alias).ToArray()),
                ["allowedValues"] = new JsonArray(field.AllowedValues.Select(static value => (JsonNode?)value).ToArray()),
                ["carriesUndeclaredValueVerbatim"] = field.CarriesUndeclaredValueVerbatim,
                ["convention"] = field.Convention.ToString(),
                ["underlyingRole"] = field.UnderlyingRole?.ToString(),
            });
        }

        return new JsonObject
        {
            ["path"] = path,
            ["containerSince"] = container.SinceTermsVersion,
            ["members"] = members,
        };
    }

    private static string MemberKeys(JsonArray members)
        => string.Join(", ", members.Select(static member => member?["key"]?.GetValue<string>()));

    /// <summary>
    /// Writes the received frozen shapes: the committed entries unchanged, then each missing live
    /// container in path order. Committed entries are never recomputed.
    /// </summary>
    private static string WriteReceivedContainerShapes(IReadOnlyList<JsonObject> committed, IEnumerable<JsonObject> missing)
    {
        var containers = new JsonArray();
        foreach (var entry in committed.Concat(missing))
        {
            containers.Add(entry.DeepClone());
        }

        var directory = Path.Combine(AppContext.BaseDirectory, "fixtures", "security-master", ElementShapeFolder);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, VersionedContainersFile + ReceivedSuffix);
        File.WriteAllText(path, new JsonObject { ["containers"] = containers }.ToJsonString(CanonicalJson) + "\n");
        return path;
    }

    // Every registered inner shape of a class, with the container field that holds it.
    private static IEnumerable<(string Path, SecurityAssetTermField Container, IReadOnlyList<SecurityAssetTermField> Fields)> ElementShapesOf(string assetClass)
    {
        foreach (var path in SecurityAssetTermsSchema.ElementFieldPaths)
        {
            var segments = path.Split('.');
            if (!string.Equals(segments[0], assetClass, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var container = SecurityAssetTermsSchema.Field(assetClass, segments[1])
                ?? throw new InvalidOperationException($"{path}: {assetClass} declares no '{segments[1]}'.");
            for (var index = 2; index < segments.Length; index++)
            {
                var key = segments[index];
                container = SecurityAssetTermsSchema.ElementFields(assetClass, string.Join(".", segments[1..index]))
                    .FirstOrDefault(field => string.Equals(field.Key, key, StringComparison.Ordinal))
                    ?? throw new InvalidOperationException($"{path}: no element field '{key}'.");
            }

            yield return (path, container, SecurityAssetTermsSchema.ElementFields(assetClass, string.Join(".", segments[1..])));
        }
    }

    [Fact]
    public void IdentitySteps_AreLegal_AgainstTheManifests()
    {
        var manifests = LoadCommittedManifests();
        var failures = new List<string>();
        foreach (var assetClass in SecurityAssetClassCatalog.AssetClasses)
        {
            foreach (var step in (SecurityAssetClassCatalog.GetOrDefault(assetClass).TermsUpgrades ?? [])
                         .Where(static step => step.Kind == SecurityTermsUpgradeKind.Identity))
            {
                if (!manifests.TryGetValue(assetClass, out var manifest)
                    || manifest.Versions.SingleOrDefault(version => version.TermsVersion == step.FromVersion) is not { } from
                    || manifest.Versions.SingleOrDefault(version => version.TermsVersion == step.ToVersion) is not { } to)
                {
                    failures.Add($"{assetClass} {step.FromVersion}→{step.ToVersion}: the manifest does not record both versions.");
                    continue;
                }

                var violations = IdentityViolations(from, to);
                if (violations.Count > 0)
                {
                    failures.Add(
                        $"{assetClass} {step.FromVersion}→{step.ToVersion} is declared an identity step, but {string.Join("; ", violations)}. " +
                        $"Declare it SecurityAssetClassTermsUpgrade.Transforming(...) and commit {RepositoryUpgradeFixturePath}/{assetClass}/" +
                        $"v{step.FromVersion}.in.json and v{step.FromVersion}.out.json.");
                }
            }
        }

        failures.Should().BeEmpty(
            "an identity step is legal only when the next version removes or retypes nothing, makes no optional term required and adds only optional terms");
    }

    [Fact]
    public void TransformSteps_HaveFixturesThatMapInToOut_AndAreIdempotent()
    {
        var manifests = LoadCommittedManifests();
        var directory = LocateFixtureDirectory(UpgradeFixtureFolder);
        var failures = new List<string>();
        foreach (var assetClass in SecurityAssetClassCatalog.AssetClasses)
        {
            var descriptor = SecurityAssetClassCatalog.GetOrDefault(assetClass);
            var steps = descriptor.TermsUpgrades ?? [];
            foreach (var step in steps.Where(static step => step.Kind == SecurityTermsUpgradeKind.Transform))
            {
                var version = step.FromVersion;
                var inPath = directory is null ? null : Path.Combine(directory, assetClass, $"v{version}.in.json");
                var outPath = directory is null ? null : Path.Combine(directory, assetClass, $"v{version}.out.json");
                if (inPath is null || outPath is null || !File.Exists(inPath) || !File.Exists(outPath))
                {
                    failures.Add($"{assetClass} {version}→{version + 1}: commit {RepositoryUpgradeFixturePath}/{assetClass}/v{version}.in.json and v{version}.out.json.");
                    continue;
                }

                var input = Json(File.ReadAllText(inPath));
                var output = Json(File.ReadAllText(outPath));
                if (SecurityTermsEnvelope.ResolveAssetClassTermsVersion(input) != version)
                {
                    failures.Add($"{assetClass} v{version}.in.json must be stamped {SecurityTermsEnvelope.AssetClassTermsVersionKey} = {version}.");
                }

                var target = descriptor with
                {
                    TermsVersion = version + 1,
                    TermsUpgrades = steps.Where(candidate => candidate.FromVersion <= version).ToArray(),
                };
                if (!JsonElement.DeepEquals(SecurityAssetClassTermsUpgrader.UpgradeForRead(target, input), output))
                {
                    failures.Add($"{assetClass} {version}→{version + 1}: v{version}.in.json does not upgrade to v{version}.out.json.");
                }

                var restamped = Json(SecurityTermsEnvelope.StampFlat(output.GetRawText(), version));
                if (!JsonElement.DeepEquals(SecurityAssetClassTermsUpgrader.UpgradeForRead(target, restamped), output))
                {
                    failures.Add($"{assetClass} {version}→{version + 1}: the transform is not idempotent on a payload already in the version-{version + 1} shape.");
                }

                if (manifests.TryGetValue(assetClass, out var manifest))
                {
                    AddUndeclaredKeys(failures, assetClass, $"v{version}.in.json", input, manifest, version);
                    AddUndeclaredKeys(failures, assetClass, $"v{version}.out.json", output, manifest, version + 1);
                }
            }
        }

        failures.Should().BeEmpty("a transform step ships in and out fixtures that prove what it does on read");
    }

    [Fact]
    public void TransformFixtures_BelongToDeclaredTransformSteps()
    {
        var directory = LocateFixtureDirectory(UpgradeFixtureFolder);
        if (directory is null)
        {
            // No transform step exists yet, so no fixture directory does either.
            return;
        }

        var failures = new List<string>();
        foreach (var classDirectory in Directory.EnumerateDirectories(directory))
        {
            var assetClass = Path.GetFileName(classDirectory);
            if (!SecurityAssetClassCatalog.AssetClasses.Contains(assetClass, StringComparer.Ordinal))
            {
                failures.Add($"{assetClass}: names no catalog asset class.");
                continue;
            }

            var transforms = (SecurityAssetClassCatalog.GetOrDefault(assetClass).TermsUpgrades ?? [])
                .Where(static step => step.Kind == SecurityTermsUpgradeKind.Transform)
                .Select(static step => step.FromVersion)
                .ToHashSet();
            foreach (var file in Directory.EnumerateFiles(classDirectory, "*.json"))
            {
                var match = Regex.Match(Path.GetFileName(file), @"^v(?<version>\d+)\.(in|out)\.json$", RegexOptions.CultureInvariant);
                if (!match.Success || !transforms.Contains(int.Parse(match.Groups["version"].Value, System.Globalization.CultureInfo.InvariantCulture)))
                {
                    failures.Add($"{assetClass}/{Path.GetFileName(file)}: no declared transform step reads this fixture.");
                }
            }
        }

        failures.Should().BeEmpty("upgrade fixtures exist only for declared transform steps");
    }

    // ── Legality rules ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void IdentityLegality_RefusesRemovalRetypeRequiredAdditionAndTightening_AndAllowsOptionalAdditionsAndRelaxing()
    {
        var baseline = At(1, Field("a", "String", required: true), Field("b", "Decimal", required: false));

        IdentityViolations(baseline, At(2, Field("a", "String", required: true)))
            .Should().ContainSingle().Which.Should().Contain("'b' is removed");
        IdentityViolations(baseline, At(2, Field("a", "String", required: true), Field("b", "Decimal", required: false), Field("c", "Date", required: true)))
            .Should().ContainSingle().Which.Should().Contain("'c' is added as required");
        IdentityViolations(baseline, At(2, Field("a", "String", required: true), Field("b", "Integer", required: false)))
            .Should().ContainSingle().Which.Should().Contain("'b' is retyped from Decimal to Integer");
        IdentityViolations(baseline, At(2, Field("a", "String", required: true), Field("b", "Decimal", required: true)))
            .Should().ContainSingle().Which.Should().Contain("'b' becomes required");
        IdentityViolations(baseline, At(2, Field("a", "String", required: true), Field("renamed", "Decimal", required: false)))
            .Should().ContainSingle().Which.Should().Contain("'b' is removed", "a rename reads as a removal plus an addition");

        IdentityViolations(
                baseline,
                At(
                    2,
                    Field("a", "String", required: true),
                    Field("b", "Decimal", required: false),
                    Field("c", "Date", required: false),
                    Field("d", "Integer", required: false),
                    Field("e", "Array", required: false)))
            .Should().BeEmpty("adding optional terms is an identity");
        IdentityViolations(baseline, At(2, Field("a", "String", required: false), Field("b", "Decimal", required: false)))
            .Should().BeEmpty("relaxing a required term to optional reads every older payload unchanged");
    }

    // ── Accessor rule ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void NoSourceFile_ReadsTheTermsVersionOffTheCatalogDescriptor()
    {
        var root = FindRepositoryRoot();
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.*", SearchOption.AllDirectories))
        {
            if (Path.GetExtension(file) is not (".cs" or ".fs"))
            {
                continue;
            }

            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (SkippedPathSegments.Any(segment => ("/" + relative).Contains(segment, StringComparison.Ordinal)))
            {
                continue;
            }

            if (RetiredTermsVersionRead.IsMatch(File.ReadAllText(file)))
            {
                offenders.Add(relative);
            }
        }

        offenders.Should().BeEmpty(
            "the terms version a node writes is read through SecurityAssetClassCatalog.GetTermsVersion and the newest it reads through " +
            "GetReadsTermsThrough, so each has one accessor");
    }

    // ── Manifest model ──────────────────────────────────────────────────────────────────────────

    private sealed record ManifestField(string Key, string Type, bool Required);

    private sealed record ManifestVersion(int TermsVersion, IReadOnlyList<ManifestField> Fields);

    private sealed record TermsVersionManifest(string AssetClass, IReadOnlyList<ManifestVersion> Versions);

    private static ManifestField Field(string key, string type, bool required) => new(key, type, required);

    private static ManifestVersion At(int termsVersion, params ManifestField[] fields) => new(termsVersion, fields);

    private static string Describe(ManifestField field)
        => $"{field.Key}:{field.Type}:{(field.Required ? "required" : "optional")}";

    /// <summary>The live schema's fields that exist at <paramref name="termsVersion"/>, ordered by key.</summary>
    private static ManifestVersion LiveVersion(string assetClass, int termsVersion)
        => new(
            termsVersion,
            SecurityAssetTermsSchema.Fields(assetClass)
                .Where(field => field.SinceTermsVersion <= termsVersion)
                .Select(static field => new ManifestField(field.Key, field.Type.ToString(), field.Required))
                .OrderBy(static field => field.Key, StringComparer.Ordinal)
                .ToArray());

    /// <summary>
    /// Rules (a)–(d): nothing removed (a rename is a removal plus an addition), every added key
    /// optional, no surviving key retyped, and no optional key made required. Relaxing a required key
    /// is allowed. Defaults are not in the manifests, so a transform step is always permitted.
    /// </summary>
    private static IReadOnlyList<string> IdentityViolations(ManifestVersion from, ManifestVersion to)
    {
        var before = from.Fields.ToDictionary(static field => field.Key, StringComparer.Ordinal);
        var after = to.Fields.ToDictionary(static field => field.Key, StringComparer.Ordinal);
        var violations = new List<string>();
        foreach (var (key, field) in before.OrderBy(static entry => entry.Key, StringComparer.Ordinal))
        {
            if (!after.TryGetValue(key, out var next))
            {
                violations.Add($"'{key}' is removed");
                continue;
            }

            if (!string.Equals(field.Type, next.Type, StringComparison.Ordinal))
            {
                violations.Add($"'{key}' is retyped from {field.Type} to {next.Type}");
            }

            if (!field.Required && next.Required)
            {
                violations.Add($"'{key}' becomes required");
            }
        }

        foreach (var (key, next) in after.OrderBy(static entry => entry.Key, StringComparer.Ordinal))
        {
            if (!before.ContainsKey(key) && next.Required)
            {
                violations.Add($"'{key}' is added as required");
            }
        }

        return violations;
    }

    private static void AddUndeclaredKeys(
        List<string> failures,
        string assetClass,
        string fixture,
        JsonElement payload,
        TermsVersionManifest manifest,
        int termsVersion)
    {
        var declared = manifest.Versions.SingleOrDefault(version => version.TermsVersion == termsVersion)?.Fields
            .Select(static field => field.Key)
            .ToHashSet(StringComparer.Ordinal) ?? [];
        var undeclared = payload.EnumerateObject()
            .Select(static property => property.Name)
            .Where(name => !SecurityTermsEnvelope.IsReservedKey(name) && !declared.Contains(name))
            .ToArray();
        if (undeclared.Length > 0)
        {
            failures.Add($"{assetClass} {fixture} carries keys version {termsVersion} does not declare: {string.Join(", ", undeclared)}.");
        }
    }

    private static Dictionary<string, TermsVersionManifest> LoadCommittedManifests()
    {
        var directory = LocateFixtureDirectory(ManifestFolder);
        directory.Should().NotBeNull($"the committed manifests live under {RepositoryManifestPath}");

        var manifests = new Dictionary<string, TermsVersionManifest>(StringComparer.Ordinal);
        foreach (var assetClass in SecurityAssetClassCatalog.AssetClasses)
        {
            var path = Path.Combine(directory!, assetClass + ".json");
            File.Exists(path).Should().BeTrue(
                $"{assetClass} needs a committed manifest (see EveryCatalogClass_HasACommittedManifest_ForVersionsOneThroughWhatItReads)");
            manifests[assetClass] = ParseManifest(File.ReadAllText(path));
        }

        return manifests;
    }

    private static TermsVersionManifest ParseManifest(string json)
    {
        var root = JsonNode.Parse(json)?.AsObject() ?? throw new InvalidOperationException("The manifest is not a JSON object.");
        var versions = root["versions"]?.AsArray() ?? throw new InvalidOperationException("The manifest has no versions array.");
        return new TermsVersionManifest(
            root["assetClass"]?.GetValue<string>() ?? throw new InvalidOperationException("The manifest has no assetClass."),
            versions
                .Select(static version => new ManifestVersion(
                    version!["termsVersion"]!.GetValue<int>(),
                    version["fields"]!.AsArray()
                        .Select(static field => new ManifestField(
                            field!["key"]!.GetValue<string>(),
                            field["type"]!.GetValue<string>(),
                            field["required"]!.GetValue<bool>()))
                        .ToArray()))
                .ToArray());
    }

    private static string WriteManifest(TermsVersionManifest manifest)
    {
        var versions = new JsonArray();
        foreach (var version in manifest.Versions.OrderBy(static version => version.TermsVersion))
        {
            var fields = new JsonArray();
            foreach (var field in version.Fields.OrderBy(static field => field.Key, StringComparer.Ordinal))
            {
                fields.Add(new JsonObject
                {
                    ["key"] = field.Key,
                    ["type"] = field.Type,
                    ["required"] = field.Required,
                });
            }

            versions.Add(new JsonObject
            {
                ["termsVersion"] = version.TermsVersion,
                ["fields"] = fields,
            });
        }

        var root = new JsonObject
        {
            ["assetClass"] = manifest.AssetClass,
            ["versions"] = versions,
        };
        return root.ToJsonString(CanonicalJson) + "\n";
    }

    /// <summary>
    /// Writes the received manifest: the committed versions unchanged, then every version after the
    /// last committed one up to <paramref name="termsVersion"/>, derived from the live schema's
    /// <c>.Since(n)</c> tags. Committed versions are never recomputed.
    /// </summary>
    private static string WriteReceived(string assetClass, TermsVersionManifest? committed, int termsVersion)
    {
        var versions = committed?.Versions.ToList() ?? [];
        for (var version = versions.Count + 1; version <= termsVersion; version++)
        {
            versions.Add(LiveVersion(assetClass, version));
        }

        var directory = Path.Combine(AppContext.BaseDirectory, "fixtures", "security-master", ManifestFolder);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, assetClass + ReceivedSuffix);
        File.WriteAllText(path, WriteManifest(new TermsVersionManifest(assetClass, versions)));
        return path;
    }

    /// <summary>
    /// Prefers the fixtures copied beside the test binaries (csproj None item with
    /// CopyToOutputDirectory), falling back to a repository walk for ad hoc runs. A copied directory
    /// holding only received files does not count.
    /// </summary>
    private static string? LocateFixtureDirectory(string folder)
    {
        var copied = Path.Combine(AppContext.BaseDirectory, "fixtures", "security-master", folder);
        if (Directory.Exists(copied)
            && Directory.EnumerateFileSystemEntries(copied).Any(static entry => !entry.EndsWith(ReceivedSuffix, StringComparison.Ordinal)))
        {
            return copied;
        }

        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "tests", "fixtures", "security-master", folder);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Meridian.sln")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Unable to locate Meridian repository root from test output directory.");
    }

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();
}
