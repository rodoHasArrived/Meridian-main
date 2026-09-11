using FluentAssertions;
using Meridian.Application.Commands;

namespace Meridian.Tests.Application.FundStructure;

/// <summary>Operator invocation remains explicit and connection failures never echo credentials.</summary>
public sealed class FundStructureTenantBackfillCommandTests
{
    [Fact]
    public async Task Execute_MissingExplicitMode_RefusesBeforeCreatingDatabaseRunner()
    {
        using var output = new StringWriter();
        var creates = 0;
        var command = new FundStructureTenantBackfillCommand(() => { creates++; throw new InvalidOperationException(); }, output);

        var result = await command.ExecuteAsync(["--fund-tenant-backfill", "--output", "unused.json"]);

        result.Success.Should().BeFalse();
        creates.Should().Be(0);
        output.ToString().Should().Contain("--mode preview|apply");
    }

    [Fact]
    public async Task Execute_ConnectionError_RedactsExceptionAndRawArguments()
    {
        using var output = new StringWriter();
        var command = new FundStructureTenantBackfillCommand(
            () => throw new InvalidOperationException("Host=private;Password=do-not-echo"), output);

        var result = await command.ExecuteAsync(["--fund-tenant-backfill", "--mode", "preview", "--output", "unused.json"]);

        result.Success.Should().BeFalse();
        output.ToString().Should().Contain("InvalidOperationException");
        output.ToString().Should().NotContain("Password").And.NotContain("do-not-echo").And.NotContain("private");
    }
}
