using System.Globalization;
using Meridian.Contracts.Workstation;

namespace Meridian.Wpf.ViewModels;

public sealed partial class SecurityMasterViewModel
{
    private void AppendSchedulePolicyEvidence(SecurityMasterTrustSnapshotDto snapshot)
    {
        if (snapshot.CashFlowBlocker is { } cashFlowBlocker)
            ScheduleBookFields.Add(new($"Cash-flow calculation blocker: {cashFlowBlocker.Code}", cashFlowBlocker.Reason));
        var exercise = snapshot.ScheduleBook?.DeclaredExercise ?? snapshot.ScheduleSummary?.DeclaredExercise;
        if (exercise is not null)
        {
            ScheduleBookFields.Add(new("Declared call/put schedule", exercise.Status.ToString()));
            ScheduleBookFields.Add(new("Schedule evaluated as of", SecurityMasterPresentation.FormatNullableDate(exercise.AsOf)));
            ScheduleBookFields.Add(new("Schedule security version", exercise.SecurityVersion?.ToString(CultureInfo.InvariantCulture) ?? "Unavailable"));
            ScheduleBookFields.Add(new("Exercise windows", exercise.WindowCount.ToString(CultureInfo.InvariantCulture)));
            foreach (var blocker in exercise.Blockers)
                ScheduleBookFields.Add(new($"Schedule blocker: {blocker.Code}", blocker.Reason));
            foreach (var diagnostic in exercise.Diagnostics)
                ScheduleBookFields.Add(new("Schedule diagnostic", diagnostic));
            if (exercise.Status == SecurityMasterDeclaredExerciseStatus.Resolved)
                ScheduleBookFields.Add(new("Exercise authority", "Resolved dates grant no exercise authority."));
        }

        if (snapshot.Governance is not { } governance)
        {
            ScheduleBookFields.Add(new("Effective governance", "Unavailable. Refresh the trust snapshot to inspect the host's requirements."));
            return;
        }
        ScheduleBookFields.Add(new("Effective governance", governance.Profile));
        ScheduleBookFields.Add(new("Governance source", governance.ProfileSource));
        if (governance.IsProfileLatched)
            ScheduleBookFields.Add(new("Governance latch", "Institutional requirements remain in force until the host restarts."));
        ScheduleBookFields.Add(new("Governed term amendments", governance.RequireGovernedTermAmendments ? "Required" : "Not required"));
        ScheduleBookFields.Add(new("Independent field reviewer", governance.RequireIndependentReviewer ? "Required" : "Not required"));
        ScheduleBookFields.Add(new("Independent profile approver", governance.RequireIndependentProfileApprover ? "Required" : "Not required"));
        foreach (var key in governance.ContradictedKeys)
            ScheduleBookFields.Add(new("Ignored configuration", $"{key}. Effective requirements remain in force."));
    }
}
