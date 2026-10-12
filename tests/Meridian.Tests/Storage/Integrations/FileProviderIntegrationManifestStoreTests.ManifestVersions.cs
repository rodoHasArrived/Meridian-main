using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Contracts.Integrations;
using Meridian.Contracts.Integrity;
using Meridian.ProcessTestHelper;
using Meridian.Storage.Integrations;

namespace Meridian.Tests.Storage.Integrations;

public sealed partial class FileProviderIntegrationManifestStoreTests
{
    [Fact]
    public async Task SaveManifestVersionAsync_RetainsCandidateWithoutMakingItCurrentAfterRestart()
    {
        var candidate = CreateManifest() with { ManifestVersion = 7 };
        await new FileProviderIntegrationManifestStore(_testRoot).SaveManifestVersionAsync(candidate);

        var restarted = new FileProviderIntegrationManifestStore(_testRoot);
        (await restarted.GetManifestVersionAsync(candidate.ManifestId, 7)).Should().BeEquivalentTo(candidate);
        (await restarted.GetManifestAsync(candidate.ManifestId)).Should().BeNull();
        (await restarted.ListManifestsAsync()).Should().BeEmpty();

        (await restarted.CompareExchangeCurrentManifestAsync(
            candidate.ManifestId, null, ProviderIntegrationManifestIdentity.Create(candidate))).Should().BeTrue();
        (await restarted.GetManifestAsync(candidate.ManifestId)).Should().BeEquivalentTo(candidate);
    }

    [Fact]
    public async Task SaveManifestAsync_RequiresCompareExchangeToReplaceCurrentVersion()
    {
        var original = CreateManifest();
        var candidate = original with { ManifestVersion = 2, ChangeReason = "Mapping update" };
        var store = new FileProviderIntegrationManifestStore(_testRoot);
        await store.SaveManifestAsync(original);

        var saveWithoutExpectedVersion = () => store.SaveManifestAsync(candidate);

        await saveWithoutExpectedVersion.Should().ThrowAsync<InvalidOperationException>();
        (await store.GetManifestAsync(original.ManifestId)).Should().BeEquivalentTo(original);
    }

    [Fact]
    public async Task SaveManifestVersionAsync_RejectsChangedContentUnderAnExistingVersion()
    {
        var original = CreateManifest();
        var store = new FileProviderIntegrationManifestStore(_testRoot);
        await store.SaveManifestAsync(original);
        var before = await File.ReadAllTextAsync(ManifestVersionPath(original));
        var conflicting = original with { DisplayName = "Changed in place" };

        var saveVersion = () => store.SaveManifestVersionAsync(conflicting);
        var saveCurrent = () => store.SaveManifestAsync(conflicting);

        await saveVersion.Should().ThrowAsync<InvalidOperationException>();
        await saveCurrent.Should().ThrowAsync<InvalidOperationException>();
        (await File.ReadAllTextAsync(ManifestVersionPath(original))).Should().Be(before);
        (await store.GetManifestAsync(original.ManifestId)).Should().BeEquivalentTo(original);
        (await store.GetManifestVersionAsync(original.ManifestId, 1)).Should().BeEquivalentTo(original);
    }

    [Fact]
    public async Task SaveManifestVersionAsync_IdenticalRetryIgnoresDictionaryInsertionOrder()
    {
        var original = CreateManifest();
        original = original with
        {
            Auth = original.Auth with
            {
                Metadata = new Dictionary<string, string> { ["audience"] = "positions", ["region"] = "us" }
            }
        };
        var reordered = original with
        {
            Auth = original.Auth with
            {
                Metadata = new Dictionary<string, string> { ["region"] = "us", ["audience"] = "positions" }
            }
        };
        var store = new FileProviderIntegrationManifestStore(_testRoot);
        await store.SaveManifestAsync(original);
        var before = await File.ReadAllTextAsync(ManifestVersionPath(original));

        await new FileProviderIntegrationManifestStore(_testRoot).SaveManifestVersionAsync(reordered);
        await store.SaveManifestAsync(reordered);

        ProviderIntegrationManifestIdentity.Create(reordered).Should().Be(ProviderIntegrationManifestIdentity.Create(original));
        (await File.ReadAllTextAsync(ManifestVersionPath(original))).Should().Be(before);
    }

    [Fact]
    public async Task SaveManifestVersionAsync_ConcurrentConflictingWritersRetainExactlyOneSnapshot()
    {
        var original = CreateManifest();
        var conflicting = original with { ChangeReason = "Concurrent alternative" };
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = new[] { original, conflicting }.Select(async candidate =>
        {
            await gate.Task;
            var error = await Record.ExceptionAsync(() =>
                new FileProviderIntegrationManifestStore(_testRoot).SaveManifestVersionAsync(candidate));
            return (Candidate: candidate, Error: error);
        }).ToArray();

        gate.SetResult();
        var results = await Task.WhenAll(attempts);

        var winner = results.Should().ContainSingle(result => result.Error == null).Which.Candidate;
        results.Should().ContainSingle(result => result.Error is InvalidOperationException);
        var restarted = new FileProviderIntegrationManifestStore(_testRoot);
        (await restarted.GetManifestVersionAsync(original.ManifestId, 1)).Should().BeEquivalentTo(winner);
        (await restarted.GetManifestAsync(original.ManifestId)).Should().BeNull();
    }

    [Fact]
    public async Task SaveManifestVersionAsync_FreezesCallerCollectionsBeforeWaitingForTheLock()
    {
        var metadata = new Dictionary<string, string> { ["audience"] = "original" };
        var manifest = CreateManifest();
        manifest = manifest with { Auth = manifest.Auth with { Metadata = metadata } };
        var reference = ProviderIntegrationManifestIdentity.Create(manifest);
        var lockPath = Path.Combine(_testRoot, "_integrations", "manifest-locks",
            $"{Sha256Digest.ComputeUtf8(manifest.ManifestId)}.lock");
        Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
        var store = new FileProviderIntegrationManifestStore(_testRoot);
        Task save;
        await using (var heldLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            save = store.SaveManifestVersionAsync(manifest);
            metadata["audience"] = "mutated while waiting";
        }
        await save.WaitAsync(TimeSpan.FromSeconds(10));

        var retained = await store.GetManifestVersionAsync(manifest.ManifestId, manifest.ManifestVersion);

        retained.Should().NotBeNull();
        retained!.Auth.Metadata["audience"].Should().Be("original");
        ProviderIntegrationManifestIdentity.Create(retained).Should().Be(reference);
    }

    [Fact]
    public async Task CompareExchangeCurrentManifestAsync_ConcurrentInstancesAllowOnlyOneEditorToAdvance()
    {
        var original = CreateManifest();
        var first = original with { ManifestVersion = 2, ChangeReason = "First editor" };
        var second = original with { ManifestVersion = 3, ChangeReason = "Second editor" };
        var store = new FileProviderIntegrationManifestStore(_testRoot);
        await store.SaveManifestAsync(original);
        await store.SaveManifestVersionAsync(first);
        await store.SaveManifestVersionAsync(second);
        var expected = ProviderIntegrationManifestIdentity.Create(original);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = new[] { first, second }.Select(async candidate =>
        {
            await gate.Task;
            var advanced = await new FileProviderIntegrationManifestStore(_testRoot)
                .CompareExchangeCurrentManifestAsync(original.ManifestId, expected,
                    ProviderIntegrationManifestIdentity.Create(candidate));
            return (Candidate: candidate, Advanced: advanced);
        }).ToArray();

        gate.SetResult();
        var results = await Task.WhenAll(attempts);

        var winner = results.Should().ContainSingle(result => result.Advanced).Which.Candidate;
        results.Should().ContainSingle(result => !result.Advanced);
        var restarted = new FileProviderIntegrationManifestStore(_testRoot);
        (await restarted.GetManifestAsync(original.ManifestId)).Should().BeEquivalentTo(winner);
        foreach (var retained in new[] { original, first, second })
        {
            (await restarted.GetManifestVersionAsync(retained.ManifestId, retained.ManifestVersion))
                .Should().BeEquivalentTo(retained);
        }
    }

    [Fact]
    public async Task CompareExchangeCurrentManifestAsync_MismatchedExpectedDigestDoesNotAdvance()
    {
        var original = CreateManifest();
        var candidate = original with { ManifestVersion = 2 };
        var store = new FileProviderIntegrationManifestStore(_testRoot);
        await store.SaveManifestAsync(original);
        await store.SaveManifestVersionAsync(candidate);
        var stale = ProviderIntegrationManifestIdentity.Create(original) with { ContentDigest = new string('0', 64) };

        (await store.CompareExchangeCurrentManifestAsync(original.ManifestId, stale,
            ProviderIntegrationManifestIdentity.Create(candidate))).Should().BeFalse();

        (await store.GetManifestAsync(original.ManifestId)).Should().BeEquivalentTo(original);
    }

    [Fact]
    public async Task CompareExchangeCurrentManifestAsync_DoesNotRollBackToAnOlderVersion()
    {
        var original = CreateManifest();
        var current = original with { ManifestVersion = 2 };
        var store = new FileProviderIntegrationManifestStore(_testRoot);
        await store.SaveManifestVersionAsync(original);
        await store.SaveManifestAsync(current);

        var rollback = () => store.CompareExchangeCurrentManifestAsync(original.ManifestId,
            ProviderIntegrationManifestIdentity.Create(current), ProviderIntegrationManifestIdentity.Create(original));

        await rollback.Should().ThrowAsync<InvalidOperationException>();
        (await store.GetManifestAsync(original.ManifestId)).Should().BeEquivalentTo(current);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CompareExchangeCurrentManifestAsync_RejectsMissingOrWrongDigestCandidate(bool missing)
    {
        var original = CreateManifest();
        var candidate = original with { ManifestVersion = 2 };
        var store = new FileProviderIntegrationManifestStore(_testRoot);
        await store.SaveManifestAsync(original);
        var next = ProviderIntegrationManifestIdentity.Create(candidate);
        if (!missing)
        {
            await store.SaveManifestVersionAsync(candidate);
            next = next with { ContentDigest = new string('0', 64) };
        }

        var advance = () => store.CompareExchangeCurrentManifestAsync(original.ManifestId,
            ProviderIntegrationManifestIdentity.Create(original), next);

        await advance.Should().ThrowAsync<InvalidDataException>();
        (await store.GetManifestAsync(original.ManifestId)).Should().BeEquivalentTo(original);
    }

    [Fact]
    public async Task GetManifestAsync_MigratesOnlyTheSurvivingLegacyVersion()
    {
        var legacy = CreateManifest() with { ManifestVersion = 7 };
        await WriteLegacyManifestAsync(legacy);
        var store = new FileProviderIntegrationManifestStore(_testRoot);

        (await store.GetManifestAsync(legacy.ManifestId)).Should().BeEquivalentTo(legacy);

        var restarted = new FileProviderIntegrationManifestStore(_testRoot);
        (await restarted.GetManifestVersionAsync(legacy.ManifestId, 7)).Should().BeEquivalentTo(legacy);
        for (var missingVersion = 1; missingVersion < 7; missingVersion++)
        {
            (await restarted.GetManifestVersionAsync(legacy.ManifestId, missingVersion)).Should().BeNull();
        }
        (await restarted.ListManifestsAsync()).Should().ContainSingle().Which.Should().BeEquivalentTo(legacy);
        using var pointer = JsonDocument.Parse(await File.ReadAllTextAsync(ManifestPointerPath(legacy.ManifestId)));
        pointer.RootElement.GetProperty("formatVersion").GetInt32().Should().Be(1);
        pointer.RootElement.GetProperty("manifestReference").GetProperty("manifestVersion").GetInt32().Should().Be(7);
        Directory.GetFiles(Path.GetDirectoryName(ManifestVersionPath(legacy))!, "*.json").Should().ContainSingle();
    }

    [Fact]
    public async Task GetManifestAsync_ResumesLegacyMigrationWhenSnapshotWasAlreadyRetained()
    {
        var legacy = CreateManifest() with { ManifestVersion = 7 };
        await new FileProviderIntegrationManifestStore(_testRoot).SaveManifestVersionAsync(legacy);
        await WriteLegacyManifestAsync(legacy);

        var restarted = new FileProviderIntegrationManifestStore(_testRoot);

        (await restarted.GetManifestAsync(legacy.ManifestId)).Should().BeEquivalentTo(legacy);
        (await restarted.GetManifestVersionAsync(legacy.ManifestId, 7)).Should().BeEquivalentTo(legacy);
        Directory.GetFiles(Path.GetDirectoryName(ManifestVersionPath(legacy))!, "*.json").Should().ContainSingle();
    }

    [Fact]
    public async Task GetManifestAsync_ConflictingLegacyMigrationPreservesBothExistingFiles()
    {
        var retained = CreateManifest() with { ManifestVersion = 7 };
        await new FileProviderIntegrationManifestStore(_testRoot).SaveManifestVersionAsync(retained);
        var legacy = retained with { ChangeReason = "Conflicting surviving legacy content" };
        await WriteLegacyManifestAsync(legacy);
        var pointerBefore = await File.ReadAllTextAsync(ManifestPointerPath(legacy.ManifestId));
        var snapshotBefore = await File.ReadAllTextAsync(ManifestVersionPath(retained));

        var migrate = () => new FileProviderIntegrationManifestStore(_testRoot).GetManifestAsync(legacy.ManifestId);

        await migrate.Should().ThrowAsync<InvalidOperationException>();
        (await File.ReadAllTextAsync(ManifestPointerPath(legacy.ManifestId))).Should().Be(pointerBefore);
        (await File.ReadAllTextAsync(ManifestVersionPath(retained))).Should().Be(snapshotBefore);
    }

    [Theory]
    [InlineData("content")]
    [InlineData("digest")]
    public async Task GetManifestVersionAsync_RejectsTamperedSnapshot(string tamper)
    {
        var original = CreateManifest();
        var store = new FileProviderIntegrationManifestStore(_testRoot);
        await store.SaveManifestAsync(original);
        var path = ManifestVersionPath(original);
        var snapshot = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        if (tamper == "content")
        {
            snapshot["manifest"]!["displayName"] = "Tampered snapshot";
        }
        else
        {
            snapshot["manifestReference"]!["contentDigest"] = new string('0', 64);
        }
        await File.WriteAllTextAsync(path, snapshot.ToJsonString());
        var restarted = new FileProviderIntegrationManifestStore(_testRoot);

        var getVersion = () => restarted.GetManifestVersionAsync(original.ManifestId, 1);
        var getCurrent = () => restarted.GetManifestAsync(original.ManifestId);
        var list = () => restarted.ListManifestsAsync();

        await getVersion.Should().ThrowAsync<InvalidDataException>();
        await getCurrent.Should().ThrowAsync<InvalidDataException>();
        await list.Should().ThrowAsync<InvalidDataException>();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetManifestVersionAsync_OriginalHistoryRemainsReadableWhenCurrentIsCorrupt(bool corruptPointer)
    {
        var original = CreateManifest();
        var newer = original with { ManifestVersion = 2 };
        var store = new FileProviderIntegrationManifestStore(_testRoot);
        await store.SaveManifestAsync(original);
        await store.SaveManifestVersionAsync(newer);
        (await store.CompareExchangeCurrentManifestAsync(original.ManifestId,
            ProviderIntegrationManifestIdentity.Create(original), ProviderIntegrationManifestIdentity.Create(newer)))
            .Should().BeTrue();
        await File.WriteAllTextAsync(
            corruptPointer ? ManifestPointerPath(original.ManifestId) : ManifestVersionPath(newer), "{");

        var restarted = new FileProviderIntegrationManifestStore(_testRoot);

        (await restarted.GetManifestVersionAsync(original.ManifestId, original.ManifestVersion))
            .Should().BeEquivalentTo(original);
        var getCurrent = () => restarted.GetManifestAsync(original.ManifestId);
        await getCurrent.Should().ThrowAsync<InvalidDataException>();
    }

    [Theory]
    [InlineData("format")]
    [InlineData("digest")]
    [InlineData("identity")]
    [InlineData("missing-version")]
    [InlineData("malformed-json")]
    public async Task GetManifestAsync_RejectsInvalidPointerWithoutFallingBackToLatestSnapshot(string tamper)
    {
        var original = CreateManifest();
        var store = new FileProviderIntegrationManifestStore(_testRoot);
        await store.SaveManifestAsync(original);
        await store.SaveManifestVersionAsync(original with { ManifestVersion = 2 });
        var path = ManifestPointerPath(original.ManifestId);
        var pointer = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        switch (tamper)
        {
            case "format":
                pointer["formatVersion"] = 999;
                break;
            case "digest":
                pointer["manifestReference"]!["contentDigest"] = new string('0', 64);
                break;
            case "identity":
                pointer["manifestReference"]!["manifestId"] = "different-manifest";
                break;
            case "missing-version":
                pointer["manifestReference"]!["manifestVersion"] = 99;
                break;
        }
        await File.WriteAllTextAsync(path, tamper == "malformed-json" ? "{" : pointer.ToJsonString());
        var restarted = new FileProviderIntegrationManifestStore(_testRoot);

        var getCurrent = () => restarted.GetManifestAsync(original.ManifestId);
        var list = () => restarted.ListManifestsAsync();

        await getCurrent.Should().ThrowAsync<InvalidDataException>();
        await list.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CompareExchangeCurrentManifestAsync_LiveProcessLockDoesNotExpireAndCrashLeavesCurrentUnchanged()
    {
        var original = CreateManifest();
        var candidate = original with { ManifestVersion = 2, ChangeReason = "Retained before crash" };
        var store = new FileProviderIntegrationManifestStore(_testRoot);
        await store.SaveManifestAsync(original);
        var candidatePath = Path.Combine(_testRoot, "candidate.json");
        await File.WriteAllTextAsync(candidatePath, JsonSerializer.Serialize(
            candidate, ProviderIntegrationContractsJsonContext.Default.ProviderIntegrationManifestDto));
        var lockPath = Path.Combine(_testRoot, "_integrations", "manifest-locks",
            $"{Sha256Digest.ComputeUtf8(original.ManifestId)}.lock");
        File.SetLastWriteTimeUtc(lockPath, DateTime.UtcNow.AddHours(-1));
        var ready = Path.Combine(_testRoot, "manifest-writer.ready");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        };
        foreach (var argument in new[]
        {
            "exec", "--depsfile", Path.Combine(AppContext.BaseDirectory, "Meridian.Tests.deps.json"),
            "--runtimeconfig", Path.Combine(AppContext.BaseDirectory, "Meridian.Tests.runtimeconfig.json"),
            typeof(ProcessTestHelperMarker).Assembly.Location,
            "manifest-retain-and-hold-lock", _testRoot, candidatePath, ready
        })
        {
            start.ArgumentList.Add(argument);
        }
        using var writer = Process.Start(start) ?? throw new InvalidOperationException("Manifest writer did not start.");
        var errors = writer.StandardError.ReadToEndAsync();
        try
        {
            while (!File.Exists(ready))
            {
                Assert.False(writer.HasExited, writer.HasExited ? await errors : "");
                await Task.Delay(20, timeout.Token);
            }
            File.GetLastWriteTimeUtc(lockPath).Should().BeBefore(DateTime.UtcNow.AddMinutes(-30));
            using var contention = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
            contention.CancelAfter(TimeSpan.FromMilliseconds(250));
            var blockedUpdate = () => store.CompareExchangeCurrentManifestAsync(original.ManifestId,
                ProviderIntegrationManifestIdentity.Create(original), ProviderIntegrationManifestIdentity.Create(candidate),
                contention.Token);

            await blockedUpdate.Should().ThrowAsync<OperationCanceledException>();
            writer.HasExited.Should().BeFalse();
            writer.Kill(entireProcessTree: true);
            await writer.WaitForExitAsync(timeout.Token);

            var restarted = new FileProviderIntegrationManifestStore(_testRoot);
            (await restarted.GetManifestAsync(original.ManifestId, timeout.Token)).Should().BeEquivalentTo(original);
            (await restarted.GetManifestVersionAsync(candidate.ManifestId, 2, timeout.Token)).Should().BeEquivalentTo(candidate);
            (await restarted.CompareExchangeCurrentManifestAsync(original.ManifestId,
                ProviderIntegrationManifestIdentity.Create(original), ProviderIntegrationManifestIdentity.Create(candidate),
                timeout.Token)).Should().BeTrue();
            (await restarted.GetManifestAsync(original.ManifestId, timeout.Token)).Should().BeEquivalentTo(candidate);
        }
        finally
        {
            if (!writer.HasExited)
            {
                writer.Kill(entireProcessTree: true);
            }
            await writer.WaitForExitAsync();
        }
    }

    private string ManifestPointerPath(string manifestId)
        => Path.Combine(_testRoot, "_integrations", "manifests", $"{Sha256Digest.ComputeUtf8(manifestId)}.json");

    private string ManifestVersionPath(ProviderIntegrationManifestDto manifest)
        => Path.Combine(_testRoot, "_integrations", "manifest-versions",
            Sha256Digest.ComputeUtf8(manifest.ManifestId), $"{manifest.ManifestVersion}.json");

    private async Task WriteLegacyManifestAsync(ProviderIntegrationManifestDto manifest)
    {
        var path = ManifestPointerPath(manifest.ManifestId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(
            manifest, ProviderIntegrationContractsJsonContext.Default.ProviderIntegrationManifestDto));
    }
}
