using System.Net;
using FluentAssertions;
using Meridian.Contracts.AccountingSystem;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.FundStructure;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Tests.DataIntegration.AccountingSystem;
using Moq;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed partial class AccountingSystemIntegrationServiceTests
{
    [Theory]
    [InlineData("xero", false, false)]
    [InlineData("netsuite", false, false)]
    [InlineData("netsuite", true, false)]
    [InlineData("xero", false, true)]
    [InlineData("netsuite", false, true)]
    public async Task LiveProviders_ReconcileReportBalancesAcrossPeriods_AndExportOnlyRequestedActivity(string id, bool unnumbered, bool effectiveDates)
    {
        string Code(string account) => unnumbered ? $"netsuite-account:{account}" : account switch { "cash" => "100", "income" => "400", _ => "320" };
        var credentials = new ExternalGlTestStore(id);
        using var handler = new ExternalGlTestHandler((request, body) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("Organisation", StringComparison.Ordinal))
                return ExternalGlTestHandler.Json(new
                {
                    Organisations = new[] { new { OrganisationID = ExternalGlTestStore.Tenant,
                    BaseCurrency = "USD", FinancialYearEndMonth = 3, FinancialYearEndDay = 31 } }
                });
            if (path.EndsWith("Accounts", StringComparison.Ordinal))
                return ExternalGlTestHandler.Json(new
                {
                    Accounts = new[]
                {
                    new { AccountID = "cash", Code = Code("cash"), Name = "Cash", Type = "BANK", Status = "ACTIVE", SystemAccount = "" },
                    new { AccountID = "income", Code = Code("income"), Name = "Revenue", Type = "REVENUE", Status = "ACTIVE", SystemAccount = "" },
                    new { AccountID = "retained", Code = Code("retained"), Name = "Prior results", Type = "EQUITY", Status = "ACTIVE", SystemAccount = "RETAINEDEARNINGS" }
                }
                });
            if (path.EndsWith("Journals", StringComparison.Ordinal))
                return ExternalGlTestHandler.Json(new
                {
                    Journals = request.RequestUri.Query.Contains("offset=0", StringComparison.Ordinal)
                    ? new object[] { new { JournalID = "feb-6", JournalNumber = 7, JournalDate = "2026-02-06", JournalLines = new[]
                    { new { JournalLineID = "1", AccountID = "cash", NetAmount = 30m }, new { JournalLineID = "2", AccountID = "income", NetAmount = -30m } } } } : []
                });
            if (path.EndsWith("TrialBalance", StringComparison.Ordinal))
                return ExternalGlTestHandler.Json(new
                {
                    Reports = new[] { new { ReportType = "TrialBalance", Rows = new object[]
                {
                    new { RowType = "Header", Cells = new[] { "Account", "Debit", "Credit", "YTD Debit", "YTD Credit" }.Select(value => new { Value = value }) },
                    new { RowType = "Section", Rows = new[] { ExternalGlTestData.XeroBalance("cash", "210", ""),
                        ExternalGlTestData.XeroBalance("income", "", "110"), ExternalGlTestData.XeroBalance("retained", "", "100") } }
                } } }
                });
            if (body.Contains("FROM account WHERE", StringComparison.Ordinal))
                return ExternalGlTestHandler.Page(new[] { "cash", "income", "retained" }.Select(account => (object)new
                {
                    id = account,
                    acctnumber = unnumbered ? "" : Code(account),
                    acctname = account,
                    accttype = account == "cash" ? "Bank" : account == "income" ? "Income" : "Equity",
                    isinactive = "F",
                    specialaccounttype = account == "retained" ? "RetEarnings" : "NONE"
                }).ToArray());
            if (body.Contains("AS prioryearbalance", StringComparison.Ordinal))
                return ExternalGlTestHandler.Page([ExternalGlTestData.NetSuiteBalance("cash", 210, 150), ExternalGlTestData.NetSuiteBalance("income", -210, -150)]);
            if (body.Contains("FROM transaction t", StringComparison.Ordinal))
                return ExternalGlTestHandler.Page([
                    new { journalid = "feb-6", accountingdate = "2026-02-06", lineid = "1", accountid = "cash", debit = 30m, credit = 0m },
                    new { journalid = "feb-6", accountingdate = "2026-02-06", lineid = "2", accountid = "income", debit = 0m, credit = 30m }]);
            return ExternalGlTestData.Respond(request, body);
        });
        using var client = new HttpClient(handler);
        var provider = ExternalGlTestData.Provider(id, credentials, client);
        var records = new List<LedgerJournalEntryRecord>();
        var periods = new List<LedgerAccountingPeriod>();
        var activities = new[] { ("2024-12-15", 100m), ("2025-05-15", 50m), ("2026-01-15", 20m),
            ("2026-02-01", 10m), ("2026-02-06", 30m), ("2026-02-25", 999m) };
        foreach (var (date, amount) in activities)
        {
            var accountingDate = DateOnly.Parse(date);
            var timestamp = DateTimeOffset.Parse((effectiveDates ? date == "2026-02-25" ? "2026-02-06" : "2026-03-10" : date) + "T00:00:00Z");
            var periodStart = new DateOnly(accountingDate.Year, accountingDate.Month, 1);
            var period = periods.SingleOrDefault(p => p.StartDate == periodStart);
            if (period is null)
            {
                period = new(Guid.NewGuid(), ExternalGlLedgerBookId, accountingDate.Year, accountingDate.Month, date,
                    periodStart, periodStart.AddMonths(1).AddDays(-1), "Open", timestamp, null, 1);
                periods.Add(period);
            }
            var journalId = Guid.NewGuid();
            var journal = new JournalEntry(journalId, timestamp, date,
            [
                new(Guid.NewGuid(), journalId, timestamp, new(Code("cash"), LedgerAccountType.Asset), amount, 0m, date),
                new(Guid.NewGuid(), journalId, timestamp, new(Code("income"), LedgerAccountType.Revenue), 0m, amount, date)
            ], effectiveDates ? new JournalEntryMetadata(EffectiveDate: accountingDate) : null);
            records.Add(new(journal, Guid.NewGuid(), period.PeriodId, null, null, records.Count + 1, timestamp));
        }
        var ledger = new Mock<ILedgerJournalStore>(MockBehavior.Strict);
        ledger.Setup(s => s.ListPeriodsAsync(ExternalGlLedgerBookId, null, "default-fund", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(periods);
        ledger.Setup(s => s.GetLedgerBookAsync(ExternalGlLedgerBookId, It.IsAny<CancellationToken>())).ReturnsAsync(
            new LedgerBookRecord(ExternalGlLedgerBookId, "default-fund", Guid.NewGuid(), FundStructureNodeKindDto.Fund, "Book", "USD", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        ledger.Setup(s => s.GetByPeriodAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid period, CancellationToken _) => (IReadOnlyList<LedgerJournalEntryRecord>)records.Where(r => r.PeriodId == period).ToArray());
        var service = CreateService(ledger.Object, provider);
        var start = new DateOnly(2026, 2, 5);
        var end = new DateOnly(2026, 2, 15);
        var import = await service.ImportAsync(new(id, "default-fund", ExternalGlLedgerBookId, start, end));
        import.Summary.PeriodStart.Should().Be(start);
        import.Summary.TrialBalanceBasis!.IncomeStatementPeriodStart.Should().Be(id == "xero" ? new(2025, 4, 1) : new(2026, 1, 1));
        var reconciliation = await service.ReconcileLatestAsync(id, "default-fund", ExternalGlLedgerBookId);
        reconciliation.BreakCount.Should().Be(0);
        reconciliation.Rows.Single(r => r.AccountCode == Code("cash")).MeridianDebit.Should().Be(210m);
        reconciliation.Rows.Single(r => r.AccountCode == Code("income")).MeridianCredit.Should().Be(id == "xero" ? 110m : 60m);
        reconciliation.Rows.Single(r => r.AccountCode == Code("retained")).MeridianCredit.Should().Be(id == "xero" ? 100m : 150m);
        var template = CertifiedQuickBooksMappingProfile();
        var profile = template with
        {
            ProviderId = id,
            ProfileId = $"{id}-period-basis",
            AccountMappings = new[] { "cash", "income", "retained" }.ToDictionary(Code, a => a),
            DimensionMappings = template.DimensionMappings.Select(m => m with { ProviderId = id }).ToArray()
        };
        await service.UpsertMappingProfileAsync(new(profile, "accounting-ops", ProviderId: id,
            FundProfileId: "default-fund", LedgerBookId: ExternalGlLedgerBookId, EvidenceLinks: [$"approval:external-gl-mapping:{profile.ProfileId}"]));
        var controls = id == "xero" ? new[] { "tracking-category-options", "contact-mapping", "tax-rate-mapping", "bank-account-scope" }
            : ["subsidiary-scope", "classification-segments", "entity-mapping", "intercompany-controls"];
        var evidence = controls.Select(c => $"approval:external-gl-provider:{id}:{c}:import:{import.Summary.ImportId}:ledger-book:{ExternalGlLedgerBookId:D}:{start:yyyy-MM-dd}:{end:yyyy-MM-dd}").ToArray();
        var package = await service.CreateExportPackageAsync(new("accounting-ops", id, "default-fund", ExternalGlLedgerBookId, start, end, profile.ProfileId,
            EvidenceLinks: [$"approval:export-package:{id}:default-fund:ledger-book:{ExternalGlLedgerBookId:D}:{start:yyyy-MM-dd}:{end:yyyy-MM-dd}", .. evidence]));
        package.ValidationIssues.Should().NotContain(i => i.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        package.GeneratedLines.Should().HaveCount(2);
        package.GeneratedLines.Should().ContainSingle(l => l.ExternalAccountId == "cash" && l.Debit == 30m && l.Credit == 0m);
        package.GeneratedLines.Should().ContainSingle(l => l.ExternalAccountId == "income" && l.Credit == 30m && l.Debit == 0m);
        var certified = await service.CertifyExportPackageAsync(new(package.ExportPackageId, "controller", "Reviewed the distinct report and activity periods.",
            [$"approval:external-gl-export-certification:{package.ExportPackageId}:{package.Certification!.CertificationId}:ledger-book:{ExternalGlLedgerBookId:D}:{start:yyyy-MM-dd}:{end:yyyy-MM-dd}"]));
        certified!.Certification!.State.Should().Be(AccountingCertificationStateDto.Certified);
        (await service.GetExportPackageManifestAsync(package.ExportPackageId))!.GeneratedLines.Should().BeEquivalentTo(package.GeneratedLines);
        // Equal-and-opposite edits preserve closing balances but must invalidate the retained gross export activity.
        var changeId = Guid.NewGuid();
        var changeDate = new DateTimeOffset(2026, 2, 7, 0, 0, 0, TimeSpan.Zero);
        records.Add(new(new(changeId, changeDate, "Gross activity amendment",
            [new(Guid.NewGuid(), changeId, changeDate, new(Code("cash"), LedgerAccountType.Asset), 5m, 0m, "Gross activity amendment"),
             new(Guid.NewGuid(), changeId, changeDate, new(Code("cash"), LedgerAccountType.Asset), 0m, 5m, "Gross activity amendment")]),
            Guid.NewGuid(), periods.Last().PeriodId, null, null, 99, changeDate));
        (await service.GetExportPackageManifestAsync(package.ExportPackageId))!.ValidationIssues
            .Should().Contain(i => i.Severity == AccountingConfigurationValidationSeverityDto.Critical);
    }
}
