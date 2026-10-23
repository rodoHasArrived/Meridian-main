using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Xunit;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Guards ADR-024 decision 3 — a per-class terms evolution bumps the class's
/// <see cref="SecurityAssetClassDescriptor.TermsVersion"/> and ships an upcaster — against committed
/// per-class version manifests under <c>tests/fixtures/security-master/terms-versions</c>.
/// <para>A manifest records, for every terms version of one class, the top-level flat keys with their
/// <see cref="SecurityAssetTermFieldType"/> and whether they are required. The committed manifests are
/// the authoritative history: versions are append-only and the live schema is compared only against
/// the latest one. Each field's <see cref="SecurityAssetTermField.SinceTermsVersion"/> must be the
/// first version that lists it, each declared upgrade step must be legal for the manifest diff it
/// spans, and the steps must be contiguous.</para>
/// <para>A missing manifest, or one without the class's newest version, writes
/// <c>&lt;AssetClass&gt;.received.json</c> beside the test binaries and fails. The received file holds
/// the committed versions unchanged, followed by each missing version derived from the live schema
/// and the fields' <c>.Since(n)</c> tags. Commit it as <c>&lt;AssetClass&gt;.json</c> only after review.
/// Element shapes (a swap's legs), aliases, vocabularies and conventions are outside the manifests;
/// the term-schema round-trip suites guard those.</para>
/// </summary>
[Trait("Category", "Unit")]
public sealed class SecurityAssetClassTermsHistoryTests
{
    private const string ManifestFolder = "terms-versions";
    private const string UpgradeFixtureFolder = "terms-upgrades";
    private const string ReceivedSuffix = ".received.json";
    private const string RepositoryManifestPath = "tests/fixtures/security-master/terms-versions";
    private const string RepositoryUpgradeFixturePath = "tests/fixtures/security-master/terms-upgrades";

    private static readonly JsonSerializerOptions CanonicalJson = new()
    {
        WriteIndented = true,
        IndentSize = 2,
        // JsonSerializerOptions.NewLine defaults to Environment.NewLine; the manifests are LF.
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly Regex RetiredTermsVersionRead = new(
        @"SecurityAssetClassCatalog\s*\.\s*GetOrDefault\s*\([^()]*\)\s*\.\s*TermsVersion\b",
        RegexOptions.CultureInvariant);

    private static readonly string[] SkippedPathSegments = ["/bin/", "/obj/", "/node_modules/", "/wwwroot/"];

    // ── Manifests ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void EveryCatalogClass_HasACommittedManifest_ForVersionsOneThroughItsTermsVersion()
    {
        var directory = LocateFixtureDirectory(ManifestFolder);
        var failures = new List<string>();
        var received = new List<string>();

        foreach (var assetClass in SecurityAssetClassCatalog.AssetClasses)
        {
            var termsVersion = SecurityAssetClassCatalog.GetTermsVersion(assetClass);
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
                failures.Add($"{assetClass}: the manifest records versions 1 to {versions.Length}, but the class is at terms version {termsVersion}.");
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
            "every catalog class needs a committed terms-version manifest for versions 1 to its TermsVersion" +
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
            var termsVersion = SecurityAssetClassCatalog.GetTermsVersion(assetClass);
            var latest = manifest.Versions.SingleOrDefault(version => version.TermsVersion == termsVersion);
            if (latest is null)
            {
                failures.Add($"{assetClass}: the manifest has no version {termsVersion} (see EveryCatalogClass_HasACommittedManifest_ForVersionsOneThroughItsTermsVersion).");
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
            "the live SecurityAssetTermsSchema must equal the latest committed manifest version. To change a class's flat terms, bump " +
            "its TermsVersion to n+1, tag each new field .Since(n+1), declare the n→n+1 step in TermsUpgrades, and let " +
            "EveryCatalogClass_HasACommittedManifest_ForVersionsOneThroughItsTermsVersion append version n+1; never edit a committed version");
    }

    [Fact]
    public void EachFieldsSinceTermsVersion_IsTheFirstManifestVersionListingIt()
    {
        var failures = new List<string>();
        foreach (var (assetClass, manifest) in LoadCommittedManifests())
        {
            var termsVersion = SecurityAssetClassCatalog.GetTermsVersion(assetClass);
            foreach (var field in SecurityAssetTermsSchema.Fields(assetClass))
            {
                if (field.SinceTermsVersion > termsVersion)
                {
                    failures.Add($"{assetClass}.{field.Key} is tagged .Since({field.SinceTermsVersion}) but the class is at terms version {termsVersion}.");
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
    public void StructuredCreditVersionTwoFields_AreTheNineTypedTrancheTerms()
    {
        var manifest = LoadCommittedManifests().Should().ContainKey("StructuredCredit").WhoseValue;

        manifest.Versions.Select(static version => version.TermsVersion).Should().Equal(1, 2);
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
                Enumerable.Range(1, descriptor.TermsVersion - 1),
                $"{assetClass} is at terms version {descriptor.TermsVersion} and must declare exactly one step from each version 1 to " +
                $"{descriptor.TermsVersion - 1}, in ascending order (none while it is at version 1)");
            steps.Should().OnlyContain(step => !string.IsNullOrWhiteSpace(step.Rationale));
            steps.Should().OnlyContain(step => (step.Kind == SecurityTermsUpgradeKind.Transform) == (step.Transform != null));
        }

        var structuredCredit = SecurityAssetClassCatalog.GetOrDefault("StructuredCredit").TermsUpgrades;
        var identity = structuredCredit.Should().ContainSingle().Which;
        identity.FromVersion.Should().Be(1);
        identity.ToVersion.Should().Be(2);
        identity.Kind.Should().Be(SecurityTermsUpgradeKind.Identity);
        identity.Transform.Should().BeNull();
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
            "the terms version a node writes or accepts is read through SecurityAssetClassCatalog.GetTermsVersion, so it has one accessor");
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
                $"{assetClass} needs a committed manifest (see EveryCatalogClass_HasACommittedManifest_ForVersionsOneThroughItsTermsVersion)");
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
