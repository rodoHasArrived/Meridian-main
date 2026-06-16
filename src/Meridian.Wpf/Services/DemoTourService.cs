using System;
using System.Collections.Generic;

using Meridian.Ui.Services.Services;

namespace Meridian.Wpf.Services;

/// <summary>
/// Coordinates the safe, fixture-backed desktop tour used for first-launch onboarding and Settings.
/// </summary>
public sealed class DemoTourService
{
    private static readonly Lazy<DemoTourService> _instance = new(() => new DemoTourService(FixtureModeDetector.Instance, NavigationService.Instance));

    private readonly FixtureModeDetector _fixtureModeDetector;
    private readonly NavigationService _navigationService;
    private int _currentStepIndex = -1;

    public static DemoTourService Instance => _instance.Value;

    public DemoTourService(FixtureModeDetector fixtureModeDetector, NavigationService navigationService)
    {
        _fixtureModeDetector = fixtureModeDetector ?? throw new ArgumentNullException(nameof(fixtureModeDetector));
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
    }

    public event EventHandler? TourChanged;

    public IReadOnlyList<DemoTourStep> Steps { get; } =
    [
        new(1, "Data/provider status", "DataShell", "Review sample provider health and queue telemetry before any live provider credentials are connected."),
        new(2, "Portfolio records", "PortfolioShell", "Inspect fixture portfolio records that are isolated from retained source-backed books."),
        new(3, "Accounting reconciliation", "FundReconciliation", "Walk through safe sample breaks, match context, and close-readiness posture."),
        new(4, "Evidence/audit context", "FundAuditTrail", "Confirm retained evidence, audit history, and source references are visibly sample-only."),
        new(5, "Reporting readiness", "ReportingShell", "Preview governed report readiness without publishing provider-backed operational data."),
        new(6, "Settings", "SettingsShell", "Finish in Settings, where the tour can be restarted and demo/sample mode is clearly labeled.")
    ];

    public bool IsTourActive => _currentStepIndex >= 0 && _currentStepIndex < Steps.Count;

    public DemoTourStep? CurrentStep => IsTourActive ? Steps[_currentStepIndex] : null;

    public string CurrentStepText => CurrentStep is { } step
        ? $"Demo/sample tour {step.Sequence} of {Steps.Count}: {step.Title}"
        : "Demo/sample tour inactive";

    public string CurrentStepDetail => CurrentStep?.Description
        ?? "Start the guided tour to load fixture data and visit the safe demo workflow.";

    public bool CanMoveNext => IsTourActive && _currentStepIndex < Steps.Count - 1;

    public void StartTour()
    {
        _fixtureModeDetector.SetFixtureMode(true);
        FixtureDataService.Instance.SetScenario(FixtureScenario.Connected);
        _currentStepIndex = 0;
        NavigateToCurrentStep();
        TourChanged?.Invoke(this, EventArgs.Empty);
    }

    public void MoveNext()
    {
        if (!CanMoveNext)
        {
            return;
        }

        _currentStepIndex++;
        NavigateToCurrentStep();
        TourChanged?.Invoke(this, EventArgs.Empty);
    }

    public void EndTour()
    {
        _currentStepIndex = -1;
        TourChanged?.Invoke(this, EventArgs.Empty);
    }

    private void NavigateToCurrentStep()
    {
        if (CurrentStep is { PageTag.Length: > 0 } step)
        {
            _navigationService.NavigateTo(step.PageTag);
        }
    }
}

public sealed record DemoTourStep(int Sequence, string Title, string PageTag, string Description);
