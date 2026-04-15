using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Application.EnvironmentDesign;
using Meridian.Contracts.EnvironmentDesign;
using Meridian.Application.FundStructure;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Services;
using Meridian.Wpf.Models;

namespace Meridian.Wpf.Services;

public sealed partial class WorkstationOperatingContextService
{
    private readonly FundContextService _fundContextService;
    private readonly IFundStructureService? _fundStructureService;
    private readonly IEnvironmentRuntimeProjectionService? _environmentRuntimeProjectionService;
    private readonly string _storagePath;
    private readonly List<WorkstationOperatingContext> _contexts = new();
    private bool _loaded;

    public WorkstationOperatingContextService(
        FundContextService fundContextService,
        IFundStructureService? fundStructureService = null,
        IEnvironmentRuntimeProjectionService? environmentRuntimeProjectionService = null,
        string? storagePath = null)
    {
        _fundContextService = fundContextService ?? throw new ArgumentNullException(nameof(fundContextService));
        _fundStructureService = fundStructureService;
        _environmentRuntimeProjectionService = environmentRuntimeProjectionService;
        _storagePath = storagePath ?? GetDefaultStoragePath();
    }

    public IReadOnlyList<WorkstationOperatingContext> Contexts => _contexts.AsReadOnly();

    public WorkstationOperatingContext? CurrentContext { get; private set; }

    public string? LastSelectedOperatingContextKey { get; private set; }

    public BoundedWindowMode CurrentWindowMode { get; private set; } = BoundedWindowMode.DockFloat;

    public string? CurrentLayoutPresetId { get; private set; }

    public event EventHandler<WorkstationOperatingContextChangingEventArgs>? ActiveContextChanging;

    public event EventHandler<WorkstationOperatingContextChangedEventArgs>? ActiveContextChanged;

    public event EventHandler? ContextCatalogChanged;

    public event EventHandler? ContextSwitchRequested;

    public event EventHandler? WindowModeChanged;

    public async Task LoadAsync(CancellationToken ct = default)
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        await _fundContextService.LoadAsync(ct).ConfigureAwait(false);
        await LoadSettingsAsync(ct).ConfigureAwait(false);
        await RefreshContextsAsync(ct).ConfigureAwait(false);

        if (CurrentContext is not null)
        {
            return;
        }

        var targetKey = LastSelectedOperatingContextKey;
        if (string.IsNullOrWhiteSpace(targetKey) &&
            !string.IsNullOrWhiteSpace(_fundContextService.LastSelectedFundProfileId))
        {
            targetKey = WorkstationOperatingContext.CreateContextKey(
                OperatingContextScopeKind.Fund,
                _fundContextService.LastSelectedFundProfileId!);
        }

        if (!string.IsNullOrWhiteSpace(targetKey))
        {
            await SelectContextAsync(targetKey, raiseChanging: false, raiseChanged: false, ct: ct).ConfigureAwait(false);
        }
    }

    public async Task RefreshContextsAsync(CancellationToken ct = default)
    {
        await _fundContextService.LoadAsync(ct).ConfigureAwait(false);

        var contexts = new Dictionary<string, WorkstationOperatingContext>(StringComparer.OrdinalIgnoreCase);
        foreach (var context in BuildFundProfileContexts(_fundContextService.Profiles))
        {
            contexts[context.ContextKey] = context;
        }

        if (_environmentRuntimeProjectionService is not null)
        {
            try
            {
                var runtime = await _environmentRuntimeProjectionService
                    .GetCurrentRuntimeAsync(ct: ct)
                    .ConfigureAwait(false);

                if (runtime is not null)
                {
                    foreach (var context in await BuildRuntimeContextsAsync(runtime, _fundContextService.Profiles, ct).ConfigureAwait(false))
                    {
                        contexts[context.ContextKey] = context;
                    }
                }
            }
            catch
            {
            }
        }

        if (_fundStructureService is not null && (_environmentRuntimeProjectionService is null || contexts.Count == 0))
        {
            try
            {
                var graph = await _fundStructureService
                    .GetOrganizationStructureAsync(new OrganizationStructureQuery(), ct)
                    .ConfigureAwait(false);

                foreach (var context in await BuildGraphContextsAsync(graph, _fundContextService.Profiles, runtimeLedgerGroups: null, ct).ConfigureAwait(false))
                {
                    contexts[context.ContextKey] = context;
                }
            }
            catch
            {
            }
        }

        _contexts.Clear();
        _contexts.AddRange(contexts.Values
            .OrderBy(static context => context.ScopeKind)
            .ThenBy(static context => context.DisplayName, StringComparer.OrdinalIgnoreCase));

        if (CurrentContext is not null)
        {
            CurrentContext = _contexts.FirstOrDefault(context =>
                string.Equals(context.ContextKey, CurrentContext.ContextKey, StringComparison.OrdinalIgnoreCase))
                ?? CurrentContext;
        }

        ContextCatalogChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<WorkstationOperatingContext?> SelectContextAsync(
        string contextKey,
        bool raiseChanging = true,
        bool raiseChanged = true,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contextKey);
        await LoadAsync(ct).ConfigureAwait(false);

        var nextContext = _contexts.FirstOrDefault(context =>
            string.Equals(context.ContextKey, contextKey, StringComparison.OrdinalIgnoreCase));
        if (nextContext is null)
        {
            return null;
        }

        if (CurrentContext is not null &&
            string.Equals(CurrentContext.ContextKey, nextContext.ContextKey, StringComparison.OrdinalIgnoreCase))
        {
            return CurrentContext;
        }

        if (raiseChanging)
        {
            ActiveContextChanging?.Invoke(this, new WorkstationOperatingContextChangingEventArgs(CurrentContext, nextContext));
        }

        await SynchronizeCompatibilityFundAsync(nextContext, ct).ConfigureAwait(false);

        CurrentContext = nextContext with { LastOpenedAt = DateTimeOffset.UtcNow };
        LastSelectedOperatingContextKey = CurrentContext.ContextKey;
        CurrentLayoutPresetId = CurrentContext.DefaultWindowPresetId;
        await SaveSettingsAsync(ct).ConfigureAwait(false);
        if (raiseChanged)
        {
            ActiveContextChanged?.Invoke(this, new WorkstationOperatingContextChangedEventArgs(CurrentContext));
        }

        return CurrentContext;
    }

    public async Task SetWindowModeAsync(BoundedWindowMode windowMode, string? presetId = null, CancellationToken ct = default)
    {
        if (CurrentWindowMode == windowMode && string.Equals(CurrentLayoutPresetId, presetId, StringComparison.Ordinal))
        {
            return;
        }

        CurrentWindowMode = windowMode;
        if (!string.IsNullOrWhiteSpace(presetId))
        {
            CurrentLayoutPresetId = presetId;
        }
        else if (windowMode == BoundedWindowMode.WorkbenchPreset)
        {
            CurrentLayoutPresetId ??= CurrentContext?.DefaultWindowPresetId ?? "trading-cockpit";
        }

        await SaveSettingsAsync(ct).ConfigureAwait(false);
        WindowModeChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RequestSwitchContext() => ContextSwitchRequested?.Invoke(this, EventArgs.Empty);

    public string? GetActiveScopeKey() => CurrentContext?.ContextKey ?? _fundContextService.CurrentFundProfile?.FundProfileId;

    public string GetCurrentModeDisplayName() => CurrentWindowMode switch
    {
        BoundedWindowMode.Focused => "Focused",
        BoundedWindowMode.WorkbenchPreset => string.IsNullOrWhiteSpace(CurrentLayoutPresetId)
            ? "Workbench Preset"
            : $"Preset · {HumanizePresetId(CurrentLayoutPresetId)}",
        _ => "Dock + Float"
    };

    public async Task SeedHybridSampleAsync(CancellationToken ct = default)
    {
        await _fundContextService.UpsertProfileAsync(new FundProfileDetail(
            FundProfileId: "northwind-income",
            DisplayName: "Northwind Income",
            LegalEntityName: "Northwind Income Fund LP",
            BaseCurrency: "USD",
            DefaultWorkspaceId: "governance",
            DefaultLandingPageTag: "GovernanceShell",
            DefaultLedgerScope: FundLedgerScope.Consolidated,
            IsDefault: !_fundContextService.Profiles.Any(static profile => profile.IsDefault)),
            ct).ConfigureAwait(false);

        if (_fundStructureService is not null)
        {
            await SeedHybridGraphAsync(ct).ConfigureAwait(false);
        }

        await RefreshContextsAsync(ct).ConfigureAwait(false);
    }

    private async Task SeedHybridGraphAsync(CancellationToken ct)
    {
        var organizationId = Guid.Parse("0d6b6e74-88af-44df-a718-6147b3790cf7");
        var businessId = Guid.Parse("bf705d1f-ca05-4658-84aa-cdd69a01c4d4");
        var clientId = Guid.Parse("86e71237-b2be-4544-b1b8-77dbfa2c3830");
        var fundId = Guid.Parse("2c4eaa0f-2768-4fe1-bdb2-b4136610891d");
        var entityId = Guid.Parse("911d0237-5b94-4d31-b594-9d2eec3098f3");
        var clientPortfolioId = Guid.Parse("9784b11e-2dbc-48c3-9dc9-a5a05ad9d0de");
        var fundPortfolioId = Guid.Parse("9118d218-afcf-49cb-a6f5-dd248b581c82");
        var effectiveFrom = new DateTimeOffset(2026, 1, 2, 9, 0, 0, TimeSpan.Zero);
        const string createdBy = "codex";

        await TryExecuteAsync(
            () => _fundStructureService!.CreateOrganizationAsync(
                new CreateOrganizationRequest(
                    organizationId,
                    "NW-ORG",
                    "Northwind Advisory Group",
                    "USD",
                    effectiveFrom,
                    createdBy,
                    "Hybrid advisory and fund operating sample organization."),
                ct)).ConfigureAwait(false);

        await TryExecuteAsync(
            () => _fundStructureService!.CreateBusinessAsync(
                new CreateBusinessRequest(
                    businessId,
                    organizationId,
                    BusinessKindDto.Hybrid,
                    "NW-HYB",
                    "Northwind Hybrid",
                    "USD",
                    effectiveFrom,
                    createdBy,
                    "Shared advisory and fund operating business."),
                ct)).ConfigureAwait(false);

        await TryExecuteAsync(
            () => _fundStructureService!.CreateClientAsync(
                new CreateClientRequest(
                    clientId,
                    businessId,
                    "CFO-001",
                    "Canyon Family Office",
                    "USD",
                    effectiveFrom,
                    createdBy,
                    "Advisory client with shared accounting visibility.",
                    ClientSegmentKind.FamilyOffice),
                ct)).ConfigureAwait(false);

        await TryExecuteAsync(
            () => _fundStructureService!.CreateLegalEntityAsync(
                new CreateLegalEntityRequest(
                    entityId,
                    LegalEntityTypeDto.Fund,
                    "NW-ENT",
                    "Northwind Income Fund LP",
                    "Delaware",
                    "USD",
                    effectiveFrom,
                    createdBy,
                    "Primary operating legal entity."),
                ct)).ConfigureAwait(false);

        await TryExecuteAsync(
            () => _fundStructureService!.CreateFundAsync(
                new CreateFundRequest(
                    fundId,
                    "NW-INCOME",
                    "Northwind Income",
                    "USD",
                    effectiveFrom,
                    createdBy,
                    "Hybrid operating income fund.",
                    businessId),
                ct)).ConfigureAwait(false);

        await TryExecuteAsync(
            () => _fundStructureService!.CreateInvestmentPortfolioAsync(
                new CreateInvestmentPortfolioRequest(
                    clientPortfolioId,
                    businessId,
                    "CANYON-MANDATE",
                    "Canyon Income Mandate",
                    "USD",
                    effectiveFrom,
                    createdBy,
                    ClientId: clientId,
                    Description: "Client advisory mandate portfolio."),
                ct)).ConfigureAwait(false);

        await TryExecuteAsync(
            () => _fundStructureService!.CreateInvestmentPortfolioAsync(
                new CreateInvestmentPortfolioRequest(
                    fundPortfolioId,
                    businessId,
                    "NW-FUND-PORT",
                    "Northwind Income Master Portfolio",
                    "USD",
                    effectiveFrom,
                    createdBy,
                    FundId: fundId,
                    EntityId: entityId,
                    Description: "Fund operating portfolio."),
                ct)).ConfigureAwait(false);
    }

    private async Task SynchronizeCompatibilityFundAsync(WorkstationOperatingContext context, CancellationToken ct)
    {
        var fundProfileId = context.CompatibilityFundProfileId;
        if (string.IsNullOrWhiteSpace(fundProfileId) &&
            context.ScopeKind == OperatingContextScopeKind.Fund)
        {
            fundProfileId = context.ScopeId;
        }

        if (!string.IsNullOrWhiteSpace(fundProfileId))
        {
            await _fundContextService.SelectFundProfileAsync(fundProfileId, ct).ConfigureAwait(false);
            return;
        }

        _fundContextService.ClearCurrentFund();
    }

    private async Task<IReadOnlyList<WorkstationOperatingContext>> BuildGraphContextsAsync(
        OrganizationStructureGraphDto graph,
        IReadOnlyList<FundProfileDetail> profiles,
        IReadOnlyList<EnvironmentLedgerGroupRuntimeDto>? runtimeLedgerGroups,
        CancellationToken ct)
    {
        var contexts = new Dictionary<string, WorkstationOperatingContext>(StringComparer.OrdinalIgnoreCase);
        var compatibilityByFundName = profiles.ToDictionary(
            static profile => profile.DisplayName,
            static profile => profile.FundProfileId,
            StringComparer.OrdinalIgnoreCase);

        foreach (var organization in graph.Organizations)
        {
            var businesses = graph.Businesses.Where(business => business.OrganizationId == organization.OrganizationId).ToArray();
            var funds = businesses.SelectMany(static business => business.FundIds)
                .Distinct()
                .Select(fundId => graph.Funds.FirstOrDefault(fund => fund.FundId == fundId))
                .Where(static fund => fund is not null)
                .Cast<FundSummaryDto>()
                .ToArray();

            contexts[WorkstationOperatingContext.CreateContextKey(OperatingContextScopeKind.Organization, organization.OrganizationId.ToString("D"))] =
                new WorkstationOperatingContext
                {
                    ScopeKind = OperatingContextScopeKind.Organization,
                    ScopeId = organization.OrganizationId.ToString("D"),
                    DisplayName = organization.Name,
                    BusinessKind = businesses.FirstOrDefault()?.BusinessKind ?? BusinessKindDto.Hybrid,
                    BaseCurrency = organization.BaseCurrency,
                    EntityIds = funds.SelectMany(static fund => fund.EntityIds).Select(static id => id.ToString("D")).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                    PortfolioIds = businesses.SelectMany(static business => business.InvestmentPortfolioIds).Select(static id => id.ToString("D")).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                    DefaultWorkspaceId = "governance",
                    DefaultLandingPageTag = "GovernanceShell",
                    OrganizationId = organization.OrganizationId.ToString("D"),
                    CompatibilityFundProfileId = ResolveCompatibilityFundProfileId(funds, compatibilityByFundName)
                };
        }

        foreach (var business in graph.Businesses)
        {
            var funds = graph.Funds.Where(fund => fund.BusinessId == business.BusinessId).ToArray();
            contexts[WorkstationOperatingContext.CreateContextKey(OperatingContextScopeKind.Business, business.BusinessId.ToString("D"))] =
                new WorkstationOperatingContext
                {
                    ScopeKind = OperatingContextScopeKind.Business,
                    ScopeId = business.BusinessId.ToString("D"),
                    DisplayName = business.Name,
                    BusinessKind = business.BusinessKind,
                    BaseCurrency = business.BaseCurrency,
                    EntityIds = funds.SelectMany(static fund => fund.EntityIds).Select(static id => id.ToString("D")).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                    PortfolioIds = business.InvestmentPortfolioIds.Select(static id => id.ToString("D")).ToArray(),
                    DefaultWorkspaceId = "research",
                    DefaultLandingPageTag = "ResearchShell",
                    OrganizationId = business.OrganizationId.ToString("D"),
                    BusinessId = business.BusinessId.ToString("D"),
                    CompatibilityFundProfileId = ResolveCompatibilityFundProfileId(funds, compatibilityByFundName)
                };
        }

        foreach (var client in graph.Clients)
        {
            var business = graph.Businesses.FirstOrDefault(item => item.BusinessId == client.BusinessId);
            contexts[WorkstationOperatingContext.CreateContextKey(OperatingContextScopeKind.Client, client.ClientId.ToString("D"))] =
                new WorkstationOperatingContext
                {
                    ScopeKind = OperatingContextScopeKind.Client,
                    ScopeId = client.ClientId.ToString("D"),
                    DisplayName = client.Name,
                    BusinessKind = business?.BusinessKind ?? BusinessKindDto.Hybrid,
                    BaseCurrency = client.BaseCurrency,
                    PortfolioIds = client.InvestmentPortfolioIds.Select(static id => id.ToString("D")).ToArray(),
                    DefaultWorkspaceId = "research",
                    DefaultLandingPageTag = "ResearchShell",
                    OrganizationId = business?.OrganizationId.ToString("D"),
                    BusinessId = client.BusinessId.ToString("D"),
                    ClientId = client.ClientId.ToString("D"),
                    CompatibilityFundProfileId = ResolveCompatibilityFundProfileId(
                        graph.Funds.Where(fund => fund.BusinessId == client.BusinessId),
                        compatibilityByFundName)
                };
        }

        foreach (var portfolio in graph.InvestmentPortfolios)
        {
            var business = graph.Businesses.FirstOrDefault(item => item.BusinessId == portfolio.BusinessId);
            contexts[WorkstationOperatingContext.CreateContextKey(OperatingContextScopeKind.InvestmentPortfolio, portfolio.InvestmentPortfolioId.ToString("D"))] =
                new WorkstationOperatingContext
                {
                    ScopeKind = OperatingContextScopeKind.InvestmentPortfolio,
                    ScopeId = portfolio.InvestmentPortfolioId.ToString("D"),
                    DisplayName = portfolio.Name,
                    BusinessKind = business?.BusinessKind ?? BusinessKindDto.Hybrid,
                    BaseCurrency = portfolio.BaseCurrency,
                    PortfolioIds = new[] { portfolio.InvestmentPortfolioId.ToString("D") },
                    EntityIds = portfolio.EntityId is Guid entityId ? new[] { entityId.ToString("D") } : Array.Empty<string>(),
                    DefaultWorkspaceId = "research",
                    DefaultLandingPageTag = "ResearchShell",
                    OrganizationId = business?.OrganizationId.ToString("D"),
                    BusinessId = portfolio.BusinessId.ToString("D"),
                    ClientId = portfolio.ClientId?.ToString("D"),
                    InvestmentPortfolioId = portfolio.InvestmentPortfolioId.ToString("D"),
                    FundId = portfolio.FundId?.ToString("D"),
                    EntityId = portfolio.EntityId?.ToString("D"),
                    CompatibilityFundProfileId = ResolveCompatibilityFundProfileId(
                        graph.Funds.Where(fund => portfolio.FundId.HasValue && fund.FundId == portfolio.FundId.Value),
                        compatibilityByFundName)
                };
        }

        foreach (var fund in graph.Funds)
        {
            var business = graph.Businesses.FirstOrDefault(item => item.BusinessId == fund.BusinessId);
            contexts[WorkstationOperatingContext.CreateContextKey(OperatingContextScopeKind.Fund, fund.FundId.ToString("D"))] =
                new WorkstationOperatingContext
                {
                    ScopeKind = OperatingContextScopeKind.Fund,
                    ScopeId = fund.FundId.ToString("D"),
                    DisplayName = fund.Name,
                    BusinessKind = business?.BusinessKind ?? BusinessKindDto.Hybrid,
                    BaseCurrency = fund.BaseCurrency,
                    EntityIds = fund.EntityIds.Select(static id => id.ToString("D")).ToArray(),
                    PortfolioIds = fund.InvestmentPortfolioIds.Select(static id => id.ToString("D")).ToArray(),
                    DefaultWorkspaceId = "governance",
                    DefaultLandingPageTag = "GovernanceShell",
                    OrganizationId = business?.OrganizationId.ToString("D"),
                    BusinessId = fund.BusinessId?.ToString("D"),
                    FundId = fund.FundId.ToString("D"),
                    CompatibilityFundProfileId = ResolveCompatibilityFundProfileId(new[] { fund }, compatibilityByFundName)
                };
        }

        foreach (var entity in graph.Entities)
        {
            var relatedFund = graph.Funds.FirstOrDefault(fund => fund.EntityIds.Contains(entity.EntityId));
            var business = relatedFund is null
                ? null
                : graph.Businesses.FirstOrDefault(item => item.BusinessId == relatedFund.BusinessId);

            contexts[WorkstationOperatingContext.CreateContextKey(OperatingContextScopeKind.Entity, entity.EntityId.ToString("D"))] =
                new WorkstationOperatingContext
                {
                    ScopeKind = OperatingContextScopeKind.Entity,
                    ScopeId = entity.EntityId.ToString("D"),
                    DisplayName = entity.Name,
                    LegalEntityName = entity.Name,
                    BusinessKind = business?.BusinessKind ?? BusinessKindDto.Hybrid,
                    BaseCurrency = entity.BaseCurrency,
                    EntityIds = new[] { entity.EntityId.ToString("D") },
                    PortfolioIds = graph.InvestmentPortfolios.Where(portfolio => portfolio.EntityId == entity.EntityId)
                        .Select(portfolio => portfolio.InvestmentPortfolioId.ToString("D"))
                        .ToArray(),
                    DefaultWorkspaceId = "governance",
                    DefaultLandingPageTag = "GovernanceShell",
                    OrganizationId = business?.OrganizationId.ToString("D"),
                    BusinessId = relatedFund?.BusinessId?.ToString("D"),
                    FundId = relatedFund?.FundId.ToString("D"),
                    EntityId = entity.EntityId.ToString("D"),
                    CompatibilityFundProfileId = ResolveCompatibilityFundProfileId(
                        graph.Funds.Where(fund => fund.EntityIds.Contains(entity.EntityId)),
                        compatibilityByFundName)
                };
        }

        foreach (var sleeve in graph.Sleeves)
        {
            var fund = graph.Funds.FirstOrDefault(item => item.FundId == sleeve.FundId);
            var business = fund is null
                ? null
                : graph.Businesses.FirstOrDefault(item => item.BusinessId == fund.BusinessId);

            contexts[WorkstationOperatingContext.CreateContextKey(OperatingContextScopeKind.Sleeve, sleeve.SleeveId.ToString("D"))] =
                new WorkstationOperatingContext
                {
                    ScopeKind = OperatingContextScopeKind.Sleeve,
                    ScopeId = sleeve.SleeveId.ToString("D"),
                    DisplayName = sleeve.Name,
                    BusinessKind = business?.BusinessKind ?? BusinessKindDto.Hybrid,
                    BaseCurrency = fund?.BaseCurrency ?? "USD",
                    PortfolioIds = sleeve.InvestmentPortfolioIds.Select(static id => id.ToString("D")).ToArray(),
                    DefaultWorkspaceId = "trading",
                    DefaultLandingPageTag = "TradingShell",
                    OrganizationId = business?.OrganizationId.ToString("D"),
                    BusinessId = fund?.BusinessId?.ToString("D"),
                    FundId = sleeve.FundId.ToString("D"),
                    SleeveId = sleeve.SleeveId.ToString("D"),
                    CompatibilityFundProfileId = ResolveCompatibilityFundProfileId(
                        graph.Funds.Where(item => item.FundId == sleeve.FundId),
                        compatibilityByFundName)
                };
        }

        foreach (var vehicle in graph.Vehicles)
        {
            var fund = graph.Funds.FirstOrDefault(item => item.FundId == vehicle.FundId);
            var business = fund is null
                ? null
                : graph.Businesses.FirstOrDefault(item => item.BusinessId == fund.BusinessId);

            contexts[WorkstationOperatingContext.CreateContextKey(OperatingContextScopeKind.Vehicle, vehicle.VehicleId.ToString("D"))] =
                new WorkstationOperatingContext
                {
                    ScopeKind = OperatingContextScopeKind.Vehicle,
                    ScopeId = vehicle.VehicleId.ToString("D"),
                    DisplayName = vehicle.Name,
                    BusinessKind = business?.BusinessKind ?? BusinessKindDto.Hybrid,
                    BaseCurrency = vehicle.BaseCurrency,
                    EntityIds = new[] { vehicle.LegalEntityId.ToString("D") },
                    PortfolioIds = vehicle.InvestmentPortfolioIds.Select(static id => id.ToString("D")).ToArray(),
                    DefaultWorkspaceId = "governance",
                    DefaultLandingPageTag = "GovernanceShell",
                    OrganizationId = business?.OrganizationId.ToString("D"),
                    BusinessId = fund?.BusinessId?.ToString("D"),
                    FundId = vehicle.FundId.ToString("D"),
                    VehicleId = vehicle.VehicleId.ToString("D"),
                    EntityId = vehicle.LegalEntityId.ToString("D"),
                    CompatibilityFundProfileId = ResolveCompatibilityFundProfileId(
                        graph.Funds.Where(item => item.FundId == vehicle.FundId),
                        compatibilityByFundName)
                };
        }

        foreach (var account in graph.Accounts)
        {
            var relatedFund = account.FundId.HasValue
                ? graph.Funds.FirstOrDefault(item => item.FundId == account.FundId.Value)
                : null;
            var business = relatedFund is null
                ? null
                : graph.Businesses.FirstOrDefault(item => item.BusinessId == relatedFund.BusinessId);

            contexts[WorkstationOperatingContext.CreateContextKey(OperatingContextScopeKind.Account, account.AccountId.ToString("D"))] =
                new WorkstationOperatingContext
                {
                    ScopeKind = OperatingContextScopeKind.Account,
                    ScopeId = account.AccountId.ToString("D"),
                    DisplayName = account.DisplayName,
                    BusinessKind = business?.BusinessKind ?? BusinessKindDto.Hybrid,
                    BaseCurrency = account.BaseCurrency,
                    EntityIds = account.EntityId.HasValue ? new[] { account.EntityId.Value.ToString("D") } : Array.Empty<string>(),
                    PortfolioIds = string.IsNullOrWhiteSpace(account.PortfolioId) ? Array.Empty<string>() : new[] { account.PortfolioId! },
                    DefaultWorkspaceId = "governance",
                    DefaultLandingPageTag = "GovernanceShell",
                    OrganizationId = business?.OrganizationId.ToString("D"),
                    BusinessId = relatedFund?.BusinessId?.ToString("D"),
                    FundId = account.FundId?.ToString("D"),
                    EntityId = account.EntityId?.ToString("D"),
                    SleeveId = account.SleeveId?.ToString("D"),
                    VehicleId = account.VehicleId?.ToString("D"),
                    AccountId = account.AccountId.ToString("D"),
                    CompatibilityFundProfileId = ResolveCompatibilityFundProfileId(
                        graph.Funds.Where(item => account.FundId.HasValue && item.FundId == account.FundId.Value),
                        compatibilityByFundName)
                };
        }

        if (runtimeLedgerGroups is not null)
        {
            foreach (var ledgerGroup in runtimeLedgerGroups)
            {
                var business = ledgerGroup.BusinessId.HasValue
                    ? graph.Businesses.FirstOrDefault(item => item.BusinessId == ledgerGroup.BusinessId.Value)
                    : null;

                contexts[WorkstationOperatingContext.CreateContextKey(OperatingContextScopeKind.LedgerGroup, ledgerGroup.LedgerGroupId.ToString())] =
                    new WorkstationOperatingContext
                    {
                        ScopeKind = OperatingContextScopeKind.LedgerGroup,
                        ScopeId = ledgerGroup.LedgerGroupId.ToString(),
                        DisplayName = ledgerGroup.DisplayName,
                        BusinessKind = business?.BusinessKind ?? BusinessKindDto.Hybrid,
                        BaseCurrency = ledgerGroup.BaseCurrency,
                        EntityIds = graph.Funds.Where(fund => ledgerGroup.FundId.HasValue && fund.FundId == ledgerGroup.FundId.Value)
                            .SelectMany(static fund => fund.EntityIds)
                            .Select(static id => id.ToString("D"))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToArray(),
                        PortfolioIds = ledgerGroup.InvestmentPortfolioIds.Select(static id => id.ToString("D")).ToArray(),
                        LedgerGroupIds = new[] { ledgerGroup.LedgerGroupId.ToString() },
                        DefaultWorkspaceId = "governance",
                        DefaultLandingPageTag = "GovernanceShell",
                        OrganizationId = ledgerGroup.OrganizationId?.ToString("D"),
                        BusinessId = ledgerGroup.BusinessId?.ToString("D"),
                        ClientId = ledgerGroup.ClientId?.ToString("D"),
                        FundId = ledgerGroup.FundId?.ToString("D"),
                        SleeveId = ledgerGroup.SleeveId?.ToString("D"),
                        VehicleId = ledgerGroup.VehicleId?.ToString("D"),
                        LedgerGroupId = ledgerGroup.LedgerGroupId.ToString(),
                        CompatibilityFundProfileId = ResolveCompatibilityFundProfileId(
                            graph.Funds.Where(fund => ledgerGroup.FundId.HasValue && fund.FundId == ledgerGroup.FundId.Value),
                            compatibilityByFundName)
                    };
            }
        }
        else if (_fundStructureService is not null)
        {
            foreach (var business in graph.Businesses)
            {
                AccountingStructureViewDto? accountingView = null;
                try
                {
                    accountingView = await _fundStructureService
                        .GetAccountingViewAsync(new AccountingStructureQuery(BusinessId: business.BusinessId), ct)
                        .ConfigureAwait(false);
                }
                catch
                {
                }

                if (accountingView is null)
                {
                    continue;
                }

                foreach (var ledgerGroup in accountingView.LedgerGroups)
                {
                    contexts[WorkstationOperatingContext.CreateContextKey(OperatingContextScopeKind.LedgerGroup, ledgerGroup.LedgerGroupId.ToString())] =
                        new WorkstationOperatingContext
                        {
                            ScopeKind = OperatingContextScopeKind.LedgerGroup,
                            ScopeId = ledgerGroup.LedgerGroupId.ToString(),
                            DisplayName = ledgerGroup.DisplayName,
                            BusinessKind = business.BusinessKind,
                            BaseCurrency = business.BaseCurrency,
                            EntityIds = graph.Funds.Where(fund => ledgerGroup.FundIds.Contains(fund.FundId))
                                .SelectMany(static fund => fund.EntityIds)
                                .Select(static id => id.ToString("D"))
                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                .ToArray(),
                            PortfolioIds = ledgerGroup.InvestmentPortfolioIds.Select(static id => id.ToString("D")).ToArray(),
                            LedgerGroupIds = new[] { ledgerGroup.LedgerGroupId.ToString() },
                            DefaultWorkspaceId = "governance",
                            DefaultLandingPageTag = "GovernanceShell",
                            OrganizationId = business.OrganizationId.ToString("D"),
                            BusinessId = business.BusinessId.ToString("D"),
                            LedgerGroupId = ledgerGroup.LedgerGroupId.ToString(),
                            CompatibilityFundProfileId = ResolveCompatibilityFundProfileId(
                                graph.Funds.Where(fund => ledgerGroup.FundIds.Contains(fund.FundId)),
                                compatibilityByFundName)
                        };
                }
            }
        }

        return contexts.Values.ToArray();
    }

    private async Task<IReadOnlyList<WorkstationOperatingContext>> BuildRuntimeContextsAsync(
        PublishedEnvironmentRuntimeDto runtime,
        IReadOnlyList<FundProfileDetail> profiles,
        CancellationToken ct)
    {
        var contexts = (await BuildGraphContextsAsync(runtime.OrganizationGraph, profiles, runtime.LedgerGroups, ct).ConfigureAwait(false))
            .ToDictionary(context => context.ContextKey, StringComparer.OrdinalIgnoreCase);
        var laneLookup = runtime.Lanes.ToDictionary(lane => lane.LaneId, StringComparer.OrdinalIgnoreCase);

        foreach (var mapping in runtime.ContextMappings
            .OrderBy(static mapping => mapping.ContextKey, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static mapping => mapping.LaneName, StringComparer.OrdinalIgnoreCase))
        {
            if (!contexts.TryGetValue(mapping.ContextKey, out var context) ||
                !laneLookup.TryGetValue(mapping.LaneId, out var lane))
            {
                continue;
            }

            contexts[mapping.ContextKey] = context with
            {
                EnvironmentLaneId = mapping.LaneId,
                EnvironmentLaneName = mapping.LaneName,
                OperatingEnvironmentKind = WorkstationOperatingContext.FromArchetype(mapping.Archetype),
                DefaultWorkspaceId = lane.DefaultWorkspaceId,
                DefaultLandingPageTag = lane.DefaultLandingPageTag
            };
        }

        return contexts.Values.ToArray();
    }

    private static IEnumerable<WorkstationOperatingContext> BuildFundProfileContexts(IReadOnlyList<FundProfileDetail> profiles)
    {
        foreach (var profile in profiles)
        {
            yield return new WorkstationOperatingContext
            {
                ScopeKind = OperatingContextScopeKind.Fund,
                ScopeId = profile.FundProfileId,
                DisplayName = profile.DisplayName,
                LegalEntityName = profile.LegalEntityName,
                BusinessKind = BusinessKindDto.Hybrid,
                BaseCurrency = profile.BaseCurrency,
                EntityIds = profile.EntityIds?.ToArray() ?? Array.Empty<string>(),
                DefaultWorkspaceId = string.IsNullOrWhiteSpace(profile.DefaultWorkspaceId) ? "governance" : profile.DefaultWorkspaceId,
                DefaultLandingPageTag = string.IsNullOrWhiteSpace(profile.DefaultLandingPageTag) ? "GovernanceShell" : profile.DefaultLandingPageTag,
                FundId = profile.FundProfileId,
                CompatibilityFundProfileId = profile.FundProfileId,
                LastOpenedAt = profile.LastOpenedAt
            };
        }
    }

    private async Task LoadSettingsAsync(CancellationToken ct)
    {
        if (!File.Exists(_storagePath))
        {
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(_storagePath, ct).ConfigureAwait(false);
            var model = JsonSerializer.Deserialize(json, WorkstationOperatingContextJsonContext.Default.WorkstationOperatingContextStorageModel);
            if (model is null)
            {
                return;
            }

            LastSelectedOperatingContextKey = model.LastSelectedOperatingContextKey;
            CurrentWindowMode = model.WindowMode;
            CurrentLayoutPresetId = model.CurrentLayoutPresetId;
        }
        catch
        {
            LastSelectedOperatingContextKey = null;
            CurrentWindowMode = BoundedWindowMode.DockFloat;
            CurrentLayoutPresetId = null;
        }
    }

    private async Task SaveSettingsAsync(CancellationToken ct)
    {
        try
        {
            var directory = Path.GetDirectoryName(_storagePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(
                new WorkstationOperatingContextStorageModel
                {
                    LastSelectedOperatingContextKey = LastSelectedOperatingContextKey,
                    WindowMode = CurrentWindowMode,
                    CurrentLayoutPresetId = CurrentLayoutPresetId
                },
                WorkstationOperatingContextJsonContext.Default.WorkstationOperatingContextStorageModel);

            await File.WriteAllTextAsync(_storagePath, json, ct).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private static string? ResolveCompatibilityFundProfileId(
        IEnumerable<FundSummaryDto> funds,
        IReadOnlyDictionary<string, string> compatibilityByFundName)
    {
        foreach (var fund in funds)
        {
            if (compatibilityByFundName.TryGetValue(fund.Name, out var fundProfileId))
            {
                return fundProfileId;
            }

            if (compatibilityByFundName.TryGetValue(fund.Code, out fundProfileId))
            {
                return fundProfileId;
            }
        }

        return null;
    }

    private static async Task TryExecuteAsync(Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private static string HumanizePresetId(string presetId)
        => string.Join(" ", presetId
            .Split(['-', '_'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static token => token.Length == 1
                ? token.ToUpperInvariant()
                : char.ToUpperInvariant(token[0]) + token[1..]));

    private static string GetDefaultStoragePath()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Meridian");
        return Path.Combine(directory, "workstation-operating-context.json");
    }

    private sealed class WorkstationOperatingContextStorageModel
    {
        public string? LastSelectedOperatingContextKey { get; set; }

        public BoundedWindowMode WindowMode { get; set; } = BoundedWindowMode.DockFloat;

        public string? CurrentLayoutPresetId { get; set; }
    }

    [JsonSourceGenerationOptions(WriteIndented = true)]
    [JsonSerializable(typeof(WorkstationOperatingContextStorageModel))]
    private sealed partial class WorkstationOperatingContextJsonContext : JsonSerializerContext;
}
