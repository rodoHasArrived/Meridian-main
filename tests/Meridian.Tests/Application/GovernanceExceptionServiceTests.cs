using FluentAssertions;
using Meridian.Application.Services;
using Meridian.FSharp.Ledger;

namespace Meridian.Tests.Application;

public sealed class GovernanceExceptionServiceTests
{
    [Fact]
    public void IngestBreaks_ShouldPreservePartialAndOpenBreakSemanticsForGovernanceConsumers()
    {
        var service = new GovernanceExceptionService();
        var runId = Guid.NewGuid();
        var asOf = new DateTimeOffset(2026, 3, 21, 16, 30, 0, TimeSpan.Zero);

        var created = service.IngestBreaks(
            runId,
            "portfolio-1",
            [
                new PortfolioLedgerCheckResultDto
                {
                    CheckId = "partial-break",
                    Label = "Cash timing drift",
                    IsMatch = false,
                    Category = "partial_match",
                    Status = "partial_match",
                    MissingSource = "unknown",
                    ExpectedSource = "portfolio",
                    ActualSource = "ledger",
                    ExpectedAmount = 750m,
                    ActualAmount = 750m,
                    HasExpectedAmount = true,
                    HasActualAmount = true,
                    Variance = 0m,
                    Reason = "Timing drift 5760 minute(s)",
                    Severity = "Low",
                    ExpectedAsOf = asOf,
                    ActualAsOf = asOf.AddDays(4),
                    HasExpectedAsOf = true,
                    HasActualAsOf = true
                },
                new PortfolioLedgerCheckResultDto
                {
                    CheckId = "open-break",
                    Label = "Net equity mismatch",
                    IsMatch = false,
                    Category = "amount_mismatch",
                    Status = "open",
                    MissingSource = "unknown",
                    ExpectedSource = "portfolio",
                    ActualSource = "ledger",
                    ExpectedAmount = 1000m,
                    ActualAmount = 900m,
                    HasExpectedAmount = true,
                    HasActualAmount = true,
                    Variance = -100m,
                    Reason = "Amounts differ beyond the configured tolerance.",
                    Severity = "High",
                    ExpectedAsOf = asOf,
                    ActualAsOf = asOf,
                    HasExpectedAsOf = true,
                    HasActualAsOf = true
                },
                new PortfolioLedgerCheckResultDto
                {
                    CheckId = "matched-break",
                    Label = "Exact match",
                    IsMatch = true,
                    Category = "matched",
                    Status = "matched",
                    MissingSource = "unknown",
                    ExpectedSource = "portfolio",
                    ActualSource = "ledger",
                    ExpectedAmount = 100m,
                    ActualAmount = 100m,
                    HasExpectedAmount = true,
                    HasActualAmount = true,
                    Variance = 0m,
                    Reason = "Comparison satisfied all configured checks.",
                    Severity = "Info",
                    ExpectedAsOf = asOf,
                    ActualAsOf = asOf,
                    HasExpectedAsOf = true,
                    HasActualAsOf = true
                }
            ],
            asOf);

        created.Should().HaveCount(2);
        created.Should().ContainSingle(exception =>
            exception.CheckId == "partial-break" &&
            exception.Category == "partial_match" &&
            exception.Severity == GovernanceExceptionSeverity.Low);
        created.Should().ContainSingle(exception =>
            exception.CheckId == "open-break" &&
            exception.Category == "amount_mismatch" &&
            exception.Severity == GovernanceExceptionSeverity.High);
    }
}
