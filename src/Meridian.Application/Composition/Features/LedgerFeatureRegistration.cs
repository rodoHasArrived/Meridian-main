using Meridian.FinancialOperations.Ledger;
using Meridian.Contracts.Ledger;
using Meridian.Instruments.AssetOperations;
using Meridian.Ledger;
using Meridian.Storage.AssetOperations;
using Meridian.Storage.Ledger;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Meridian.Application.Composition.Features;

/// <summary>
/// Registers core ledger services into the DI container.
/// </summary>
/// <remarks>
/// <para><b>What is registered:</b></para>
/// <list type="bullet">
/// <item><description><see cref="ProjectLedgerBook"/> — a singleton keyed ledger store that manages
/// independent in-memory <c>Meridian.Ledger.Ledger</c> instances per strategy run or project.
/// Consumers resolve it to obtain or create a <c>Meridian.Ledger.Ledger</c> by <see cref="LedgerBookKey"/>
/// without having to manage ledger lifetime themselves.</description></item>
/// </list>
/// <para><b>What is NOT registered here:</b></para>
/// <list type="bullet">
/// <item><description><c>LedgerReadService</c> — lives in <c>Meridian.Strategies</c>, which is not
/// referenced by <c>Meridian.Application</c>. It is registered by UI host startup code.</description></item>
/// <item><description><c>Meridian.Ledger.Ledger</c> itself — created per-run by the backtesting engine and
/// strategy execution layer; it is a domain object, not an injectable singleton.</description></item>
/// </list>
/// </remarks>
internal sealed class LedgerFeatureRegistration : IServiceFeatureRegistration
{
    public IServiceCollection Register(IServiceCollection services, CompositionOptions options)
    {
        // ProjectLedgerBook manages a keyed collection of independent in-memory ledgers.
        // Registering as a singleton means all components within a host process share one
        // namespace, which is the correct model for an in-process trading workstation.
        services.TryAddSingleton(_ => new ProjectLedgerBook("default"));
        services.TryAddSingleton<ProjectLedgerBook>();
        services.TryAddSingleton<IAccountingPolicyService, AccountingPolicyService>();
        services.TryAddSingleton<IAccountingBasisProjectionService, AccountingBasisProjectionService>();
        services.TryAddSingleton<IAccountingJournalDraftService, AccountingJournalDraftService>();
        services.TryAddSingleton<IAccountingPostingCandidateService, AccountingPostingCandidateService>();
        services.TryAddSingleton<IAccountingPostingCandidateWriteBuilder, AccountingPostingCandidateService>();
        services.TryAddSingleton<IAccountingPostingCandidateAuthorityBuilder>(sp =>
            sp.GetRequiredService<IAccountingPostingCandidateWriteBuilder>() as IAccountingPostingCandidateAuthorityBuilder
            ?? throw new InvalidOperationException(
                "The configured accounting posting candidate write builder must also implement " +
                $"{nameof(IAccountingPostingCandidateAuthorityBuilder)}."));
        // Corporate-action accounting remains a two-step, deterministic preparation boundary:
        // the projector produces reviewed economic/lot/posting intent and the mapper adapts a
        // promoted rule-pack result to an Asset Accounting Event Spine request. Neither service
        // appends a journal or bypasses the spine's policy, evidence, period, and approval gates.
        services.TryAddSingleton<CorporateActionAccountingProjectionService>();
        services.TryAddSingleton<ICorporateActionAccountingProjectionService>(sp =>
            sp.GetRequiredService<CorporateActionAccountingProjectionService>());
        services.TryAddSingleton<CorporateActionAssetAccountingEventMapper>();
        services.TryAddSingleton<ICorporateActionAssetAccountingEventMapper>(sp =>
            sp.GetRequiredService<CorporateActionAssetAccountingEventMapper>());
        services.TryAddSingleton<IAssetAccountingEventSpineService>(sp =>
            AssetAccountingEventSpineService.TryCreate(
                sp.GetService<IAssetAccountingEventProjectionStore>(),
                sp.GetService<IInstrumentPositionProjectionStore>(),
                sp.GetService<Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService>(),
                sp.GetService<ILedgerBookService>(),
                sp.GetService<IAccountingPolicyService>(),
                sp.GetService<IAccountingConfigurationService>(),
                sp.GetService<IAccountingPostingCandidateAuthorityBuilder>(),
                sp.GetService<ILedgerJournalStore>())!);
        services.TryAddSingleton<IAccountingPostingCandidatePostService>(sp =>
            new AccountingPostingCandidatePostService(
                sp.GetRequiredService<IAccountingPostingCandidateWriteBuilder>(),
                sp.GetService<ILedgerJournalStore>(),
                sp.GetService<IAtomicTaxLotJournalStore>(),
                sp.GetService<IAssetAccountingEventProjectionStore>(),
                sp.GetRequiredService<IAccountingPostingCandidateAuthorityBuilder>()));
        services.TryAddSingleton<IAccountingBasisProjectionSetService, AccountingBasisProjectionSetService>();

        return services;
    }
}
