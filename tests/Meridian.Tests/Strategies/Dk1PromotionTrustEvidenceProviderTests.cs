using System.Text.Json;
using FluentAssertions;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;
using Meridian.Strategies.Promotions;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Tests.Strategies;

public sealed class Dk1PromotionTrustEvidenceProviderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetEvidenceAsync_UsesRetainedPacketDespiteUnrelatedArtifacts_AndRejectsDeletion(
        bool includeMalformedBundle)
    {
        var observedAt = DateTimeOffset.UtcNow.AddHours(-1);
        var (root, packetReference) = await WritePacketAsync(observedAt, complete: true);
        try
        {
            var options = new Dk1TrustGateReadinessOptions(root);
            var readiness = new Dk1TrustGateReadinessService(options, NullLogger<Dk1TrustGateReadinessService>.Instance);
            var provider = new Dk1PromotionTrustEvidenceProvider(readiness, options);
            var run = StrategyRunEntry.StartWithEvidence(
                "trust-strategy", "Trust Strategy", RunType.Paper, "trust-run",
                retainedEvidenceReferences: [packetReference]);
            await readiness.GetCurrentAsync();
            var unrelatedDirectory = Path.Combine(root, "unrelated");
            Directory.CreateDirectory(unrelatedDirectory);
            var unrelatedPacket = Path.Combine(unrelatedDirectory, packetReference);
            File.Copy(Path.Combine(root, packetReference), unrelatedPacket);
            File.SetLastWriteTimeUtc(unrelatedPacket, DateTime.UtcNow.AddMinutes(1));
            if (includeMalformedBundle)
            {
                await File.WriteAllTextAsync(
                    Path.Combine(unrelatedDirectory, "provider-validation-evidence-bundle.json"), "{ invalid json");
            }

            var retainedEvidence = await provider.GetEvidenceAsync(run);
            File.Delete(Path.Combine(root, packetReference));
            var missingEvidence = await provider.GetEvidenceAsync(run);

            retainedEvidence.Should().NotBeNull();
            retainedEvidence!.SourceReference.Should().Be(packetReference);
            retainedEvidence.ObservedAt.Should().Be(observedAt);
            missingEvidence.Should().BeNull("promotion must reread retained evidence even while the cockpit has a cached success");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetEvidenceAsync_UsesPacketCompletenessAndObservationTime(bool complete)
    {
        var observedAt = DateTimeOffset.UtcNow.AddHours(-1);
        var (root, packetReference) = await WritePacketAsync(observedAt, complete);
        try
        {
            var options = new Dk1TrustGateReadinessOptions(root, TimeSpan.FromHours(6));
            using var services = new ServiceCollection()
                .AddLogging()
                .AddSingleton(options)
                .AddPromotionTrustEvidenceServices()
                .BuildServiceProvider();
            var provider = services.GetRequiredService<IPromotionTrustEvidenceProvider>();
            var run = StrategyRunEntry.StartWithEvidence(
                "trust-strategy", "Trust Strategy", RunType.Paper, "trust-run",
                retainedEvidenceReferences: [$"{PromotionApprovalChecklist.Dk1TrustPacketReviewed}:{packetReference}"]);

            var evidence = await provider.GetEvidenceAsync(run);

            evidence.Should().NotBeNull();
            evidence!.RunId.Should().Be(run.RunId);
            evidence.InputHashSha256.Should().Be(run.InputHashSha256);
            evidence.SourceReference.Should().Be(packetReference);
            evidence.ObservedAt.Should().Be(observedAt);
            evidence.ValidUntil.Should().Be(observedAt.AddHours(6));
            evidence.IsComplete.Should().Be(complete);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetEvidenceAsync_RejectsPacketNotRetainedBySourceRun(bool retainUnrelatedPacket)
    {
        var (root, _) = await WritePacketAsync(DateTimeOffset.UtcNow, complete: true);
        try
        {
            var options = new Dk1TrustGateReadinessOptions(root);
            var provider = new Dk1PromotionTrustEvidenceProvider(
                new Dk1TrustGateReadinessService(options, NullLogger<Dk1TrustGateReadinessService>.Instance),
                options);
            var run = StrategyRunEntry.StartWithEvidence(
                "trust-strategy", "Trust Strategy", RunType.Paper, "trust-run",
                retainedEvidenceReferences: retainUnrelatedPacket ? ["another-packet.json"] : []);

            var evidence = await provider.GetEvidenceAsync(run);

            evidence.Should().BeNull("a globally ready packet cannot authorize an unrelated run");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<(string Root, string PacketReference)> WritePacketAsync(
        DateTimeOffset observedAt,
        bool complete)
    {
        var root = Path.Combine(Path.GetTempPath(), "Meridian.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        const string packetReference = "dk1-pilot-parity-packet.json";
        var packet = new
        {
            generatedAtUtc = observedAt,
            status = "ready-for-operator-review",
            sampleReview = new
            {
                requiredCount = 1,
                samples = new[]
                {
                    new
                    {
                        id = "provider-quote-parity",
                        provider = "Alpaca",
                        status = complete ? "ready" : "blocked",
                        observed = true,
                        missingRequirements = Array.Empty<string>(),
                        evidenceAnchors = new[] { "retained/provider-quote-parity" }
                    }
                }
            },
            evidenceDocuments = new[]
            {
                new
                {
                    name = "Provider validation",
                    path = "retained/provider-validation.json",
                    exists = true,
                    status = "validated",
                    missingRequirements = Array.Empty<string>()
                }
            },
            trustRationaleContract = new
            {
                documentPath = "retained/trust-rationale.json",
                status = "validated",
                requiredPayloadFields = new[] { "signalSource" },
                requiredReasonCodes = new[] { "HEALTHY_BASELINE" }
            },
            baselineThresholdContract = new
            {
                documentPath = "retained/baseline-thresholds.json",
                status = "validated",
                requiredMetrics = new[] { "Composite trust score" },
                fpFnReviewRequired = true
            },
            blockers = Array.Empty<string>()
        };
        await File.WriteAllTextAsync(Path.Combine(root, packetReference), JsonSerializer.Serialize(packet));
        return (root, packetReference);
    }
}
