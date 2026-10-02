using System.Diagnostics;
using FluentAssertions;
using Meridian.Core.Config;
using Meridian.Platform.Tracing;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace Meridian.Tests.Platform.Tracing;

[Collection("Sequential")]
public sealed class OpenTelemetrySetupTests
{
    [Fact]
    public void Defaults_RequireTracingAndExporterOptIn()
    {
        var hostConfig = new TracingConfig();
        hostConfig.Enabled.Should().BeFalse();
        hostConfig.EnableConsoleExporter.Should().BeFalse();
        hostConfig.EnableOtlpExporter.Should().BeFalse();
        var sdkConfig = OpenTelemetryConfiguration.Default;
        sdkConfig.EnableConsoleExporter.Should().BeFalse();
        sdkConfig.EnableOtlpExporter.Should().BeFalse();
        sdkConfig.OtlpEndpoint.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("collector:4317")]
    [InlineData("/relative/collector")]
    [InlineData("file:///tmp/traces")]
    [InlineData("ftp://collector.example/traces")]
    [InlineData("https://username:password@collector.example:4317")]
    [InlineData("https://collector.example:4317?secret=credential")]
    [InlineData("https://collector.example:4317#fragment")]
    public void OtlpExporter_RejectsMissingOrUnsafeDestination(string? endpoint)
    {
        var services = new ServiceCollection();
        Action register = () => services.AddOpenTelemetryTracing(new OpenTelemetryConfiguration
        {
            EnableOtlpExporter = true,
            OtlpEndpoint = endpoint
        });

        register.Should().Throw<ArgumentException>().WithMessage("*Tracing.OtlpEndpoint*");
        services.Should().BeEmpty("invalid tracing must fail before partially registering the provider");
    }

    [Fact]
    public void ConfiguredDestination_IsValidatedEvenWhenExporterIsDisabled()
    {
        Action register = () => new ServiceCollection().AddOpenTelemetryTracing(new OpenTelemetryConfiguration
        {
            OtlpEndpoint = "file:///tmp/traces"
        });

        register.Should().Throw<ArgumentException>().WithMessage("*Tracing.OtlpEndpoint*");
    }

    [Theory]
    [InlineData("http://localhost:4317")]
    [InlineData("https://collector.example:4317")]
    [InlineData("http://[::1]:4317")]
    public void ConfiguredDestination_AcceptsAbsoluteHttpCollectorUrls(string endpoint)
    {
        // Keep the exporter disabled so validation cannot send telemetry to a test destination.
        Action register = () => new ServiceCollection().AddOpenTelemetryTracing(new OpenTelemetryConfiguration
        {
            OtlpEndpoint = endpoint
        });

        register.Should().NotThrow();
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void SamplingRatio_RejectsNonFiniteOrOutOfRangeValues(double samplingRatio)
    {
        Action register = () => new ServiceCollection().AddOpenTelemetryTracing(new OpenTelemetryConfiguration
        {
            SamplingRatio = samplingRatio
        });

        register.Should().Throw<ArgumentException>().WithMessage("*Tracing.SamplingRatio*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ShutdownTimeout_MustBePositive(int timeoutMilliseconds)
    {
        Action register = () => new ServiceCollection().AddOpenTelemetryTracing(new OpenTelemetryConfiguration
        {
            FlushTimeoutMilliseconds = timeoutMilliseconds
        });

        register.Should().Throw<ArgumentException>().WithMessage("*Tracing.FlushTimeoutMilliseconds*");
    }

    [Fact]
    public void RepeatedRegistration_OwnsOneProviderAndExportsEachSpanOnce()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOpenTelemetryTracing(OpenTelemetryConfiguration.Default);
        services.AddOpenTelemetryTracing(OpenTelemetryConfiguration.Default);
        var exporter = new RecordingExporter();
        services.ConfigureOpenTelemetryTracerProvider((_, builder) =>
            builder.AddProcessor(new SimpleActivityExportProcessor(exporter)));

        using (var provider = services.BuildServiceProvider())
        {
            provider.GetServices<TracerProvider>().Should().ContainSingle();
            using var activity = OpenTelemetrySetup.ActivitySource.StartActivity("registration.probe");
            activity.Should().NotBeNull();
        }

        exporter.Activities.Should().ContainSingle(span => span.OperationName == "registration.probe");
        exporter.DisposeCount.Should().Be(1);
    }

    private sealed class RecordingExporter : BaseExporter<Activity>
    {
        public List<Activity> Activities { get; } = [];
        public int DisposeCount { get; private set; }

        public override ExportResult Export(in Batch<Activity> batch)
        {
            foreach (var activity in batch)
                Activities.Add(activity);
            return ExportResult.Success;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                DisposeCount++;
            base.Dispose(disposing);
        }
    }
}
