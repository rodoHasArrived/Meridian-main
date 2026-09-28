using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.AccountingSystem;
using Meridian.ProviderSdk.AccountingSystem;
using Xunit;

namespace Meridian.Tests.DataIntegration.AccountingSystem;

public sealed class ExternalGlScopeTests
{
    [Theory]
    [InlineData("WAGESEXPENSE")]
    [InlineData("SUPERANNUATIONEXPENSE")]
    public async Task Xero_PayrollExpenseTypesParticipateInIncomeYearRollForward(string accountType)
    {
        using var handler = new ExternalGlTestHandler((request, body) => request.RequestUri!.AbsolutePath.EndsWith("Accounts", StringComparison.Ordinal)
            ? ExternalGlTestHandler.Json(new
            {
                Accounts = new[]
                {
                    new { AccountID = "cash", Code = "100", Name = "Cash", Type = "BANK", Status = "ACTIVE", SystemAccount = "" },
                    new { AccountID = "capital", Code = "300", Name = "Capital", Type = "EQUITY", Status = "ACTIVE", SystemAccount = "" },
                    new { AccountID = "payroll", Code = "600", Name = "Payroll", Type = accountType, Status = "ACTIVE", SystemAccount = "" },
                    new { AccountID = "retained", Code = "320", Name = "Prior results", Type = "EQUITY", Status = "ACTIVE", SystemAccount = "RETAINEDEARNINGS" }
                }
            }) : ExternalGlTestData.Respond(request, body));
        using var client = new HttpClient(handler);
        var detail = await ExternalGlTestData.Provider("xero", new("xero"), client).ImportAsync(ExternalGlTestData.Request("xero"));
        detail.Summary.TrialBalanceBasis!.IncomeStatementAccountCodes.Should().Equal("600");
        detail.Summary.TrialBalanceBasis.RetainedEarningsAccountCode.Should().Be("320");
    }

    [Fact]
    public async Task NetSuite_RestrictsAccountsToDirectOrInheritedSubsidiaryAssignments_AndRejectsForeignExportAccount()
    {
        var store = new ExternalGlTestStore("netsuite");
        using var handler = new ExternalGlTestHandler((request, body) =>
        {
            if (body.Contains("FROM subsidiary s", StringComparison.Ordinal))
                return ExternalGlTestHandler.Page([new { id = "2", parent = "1", currency = "USD" }]);
            if (body.Contains("FROM subsidiary WHERE", StringComparison.Ordinal))
                return ExternalGlTestHandler.Page([new { id = "1", parent = (string?)null }]);
            if (body.Contains("FROM account WHERE", StringComparison.Ordinal))
            {
                using var json = JsonDocument.Parse(body);
                var query = json.RootElement.GetProperty("q").GetString()!;
                query.Should().Contain("BUILTIN.MNFILTER(subsidiary, 'MN_INCLUDE', '', 'TRUE', '2') = 'T'");
                query.Should().Contain("OR (includechildren = 'T' AND (BUILTIN.MNFILTER(subsidiary, 'MN_INCLUDE', '', 'TRUE', '1') = 'T'))");
                query.Should().NotContain("'9'");
                // Provider-side relation contains direct, inherited, parent-only, and foreign accounts.
                var assignments = new[] { ("cash", "2", false), ("capital", "2", false), ("retained", "1", true),
                    ("inherited", "1", true), ("parent-only", "1", false), ("foreign", "9", true) };
                return ExternalGlTestHandler.Page(assignments.Where(a => a.Item2 == "2" || (a.Item2 == "1" && a.Item3))
                    .Select(a => ExternalGlTestData.NetSuiteAccount(a.Item1, a.Item1 == "cash" ? "Bank" : "Equity",
                        a.Item1 == "retained" ? "RetEarnings" : "NONE")).ToArray());
            }
            return ExternalGlTestData.Respond(request, body);
        });
        using var client = new HttpClient(handler);
        var provider = ExternalGlTestData.Provider("netsuite", store, client);
        var import = await provider.ImportAsync(ExternalGlTestData.Request("netsuite"));
        import.ChartAccounts.Select(a => a.ExternalAccountId).Should().BeEquivalentTo("cash", "capital", "retained", "inherited");
        var controls = new[] { "subsidiary-scope", "classification-segments", "entity-mapping", "intercompany-controls" };
        var evidence = controls.Select(c => $"approval:external-gl-provider:netsuite:{c}:import:{import.Summary.ImportId}:ledger-book:{ExternalGlTestData.Book:D}:2026-01-01:2026-01-31").ToArray();
        var issues = await provider.ValidateExportAsync(new(ExternalGlTestData.Book, new(2026, 1, 1), new(2026, 1, 31), import,
            [new("line1", "row1", AccountingSystemReconciliationStatusDto.Matched, "cash", "foreign", "Foreign", "USD", 10, 0, 10, null, null, []),
             new("line2", "row2", AccountingSystemReconciliationStatusDto.Matched, "capital", "capital", "Capital", "USD", 0, 10, -10, null, null, [])], evidence));
        issues.Should().Contain(i => i.Code == "ExternalGlProviderExportLinesInvalid");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NetSuite_UnavailableOrCyclicSubsidiaryHierarchyFailsClosed(bool cycle)
    {
        using var handler = new ExternalGlTestHandler((request, body) =>
            body.Contains("FROM subsidiary s", StringComparison.Ordinal)
                ? ExternalGlTestHandler.Page([new { id = "2", parent = cycle ? "2" : "1", currency = "USD" }])
                : body.Contains("FROM subsidiary WHERE", StringComparison.Ordinal) ? ExternalGlTestHandler.Page([])
                : ExternalGlTestData.Respond(request, body));
        using var client = new HttpClient(handler);
        await ExternalGlTestData.Provider("netsuite", new("netsuite"), client).Invoking(p => p.ImportAsync(ExternalGlTestData.Request("netsuite")))
            .Should().ThrowAsync<InvalidOperationException>();
        handler.Requests.Should().NotContain(r => r.Body.Contains("FROM account WHERE"));
    }

    [Theory]
    [InlineData("2026-03-31", 3, 31, "2025-04-01")]
    [InlineData("2026-04-01", 3, 31, "2026-04-01")]
    [InlineData("2026-01-01", 12, 31, "2026-01-01")]
    [InlineData("2024-02-29", 2, 29, "2023-03-01")]
    [InlineData("2025-03-01", 2, 29, "2025-03-01")]
    public async Task Xero_ReportYearFollowsOrganisationCalendar(string end, int month, int day, string expected)
    {
        using var handler = new ExternalGlTestHandler((request, body) => request.RequestUri!.AbsolutePath.EndsWith("Organisation", StringComparison.Ordinal)
            ? ExternalGlTestHandler.Json(new
            {
                Organisations = new[] { new { OrganisationID = ExternalGlTestStore.Tenant, BaseCurrency = "USD",
                FinancialYearEndMonth = month, FinancialYearEndDay = day } }
            })
            : ExternalGlTestData.Respond(request, body));
        using var client = new HttpClient(handler);
        var detail = await ExternalGlTestData.Provider("xero", new("xero"), client).ImportAsync(ExternalGlTestData.Request("xero") with
        { PeriodStart = DateOnly.Parse(end), PeriodEnd = DateOnly.Parse(end) });
        detail.Summary.TrialBalanceBasis!.IncomeStatementPeriodStart.Should().Be(DateOnly.Parse(expected));
    }
}
