using System.Globalization;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.AccountingClose;
using Meridian.FinancialOperations.Ledger;
using Meridian.FinancialOperations.PrivateCapital;
using Meridian.Identity.Auth;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using static Meridian.Contracts.Text.TextPrimitives;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class LedgerEndpoints
{
    private static ILedgerBookService? ResolveService(HttpContext context)
        => context.RequestServices.GetService<ILedgerBookService>();

    private static ILedgerJournalStore? ResolveJournalStore(HttpContext context)
        => context.RequestServices.GetService<ILedgerJournalStore>();

    private static IAccountingCloseManagementService? ResolveAccountingCloseManagementService(HttpContext context)
        => context.RequestServices.GetService<IAccountingCloseManagementService>();

    private static IAccountingConfigurationService? ResolveAccountingConfigurationService(HttpContext context)
    {
        var service = context.RequestServices.GetService<IAccountingConfigurationService>();
        if (service is not null)
        {
            return service;
        }

        var store = context.RequestServices.GetService<IAccountingConfigurationStore>();
        var auditStore = context.RequestServices.GetService<IAccountingActionAuditStore>();
        return store is null || auditStore is null
            ? null
            : new AccountingConfigurationService(
                store,
                auditStore,
                context.RequestServices.GetService<ILedgerBookService>());
    }

    private static IAccountingPostingCandidateService? ResolveAccountingPostingCandidateService(HttpContext context)
        => context.RequestServices.GetService<IAccountingPostingCandidateService>();

    private static IAssetAccountingEventSpineService? ResolveAssetAccountingEventSpineService(HttpContext context)
        => context.RequestServices.GetService<IAssetAccountingEventSpineService>();

    private static IAccountingPostingCandidatePostService? ResolveAccountingPostingCandidatePostService(HttpContext context)
        => context.RequestServices.GetService<IAccountingPostingCandidatePostService>();

    private static IAccountingBasisProjectionSetService? ResolveAccountingBasisProjectionSetService(HttpContext context)
        => context.RequestServices.GetService<IAccountingBasisProjectionSetService>();

    private static IManualJournalEntryWorkbenchService? ResolveManualJournalEntryWorkbenchService(HttpContext context)
    {
        var service = context.RequestServices.GetService<IManualJournalEntryWorkbenchService>();
        if (service is not null)
        {
            return service;
        }

        var draftStore = context.RequestServices.GetService<IManualJournalEntryDraftStore>();
        var configurationService = ResolveAccountingConfigurationService(context);
        var auditStore = context.RequestServices.GetService<IAccountingActionAuditStore>();
        return draftStore is null || configurationService is null || auditStore is null
            ? null
            : new ManualJournalEntryWorkbenchService(
                draftStore,
                configurationService,
                auditStore,
                 context.RequestServices.GetService<Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService>(),
                 context.RequestServices.GetService<ILedgerJournalStore>(),
                 context.RequestServices.GetService<ReportPackWorkflowService>(),
                 postingTarget: context.RequestServices.GetService<IGovernedLedgerPostingTarget>(),
                 evidenceArtifacts: context.RequestServices.GetService<Meridian.Ui.Shared.Evidence.IEvidenceArtifactStore>(),
                 assetCandidatePostingFactory: () => context.RequestServices.GetService<IAccountingPostingCandidatePostService>(),
                 assetAccountingSourceFactory: () => ResolveAssetAccountingEventSpineService(context));
    }

    private static IManualJournalEntryLifecycleService? ResolveManualJournalEntryLifecycleService(HttpContext context)
    {
        var service = context.RequestServices.GetService<IManualJournalEntryLifecycleService>();
        if (service is not null)
        {
            return service;
        }

        return ResolveManualJournalEntryWorkbenchService(context) as IManualJournalEntryLifecycleService;
    }

    private static ICapitalAccountWorkbenchService? ResolveCapitalAccountWorkbenchService(HttpContext context)
    {
        var service = context.RequestServices.GetService<ICapitalAccountWorkbenchService>();
        if (service is not null)
        {
            return service;
        }

        var manualJournalService = ResolveManualJournalEntryWorkbenchService(context);
        return manualJournalService is null
            ? null
            : new CapitalAccountWorkbenchService(
                manualJournalService,
                context.RequestServices.GetService<ReportPackWorkflowService>());
    }

    private static IPrivateCapitalFundEventCommandCenterService? ResolvePrivateCapitalFundEventCommandCenterService(HttpContext context)
    {
        var service = context.RequestServices.GetService<IPrivateCapitalFundEventCommandCenterService>();
        if (service is not null)
        {
            return service;
        }

        var manualJournalService = ResolveManualJournalEntryWorkbenchService(context);
        return manualJournalService is null
            ? null
            : new PrivateCapitalFundEventCommandCenterService(manualJournalService);
    }

    private static IResult ServiceUnavailable()
        => ServiceUnavailable("Ledger book service is not registered.");

    private static IResult ServiceUnavailable(string detail)
        => Results.Problem(detail, statusCode: StatusCodes.Status501NotImplemented);


    private static IResult MapServiceException(LedgerBookServiceException exception)
        => exception switch
        {
            LedgerBookNotFoundException => Results.NotFound(new { error = exception.Message }),
            LedgerBookValidationException or LedgerPeriodTransitionException => Results.BadRequest(new { error = exception.Message }),
            _ => Results.Problem(exception.Message)
        };
}
