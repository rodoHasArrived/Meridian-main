using FluentAssertions;
using Meridian.Contracts.AccountingSystem;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.Tests.DataIntegration.AccountingSystem;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Moq;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed partial class AccountingSystemIntegrationServiceTests
{
    [Theory]
    [InlineData("xero", AccountingBasisKindDto.Cash)]
    [InlineData("netsuite", AccountingBasisKindDto.Cash)]
    [InlineData("xero", AccountingBasisKindDto.Tax)]
    [InlineData("netsuite", AccountingBasisKindDto.Tax)]
    [InlineData("xero", AccountingBasisKindDto.Statutory)]
    [InlineData("netsuite", AccountingBasisKindDto.Statutory)]
    [InlineData("xero", null)]
    [InlineData("netsuite", null)]
    public async Task LiveProviders_RequireAccrualCompatibleBookAtReconciliationAndEveryExportBoundary(string id, AccountingBasisKindDto? incompatibleBasis)
    {
        var fixture = CreateMatchedFixtureLedgerStore([("100", LedgerAccountType.Asset, 100.25m, 0m), ("300", LedgerAccountType.Equity, 0m, 100.25m)]);
        LedgerBookRecord? book = (await fixture.GetLedgerBookAsync(ExternalGlLedgerBookId))! with { AccountingBasis = AccountingBasisKindDto.Gaap };
        var periods = await fixture.ListPeriodsAsync(ExternalGlLedgerBookId, fundProfileId: "default-fund");
        var records = await fixture.GetByPeriodAsync(periods.Single().PeriodId);
        var ledger = new Mock<ILedgerJournalStore>(MockBehavior.Strict);
        ledger.Setup(s => s.GetLedgerBookAsync(ExternalGlLedgerBookId, It.IsAny<CancellationToken>())).ReturnsAsync(() => book);
        ledger.Setup(s => s.ListPeriodsAsync(ExternalGlLedgerBookId, null, "default-fund", null, It.IsAny<CancellationToken>())).ReturnsAsync(periods);
        ledger.Setup(s => s.GetByPeriodAsync(periods.Single().PeriodId, It.IsAny<CancellationToken>())).ReturnsAsync(records);
        using var handler = new ExternalGlTestHandler(ExternalGlTestData.Respond);
        using var client = new HttpClient(handler);
        var service = CreateService(ledger.Object, ExternalGlTestData.Provider(id, new(id), client));
        var import = await service.ImportAsync(ExternalGlTestData.Request(id) with { FundProfileId = "default-fund", TenantId = null, CompanyId = null });
        var template = CertifiedQuickBooksMappingProfile();
        var profile = template with
        {
            ProviderId = id,
            ProfileId = $"{id}-book-basis",
            AccountMappings = new Dictionary<string, string> { ["100"] = "cash", ["300"] = "capital" },
            DimensionMappings = template.DimensionMappings.Select(mapping => mapping with { ProviderId = id }).ToArray()
        };
        await service.UpsertMappingProfileAsync(new(profile, "accounting-ops", ProviderId: id,
            FundProfileId: "default-fund", LedgerBookId: ExternalGlLedgerBookId,
            EvidenceLinks: [$"approval:external-gl-mapping:{profile.ProfileId}"]));
        var controls = id == "xero"
            ? new[] { "tracking-category-options", "contact-mapping", "tax-rate-mapping", "bank-account-scope" }
            : ["subsidiary-scope", "classification-segments", "entity-mapping", "intercompany-controls"];
        var evidence = controls.Select(control => $"approval:external-gl-provider:{id}:{control}:import:{import.Summary.ImportId}:ledger-book:{ExternalGlLedgerBookId:D}:2026-01-01:2026-01-31").ToArray();
        var request = new AccountingSystemExportPackageRequestDto("accounting-ops", id, "default-fund", ExternalGlLedgerBookId,
            new(2026, 1, 1), new(2026, 1, 31), profile.ProfileId, RequireBalancedReconciliation: false,
            EvidenceLinks: [ExportControlEvidence(ExternalGlLedgerBookId, id), .. evidence]);
        var package = await service.CreateExportPackageAsync(request);
        package.Certification!.State.Should().Be(AccountingCertificationStateDto.ReadyForReview);
        book = incompatibleBasis is { } basis ? book with { AccountingBasis = basis } : null;
        await service.Invoking(s => s.ReconcileLatestAsync(id, "default-fund", ExternalGlLedgerBookId))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*accrual-compatible*");
        await service.Invoking(s => s.CreateExportPackageAsync(request))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*accrual-compatible*");
        await service.Invoking(s => s.CertifyExportPackageAsync(new(package.ExportPackageId, "controller",
            "An incompatible or unresolved book basis cannot satisfy accrual report evidence.",
            [$"approval:external-gl-export-certification:{package.ExportPackageId}:{package.Certification.CertificationId}:ledger-book:{ExternalGlLedgerBookId:D}:2026-01-01:2026-01-31"])))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*accrual-compatible*");
        await service.Invoking(s => s.GetExportPackageManifestAsync(package.ExportPackageId))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*accrual-compatible*");
    }

    [Theory]
    [InlineData("xero", true, false)]
    [InlineData("netsuite", true, false)]
    [InlineData("xero", false, false)]
    [InlineData("netsuite", false, false)]
    [InlineData("xero", false, true)]
    [InlineData("netsuite", false, true)]
    public async Task LiveProviders_RejectCurrencyMismatch_WithoutRelabelingExportActivity(
        string id, bool requireBalancedReconciliation, bool omittedZeroBalances)
    {
        var credentials = new ExternalGlTestStore(id);
        using var handler = new ExternalGlTestHandler((request, body) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("Organisation", StringComparison.Ordinal))
                return ExternalGlTestHandler.Json(new
                {
                    Organisations = new[] { new { OrganisationID = ExternalGlTestStore.Tenant,
                        BaseCurrency = "EUR", FinancialYearEndMonth = 12, FinancialYearEndDay = 31 } }
                });
            if (body.Contains("FROM subsidiary", StringComparison.Ordinal))
                return ExternalGlTestHandler.Page([new { id = "2", currency = "EUR" }]);
            if (omittedZeroBalances && path.EndsWith("TrialBalance", StringComparison.Ordinal))
                return ExternalGlTestData.XeroEmptyTrialBalance();
            if (omittedZeroBalances && body.Contains("SUM(", StringComparison.Ordinal))
                return ExternalGlTestHandler.Page([]);
            return ExternalGlTestData.Respond(request, body);
        });
        using var client = new HttpClient(handler);
        var provider = ExternalGlTestData.Provider(id, credentials, client);
        var ledgerLines = new List<(string, LedgerAccountType, decimal, decimal)>
        {
            ("100", LedgerAccountType.Asset, 100.25m, 0m),
            ("300", LedgerAccountType.Equity, 0m, 100.25m)
        };
        if (omittedZeroBalances)
            ledgerLines.AddRange([("100", LedgerAccountType.Asset, 0m, 100.25m), ("300", LedgerAccountType.Equity, 100.25m, 0m)]);
        var ledger = CreateMatchedFixtureLedgerStore(ledgerLines);
        var service = CreateService(ledger, provider);
        var import = await service.ImportAsync(ExternalGlTestData.Request(id) with
        {
            FundProfileId = "default-fund",
            TenantId = null,
            CompanyId = null
        });
        var reconciliation = await service.ReconcileLatestAsync(id, "default-fund", ExternalGlLedgerBookId);
        reconciliation.MatchedCount.Should().Be(0);
        reconciliation.BreakCount.Should().Be(2);
        reconciliation.Rows.Should().OnlyContain(row => row.Variance == 0m &&
            row.Status == AccountingSystemReconciliationStatusDto.Variance && row.Currency == "USD");
        reconciliation.Rows.Should().OnlyContain(row => row.Detail.Contains("EUR") && row.Detail.Contains("USD"));
        var template = CertifiedQuickBooksMappingProfile();
        var profile = template with
        {
            ProviderId = id,
            ProfileId = $"{id}-currency-scope",
            AccountMappings = new Dictionary<string, string> { ["100"] = "cash", ["300"] = "capital" },
            DimensionMappings = template.DimensionMappings.Select(mapping => mapping with { ProviderId = id }).ToArray()
        };
        await service.UpsertMappingProfileAsync(new(profile, "accounting-ops", ProviderId: id,
            FundProfileId: "default-fund", LedgerBookId: ExternalGlLedgerBookId,
            EvidenceLinks: [$"approval:external-gl-mapping:{profile.ProfileId}"]));
        var controls = id == "xero"
            ? new[] { "tracking-category-options", "contact-mapping", "tax-rate-mapping", "bank-account-scope" }
            : ["subsidiary-scope", "classification-segments", "entity-mapping", "intercompany-controls"];
        var evidence = controls.Select(control => $"approval:external-gl-provider:{id}:{control}:import:{import.Summary.ImportId}:ledger-book:{ExternalGlLedgerBookId:D}:2026-01-01:2026-01-31").ToArray();
        var package = await service.CreateExportPackageAsync(new("accounting-ops", id, "default-fund", ExternalGlLedgerBookId,
            new(2026, 1, 1), new(2026, 1, 31), profile.ProfileId, RequireBalancedReconciliation: requireBalancedReconciliation,
            EvidenceLinks: [ExportControlEvidence(ExternalGlLedgerBookId, id), .. evidence]));
        package.GeneratedLines.Should().HaveCount(2).And.OnlyContain(line => line.Currency == "USD");
        package.ValidationIssues.Should().Contain(issue => issue.Code == "ExternalGlProviderExportLinesInvalid" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        package.Certification!.State.Should().Be(AccountingCertificationStateDto.Draft);
        await service.Invoking(s => s.CertifyExportPackageAsync(new(package.ExportPackageId, "controller",
            "Currency mismatch cannot be approved as an implicit conversion.",
            [$"approval:external-gl-export-certification:{package.ExportPackageId}:{package.Certification.CertificationId}:ledger-book:{ExternalGlLedgerBookId:D}:2026-01-01:2026-01-31"])))
            .Should().ThrowAsync<InvalidOperationException>();
        var manifest = await service.GetExportPackageManifestAsync(package.ExportPackageId);
        manifest!.GeneratedLines.Should().OnlyContain(line => line.Currency == "USD");
        manifest.ReconciliationSafeguardState.Should().Be(ExternalGlExportReconciliationSafeguardStateDto.Blocked);
        manifest.ExternalPostingAllowed.Should().BeFalse();
    }

    [Theory]
    [InlineData("xero", false)]
    [InlineData("netsuite", false)]
    [InlineData("xero", true)]
    [InlineData("netsuite", true)]
    public async Task LiveProviders_CertifyBalancedGrossAccountActivity_WithoutChangingRetainedAmounts(string id, bool omittedZeroBalances)
    {
        var credentials = new ExternalGlTestStore(id);
        using var handler = new ExternalGlTestHandler((request, body) =>
        {
            if (omittedZeroBalances && request.RequestUri!.AbsolutePath.EndsWith("TrialBalance", StringComparison.Ordinal))
                return ExternalGlTestData.XeroEmptyTrialBalance();
            if (omittedZeroBalances && body.Contains("SUM(", StringComparison.Ordinal))
                return ExternalGlTestHandler.Page([]);
            return ExternalGlTestData.Respond(request, body);
        });
        using var client = new HttpClient(handler);
        var provider = ExternalGlTestData.Provider(id, credentials, client);
        var ledger = CreateMatchedFixtureLedgerStore([
            ("100", LedgerAccountType.Asset, 125.25m, 0m),
            ("300", LedgerAccountType.Equity, 0m, 125.25m),
            ("100", LedgerAccountType.Asset, 0m, omittedZeroBalances ? 125.25m : 25m),
            ("300", LedgerAccountType.Equity, omittedZeroBalances ? 125.25m : 25m, 0m)]);
        var service = CreateService(ledger, provider);
        var import = await service.ImportAsync(ExternalGlTestData.Request(id) with { FundProfileId = "default-fund", TenantId = null, CompanyId = null });
        var template = CertifiedQuickBooksMappingProfile();
        var profile = template with
        {
            ProviderId = id,
            ProfileId = $"{id}-gross-activity",
            AccountMappings = new Dictionary<string, string> { ["100"] = "cash", ["300"] = "capital" },
            DimensionMappings = template.DimensionMappings.Select(m => m with { ProviderId = id }).ToArray()
        };
        await service.UpsertMappingProfileAsync(new(profile, "accounting-ops", ProviderId: id,
            FundProfileId: "default-fund", LedgerBookId: ExternalGlLedgerBookId,
            EvidenceLinks: [$"approval:external-gl-mapping:{profile.ProfileId}"]));
        var controls = id == "xero"
            ? new[] { "tracking-category-options", "contact-mapping", "tax-rate-mapping", "bank-account-scope" }
            : ["subsidiary-scope", "classification-segments", "entity-mapping", "intercompany-controls"];
        var evidence = controls.Select(c => $"approval:external-gl-provider:{id}:{c}:import:{import.Summary.ImportId}:ledger-book:{ExternalGlLedgerBookId:D}:2026-01-01:2026-01-31").ToArray();
        var reconciliation = await service.ReconcileLatestAsync(id, "default-fund", ExternalGlLedgerBookId);
        reconciliation.BreakCount.Should().Be(0);
        var package = await service.CreateExportPackageAsync(new("accounting-ops", id, "default-fund", ExternalGlLedgerBookId,
            new(2026, 1, 1), new(2026, 1, 31), profile.ProfileId, RequireBalancedReconciliation: true,
            EvidenceLinks: [ExportControlEvidence(ExternalGlLedgerBookId, id), .. evidence]));

        package.ValidationIssues.Should().NotContain(i => i.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        package.Certification!.State.Should().Be(AccountingCertificationStateDto.ReadyForReview);
        package.GeneratedLines.Should().ContainSingle(line => line.ExternalAccountId == "cash" && line.Debit == 125.25m && line.Credit == (omittedZeroBalances ? 125.25m : 25m));
        package.GeneratedLines.Should().ContainSingle(line => line.ExternalAccountId == "capital" && line.Debit == (omittedZeroBalances ? 125.25m : 25m) && line.Credit == 125.25m);
        var certified = await service.CertifyExportPackageAsync(new(package.ExportPackageId, "controller",
            "Reviewed gross account activity and matching provider balances.",
            [$"approval:external-gl-export-certification:{package.ExportPackageId}:{package.Certification.CertificationId}:ledger-book:{ExternalGlLedgerBookId:D}:2026-01-01:2026-01-31"]));
        certified!.Certification!.State.Should().Be(AccountingCertificationStateDto.Certified);
        var manifest = await service.GetExportPackageManifestAsync(package.ExportPackageId);
        manifest!.ValidationIssues.Should().NotContain(i => i.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        manifest.GeneratedLines.Should().BeEquivalentTo(package.GeneratedLines);
        manifest.ExternalPostingAllowed.Should().BeFalse();
    }

    [Theory]
    [InlineData("xero")]
    [InlineData("netsuite")]
    public async Task LiveProviders_RequireOwnedControls_AndInvalidateCertificationAfterConnectionChange(string id)
    {
        var credentials = new ExternalGlTestStore(id);
        using var handler = new ExternalGlTestHandler(ExternalGlTestData.Respond);
        using var client = new HttpClient(handler);
        var provider = ExternalGlTestData.Provider(id, credentials, client);
        var ledger = CreateMatchedFixtureLedgerStore([("100", LedgerAccountType.Asset, 100.25m, 0m), ("300", LedgerAccountType.Equity, 0m, 100.25m)]);
        var service = CreateService(ledger, provider);
        var import = await service.ImportAsync(ExternalGlTestData.Request(id) with { FundProfileId = "default-fund", TenantId = null, CompanyId = null });
        var template = CertifiedQuickBooksMappingProfile();
        var profile = template with
        {
            ProviderId = id,
            ProfileId = $"{id}-certified",
            AccountMappings = new Dictionary<string, string> { ["100"] = "cash", ["300"] = "capital" },
            DimensionMappings = template.DimensionMappings.Select(m => m with { ProviderId = id }).ToArray()
        };
        await service.UpsertMappingProfileAsync(new(profile, "accounting-ops", ProviderId: id,
            FundProfileId: "default-fund", LedgerBookId: ExternalGlLedgerBookId,
            EvidenceLinks: [$"approval:external-gl-mapping:{profile.ProfileId}"]));
        var request = new AccountingSystemExportPackageRequestDto("accounting-ops", id, "default-fund", ExternalGlLedgerBookId,
            new(2026, 1, 1), new(2026, 1, 31), profile.ProfileId, RequireBalancedReconciliation: false,
            EvidenceLinks: [ExportControlEvidence(ExternalGlLedgerBookId, id)]);
        var blocked = await service.CreateExportPackageAsync(request);
        blocked.Certification!.State.Should().Be(AccountingCertificationStateDto.Draft);
        blocked.ValidationIssues.Should().Contain(i => i.Code.StartsWith("ExternalGlProviderControlMissing:"));
        var controls = id == "xero"
            ? new[] { "tracking-category-options", "contact-mapping", "tax-rate-mapping", "bank-account-scope" }
            : ["subsidiary-scope", "classification-segments", "entity-mapping", "intercompany-controls"];
        var evidence = controls.Select(c => $"approval:external-gl-provider:{id}:{c}:import:{import.Summary.ImportId}:ledger-book:{ExternalGlLedgerBookId:D}:2026-01-01:2026-01-31").ToArray();
        var package = await service.CreateExportPackageAsync(request with { EvidenceLinks = [.. request.EvidenceLinks, .. evidence] });
        package.ValidationIssues.Should().NotContain(i => i.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        package.Certification!.State.Should().Be(AccountingCertificationStateDto.ReadyForReview);
        var certificationRequest = new CertifyAccountingSystemExportPackageRequestDto(package.ExportPackageId, "controller",
            "Reviewed provider controls and retained account scope.",
            [$"approval:external-gl-export-certification:{package.ExportPackageId}:{package.Certification.CertificationId}:ledger-book:{ExternalGlLedgerBookId:D}:2026-01-01:2026-01-31"]);
        await service.Invoking(s => s.CertifyExportPackageAsync(certificationRequest with { ActionOrigin = OperationsActionOriginDto.AssistantDraft }))
            .Should().ThrowAsync<InvalidOperationException>();
        var certified = await service.CertifyExportPackageAsync(certificationRequest);
        certified!.Certification!.State.Should().Be(AccountingCertificationStateDto.Certified);
        certified.PostingEnabled.Should().BeFalse();
        (await service.GetExportPackageManifestAsync(package.ExportPackageId))!.ExternalPostingAllowed.Should().BeFalse();
        credentials.Values[id == "xero" ? "TenantId" : "SubsidiaryId"] = id == "xero" ? Guid.NewGuid().ToString() : "99";
        var stale = await service.GetExportPackageManifestAsync(package.ExportPackageId);
        stale!.ValidationIssues.Should().Contain(i => i.Code == "ExternalGlProviderImportScopeMismatch");
        stale.ReconciliationSafeguardState.Should().Be(ExternalGlExportReconciliationSafeguardStateDto.Blocked);
        stale.ExternalPostingAllowed.Should().BeFalse();
    }
}
