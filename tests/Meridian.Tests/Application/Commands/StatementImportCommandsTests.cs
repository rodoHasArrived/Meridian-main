using FluentAssertions;
using Meridian.Application.Commands;
using Meridian.Application.ResultTypes;
using Serilog.Core;
using Xunit;

namespace Meridian.Tests.Application.Commands;

[Trait("Category", "Unit")]
public sealed class StatementImportCommandsTests
{
    [Fact]
    public void CanHandle_BrokerSpecificValidate_ReturnsTrue()
    {
        var command = new StatementImportCommands("data", Logger.None);

        command.CanHandle(
            [
                "--statement-validate",
                "--statement-broker", "samplebroker",
                "--statement-source-path", "statement.csv"
            ]).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_GenericLocalValidate_ReturnsFalse()
    {
        var command = new StatementImportCommands("data", Logger.None);

        command.CanHandle(
            [
                "--statement-validate",
                "--statement-source-kind", "local",
                "--statement-source-path", "statement.csv"
            ]).Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_InvalidStatementDate_ReturnsValidationFailure()
    {
        var command = new StatementImportCommands("data", Logger.None);

        var result = await command.ExecuteAsync(
            [
                "--statement-validate",
                "--statement-broker", "samplebroker",
                "--statement-source-path", "statement.csv",
                "--statement-date", "not-a-date"
            ]);

        result.Error.Should().Be(ErrorCode.ValidationFailed);
    }

    [Fact]
    public async Task Dispatcher_PrefersBrokerImportOnlyForBrokerSpecificStatementArgs()
    {
        var root = Path.Combine(Path.GetTempPath(), $"meridian-statement-cli-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var statementPath = Path.Combine(root, "statement.csv");
        await File.WriteAllTextAsync(
            statementPath,
            "account,symbol,quantity,price,cashAmount,activityType,tradeDate\nA1,SPY,10,500,5000,BUY,2026-01-02\n");

        var dispatcher = new CommandDispatcher(
            new StatementImportCommands(root, Logger.None),
            new StatementCommands());

        var originalOut = Console.Out;
        try
        {
            using var writer = new StringWriter();
            Console.SetOut(writer);

            var (handled, result) = await dispatcher.TryDispatchAsync(
                [
                    "--statement-validate",
                    "--statement-broker", "samplebroker",
                    "--statement-source-path", statementPath,
                    "--statement-date", "2026-01-31"
                ]);

            handled.Should().BeTrue();
            result.Success.Should().BeTrue();
            writer.ToString().Should().Contain("valid=True; rows=1");
        }
        finally
        {
            Console.SetOut(originalOut);
            Directory.Delete(root, recursive: true);
        }
    }
}
