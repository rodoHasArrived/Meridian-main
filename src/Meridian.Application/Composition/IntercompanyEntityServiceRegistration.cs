using Meridian.Application.FundStructure;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Services;
using Meridian.Contracts.Tenancy;
using Meridian.FinancialOperations.Consolidation;
using Meridian.FinancialOperations.Intercompany;
using Meridian.FinancialOperations.Ledger;
using Meridian.Storage;
using Meridian.Storage.FundStructure;
using Meridian.Storage.Ledger;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Meridian.Application.Composition;

/// <summary>Shared authority and persistence for CLI, browser and desktop hosts.</summary>
public static class IntercompanyEntityServiceRegistration
{
    public static IServiceCollection AddIntercompanyEntityServices(this IServiceCollection services,
        CompositionConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        if (services.Any(x => x.ServiceType == typeof(RegistrationMarker))) return services;
        services.AddSingleton(new RegistrationMarker());
        services.AddFundScopeTenantServices();
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);

        var hasFundStructureDatabase = configuration.IsConfigured(FundStructureStartup.ConnectionStringVariable);
        var hasLedgerDatabase = configuration.IsConfigured(LedgerStartup.ConnectionStringVariable);
        var isProduction = ProductionServiceRegistrationPolicy.IsProductionComposition(services);
        if (hasFundStructureDatabase)
        {
            services.TryAddSingleton<IEntityLifecycleStore, PostgresEntityLifecycleStore>();
            services.TryAddSingleton<IIntercompanyAccountingArrangementStore, PostgresIntercompanyAccountingArrangementStore>();
        }
        else
        {
            services.TryAddSingleton<IEntityLifecycleStore>(sp => new FileEntityLifecycleStore(
                Path.Combine(Root(sp), "governance", "entity-lifecycle")));
            services.TryAddSingleton<IIntercompanyAccountingArrangementStore>(sp => new FileIntercompanyAccountingArrangementStore(
                Path.Combine(Root(sp), "governance", "intercompany-accounting-arrangements")));
        }

        if (hasLedgerDatabase)
        {
            services.TryAddSingleton<IIntercompanyAccountBindingStore, PostgresIntercompanyAccountBindingStore>();
            services.TryAddSingleton<IConsolidationFxStore, ConsolidationFxPostgresStore>();
            services.TryAddSingleton<IConsolidationTransitionStore, ConsolidationTransitionPostgresStore>();
            services.TryAddSingleton<LegacyIntercompanyAttributionValidator>();
            services.TryAddSingleton<ILegacyIntercompanyAttributionStore, PostgresLegacyIntercompanyAttributionStore>();
            services.TryAddSingleton<IIntercompanyProposalStore, PostgresIntercompanyProposalStore>();
            services.TryAddSingleton<ILedgerBookControlStore>(sp => sp.GetService<ILedgerJournalStore>() as ILedgerBookControlStore ?? null!);
            services.TryAddSingleton<IControlledCloseAuthorizationStore>(sp => sp.GetService<ILedgerJournalStore>() as IControlledCloseAuthorizationStore ?? null!);
        }
        else
        {
            services.TryAddSingleton<IConsolidationFxStore>(sp => new ConsolidationFxFileStore(
                Path.Combine(Root(sp), "accounting", "consolidation-fx.json")));
            services.TryAddSingleton<IIntercompanyAccountBindingStore>(sp =>
                sp.GetService<ILedgerJournalStore>() is { } journals &&
                sp.GetService<IAccountingConfigurationStore>() is { } chart &&
                sp.GetService<IFundProfileTenancyRegistry>() is { } owners
                    ? new FileIntercompanyAccountBindingStore(Path.Combine(Root(sp), "accounting", "intercompany-bindings.json"),
                        sp.GetRequiredService<IFundScopeTenantAccessor>(), chart, journals, owners)
                    : null!);
            services.TryAddSingleton<LegacyIntercompanyAttributionValidator>(sp =>
                sp.GetService<ILedgerJournalStore>() is { } journals &&
                sp.GetService<IIntercompanyAccountBindingStore>() is { } bindings
                    ? new LegacyIntercompanyAttributionValidator(journals, bindings, sp.GetRequiredService<IIntercompanyAccountingArrangementStore>(),
                        sp.GetRequiredService<IIntercompanyAccountingArrangementLegalAuthority>())
                    : null!);
            services.TryAddSingleton<ILegacyIntercompanyAttributionStore>(sp =>
                sp.GetService<LegacyIntercompanyAttributionValidator>() is { } validator
                    ? new FileLegacyIntercompanyAttributionStore(Path.Combine(Root(sp), "accounting", "intercompany-attributions.json"),
                        sp.GetRequiredService<IFundScopeTenantAccessor>(), validator)
                    : null!);
            services.TryAddSingleton<IConsolidationTransitionStore>(sp =>
                sp.GetService<ILedgerJournalStore>() is { } journals &&
                sp.GetService<IFundProfileTenancyRegistry>() is { } owners
                    ? new ConsolidationTransitionFileStore(Path.Combine(Root(sp), "accounting", "consolidation-transitions.json"),
                        sp.GetRequiredService<IFundScopeTenantAccessor>(), journals, owners) : null!);
            // A local host without a journal database cannot promise atomic source/outbox delivery.
            // No substitute proposal store is registered; its accounting routes return unavailable.
        }

        services.TryAddSingleton<IConsolidationFxAuthority, ConsolidationFxAuthority>();
        services.TryAddSingleton<IConsolidationBookPolicyValidator>(sp =>
            sp.GetService<IAccountingPolicyService>() is { } policies
                ? new ConsolidationBookPolicyValidator(policies) : null!);
        services.TryAddSingleton<IIntercompanyAccountingArrangementLegalAuthority>(sp =>
            sp.GetService<IIntercompanyAgreementStore>() is { } legalStore
                ? new IntercompanyAccountingArrangementLegalAuthority(legalStore)
                : new UnavailableCanonicalLegalAuthority());
        services.TryAddSingleton<IIntercompanyAccountingArrangementScopeAuthority>(sp =>
        {
            if (sp.GetService<IFundStructureService>() is not { } structures ||
                sp.GetService<ILedgerJournalStore>() is not { } journals ||
                sp.GetService<IAccountingConfigurationStore>() is not { } chart ||
                sp.GetService<IFundProfileTenancyRegistry>() is not { } owners)
                return new UnavailableAgreementScopeAuthority();
            return new FundStructureIntercompanyAccountingArrangementScopeAuthority(structures,
                sp.GetService<IFundStructureStore>(), journals, chart,
                sp.GetRequiredService<IEntityLifecycleStore>(), owners,
                allowUnpartitionedSyntheticStore: !isProduction && !hasFundStructureDatabase,
                timeProvider: sp.GetRequiredService<TimeProvider>());
        });
        services.TryAddSingleton<IntercompanyAccountingArrangementService>();
        services.TryAddSingleton<IntercompanyBalanceScheduleService>(sp =>
            sp.GetService<ILedgerJournalStore>() is { } journals &&
            sp.GetService<IIntercompanyAccountBindingStore>() is { } bindings &&
            sp.GetService<IAccountingConfigurationStore>() is { } chart &&
            sp.GetService<IFundProfileTenancyRegistry>() is { } owners
                ? new IntercompanyBalanceScheduleService(journals, sp.GetRequiredService<IntercompanyAccountingArrangementService>(),
                    bindings, chart, owners, sp.GetRequiredService<IIntercompanyAccountingArrangementStore>(),
                    sp.GetService<ILegacyIntercompanyAttributionStore>()) : null!);
        services.TryAddSingleton<IIntercompanyBalanceScheduleSource>(sp => sp.GetService<IntercompanyBalanceScheduleService>()!);
        services.TryAddSingleton<IEntityLifecycleService, EntityLifecycleService>();

        if (hasLedgerDatabase)
        {
            services.TryAddSingleton<LifecycleLedgerPostingAuthority>(sp => new LifecycleLedgerPostingAuthority(
                sp.GetRequiredService<IFundStructureService>(), sp.GetRequiredService<IEntityLifecycleStore>(),
                () => sp.GetService<IControlledCloseAuthorizationStore>(),
                sp.GetService<IFundStructureStore>() is PostgresFundStructureStore structure
                    ? structure.AcquireConsolidationAuthorityLeaseAsync : null));
            services.TryAddSingleton<LifecycleControlSelection>(sp =>
            {
                var structure = sp.GetRequiredService<IFundStructureService>();
                var unavailable = !hasFundStructureDatabase && (isProduction ||
                    structure is Meridian.Application.Tenancy.TenantGuardedLocalFundStructureService { RefusesUnattributedAccess: true });
                return new(unavailable ? new UnavailableEntityLifecycleAuthority()
                    : sp.GetRequiredService<LifecycleLedgerPostingAuthority>());
            });
            services.TryAddSingleton<IntercompanyLedgerPostingAuthority>();
            services.AddSingleton<ILedgerPostingAuthority>(sp => sp.GetRequiredService<LifecycleControlSelection>().Authority);
            services.AddSingleton<ILedgerPostingAuthority>(sp => sp.GetRequiredService<IntercompanyLedgerPostingAuthority>());
            services.AddSingleton<ILedgerBookControlPreflight>(sp => (ILedgerBookControlPreflight)sp.GetRequiredService<LifecycleControlSelection>().Authority);
            services.AddSingleton<ILedgerBookControlPreflight>(sp => sp.GetRequiredService<IntercompanyLedgerPostingAuthority>());
        }
        services.TryAddSingleton<Func<IEnumerable<ILedgerPostingAuthority>>>(sp =>
            () => sp.GetServices<ILedgerPostingAuthority>());
        services.TryAddSingleton<Func<IEnumerable<ILedgerBookControlPreflight>>>(sp =>
            () => sp.GetServices<ILedgerBookControlPreflight>());
        services.TryAddSingleton<Func<IIntercompanyAgreementPostingAuthority?>>(sp =>
            () => sp.GetService<IIntercompanyAgreementPostingAuthority>());
        services.TryAddSingleton<Func<IConsolidationPostingAuthority?>>(sp =>
            () => sp.GetService<IConsolidationPostingAuthority>());
        if (hasLedgerDatabase && hasFundStructureDatabase)
        {
            services.TryAddSingleton<ConsolidationService>(sp =>
                sp.GetService<IFundStructureService>() is PostgresFundStructureService structure &&
                sp.GetService<ILedgerJournalStore>() is PostgresLedgerJournalStore journals &&
                sp.GetService<IAccountingPolicyService>() is { } policy &&
                sp.GetService<IAccountingJournalDraftService>() is { } drafts
                    ? new ConsolidationService(new ConsolidationPerimeterResolver(structure), journals, policy, drafts,
                        sp.GetService<IIntercompanyAccountBindingStore>(), sp.GetService<IConsolidationFxAuthority>(),
                        sp.GetService<ILegacyIntercompanyAttributionStore>(), sp.GetService<IConsolidationTransitionStore>(),
                        sp.GetService<IntercompanyAccountingArrangementService>()) : null!);
            services.TryAddSingleton<IConsolidationDraftGuard>(sp => sp.GetService<ConsolidationService>()!);
            services.TryAddSingleton<IConsolidationPostingAuthority>(sp =>
                sp.GetService<ConsolidationService>() is { } consolidation &&
                sp.GetService<IFundStructureStore>() is PostgresFundStructureStore structure &&
                sp.GetService<IAccountingPolicyService>() is { } policy
                    ? new PostgresConsolidationPostingAuthority(consolidation, structure, policy,
                        sp.GetService<IIntercompanyAccountBindingStore>(), sp.GetService<IConsolidationFxAuthority>(),
                        sp.GetService<ILegacyIntercompanyAttributionStore>(), sp.GetService<IConsolidationTransitionStore>(),
                        sp.GetService<Meridian.Storage.FundStructure.IIntercompanyAccountingArrangementStore>(),
                        sp.GetService<IIntercompanyAccountingArrangementLegalAuthority>()) : null!);
        }
        services.TryAddSingleton<ConsolidationTransitionService>(sp =>
            sp.GetService<ConsolidationService>() is { } consolidation &&
            sp.GetService<ILedgerJournalStore>() is { } journals &&
            sp.GetService<IConsolidationTransitionStore>() is { } transitions &&
            sp.GetService<IAccountingPolicyService>() is { } policies &&
            sp.GetService<IIntercompanyAccountBindingStore>() is { } bindings &&
            sp.GetService<ILegacyIntercompanyAttributionStore>() is { } legacy &&
            sp.GetService<IFundProfileTenancyRegistry>() is { } owners
                ? new ConsolidationTransitionService(consolidation, journals, transitions, policies, bindings,
                    sp.GetRequiredService<IConsolidationFxAuthority>(), legacy, owners,
                    sp.GetService<IFundStructureStore>() as PostgresFundStructureStore,
                    sp.GetRequiredService<TimeProvider>(),
                    sp.GetService<IIntercompanyAccountingArrangementStore>(),
                    sp.GetService<IIntercompanyAccountingArrangementLegalAuthority>()) : null!);
        return services;
    }

    private static string Root(IServiceProvider provider)
        => provider.GetService<StorageOptions>()?.RootPath ?? Path.Combine(AppContext.BaseDirectory, "data");
    private sealed class RegistrationMarker;
    private sealed record LifecycleControlSelection(ILedgerPostingAuthority Authority);

    /// <summary>Records missing new authority; the ledger's retained book mode decides warning versus refusal.</summary>
    private sealed class UnavailableEntityLifecycleAuthority : ILedgerPostingAuthority, ILedgerBookControlPreflight
    {
        public string ControlName => "entity-lifecycle";
        private static LedgerControlDecisionDto Decision() => new("entity-lifecycle", "lifecycle-authority-unavailable", false,
            "Tenant-partitioned entity lifecycle authority is unavailable; configure it and migrate this book before enforcement.");
        public Task<IAsyncDisposable> AcquireAsync(IReadOnlyList<LedgerPostingAuthorityContext> contexts, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            foreach (var context in contexts) context.Decisions.Add(Decision());
            return Task.FromResult<IAsyncDisposable>(EmptyLedgerAuthorityLease.Instance);
        }
        public Task<LedgerBookControlPreflightLease> CheckAsync(LedgerBookRecord book, string? tenantId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new LedgerBookControlPreflightLease([Decision()], EmptyLedgerAuthorityLease.Instance));
        }
    }

    private sealed class UnavailableCanonicalLegalAuthority : IIntercompanyAccountingArrangementLegalAuthority
    {
        private static InvalidOperationException Unavailable() => new("Configure the canonical tenant-partitioned legal agreement registry before using accounting profiles.");
        public Task<IntercompanyAgreementRevisionDto> GetRevisionAsync(string tenantId, IntercompanyCanonicalAgreementReferenceDto reference, CancellationToken ct = default)
            => Task.FromException<IntercompanyAgreementRevisionDto>(Unavailable());
        public Task<IntercompanyAgreementRevisionDto> GetEffectiveRevisionAsync(string tenantId, IntercompanyCanonicalAgreementReferenceDto reference, DateOnly economicDate, CancellationToken ct = default)
            => Task.FromException<IntercompanyAgreementRevisionDto>(Unavailable());
        public Task<IReadOnlyList<IntercompanyAgreementRevisionDto>> ListEffectiveRevisionsAsync(string tenantId, Guid entityId, DateOnly economicDate, CancellationToken ct = default)
            => Task.FromException<IReadOnlyList<IntercompanyAgreementRevisionDto>>(Unavailable());
        public Task<IIntercompanyAccountingArrangementLegalLease> AcquireLeaseAsync(string tenantId, IReadOnlyList<IntercompanyCanonicalAgreementReferenceDto> references, LegalEntityReadAuthorityProof? structureAuthority = null, CancellationToken ct = default)
            => Task.FromException<IIntercompanyAccountingArrangementLegalLease>(Unavailable());
    }

    private sealed class UnavailableAgreementScopeAuthority : IIntercompanyAccountingArrangementScopeAuthority
    {
        private static InvalidOperationException Unavailable()
            => new("Agreement changes require authoritative entity, ledger-book, chart and ownership services.");
        public Task<IIntercompanyAccountingArrangementScopeLease> AcquireLeaseAsync(string tenantId, ResolvedIntercompanyAccountingArrangementDto agreement, CancellationToken ct = default)
            => Task.FromException<IIntercompanyAccountingArrangementScopeLease>(Unavailable());
        public Task ValidateAsync(string tenantId, ResolvedIntercompanyAccountingArrangementDto agreement, bool requireActive, CancellationToken ct = default)
            => Task.FromException(Unavailable());
        public Task<IReadOnlyList<IntercompanyEntityLifecycleReferenceDto>> GetLifecycleEvidenceAsync(string tenantId, ResolvedIntercompanyAccountingArrangementDto agreement, CancellationToken ct = default)
            => Task.FromException<IReadOnlyList<IntercompanyEntityLifecycleReferenceDto>>(Unavailable());
    }
}
