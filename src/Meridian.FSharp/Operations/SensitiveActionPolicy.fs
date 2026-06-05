namespace Meridian.FSharp.Operations

open System
open Meridian.Identity.Auth

[<CLIMutable>]
type SensitiveActionPolicyInput =
    {
        Action: string
        Actor: string
        Role: UserRole
        Team: string
        MfaSatisfied: bool
        SecondaryApprovers: string array
    }

[<CLIMutable>]
type SensitiveActionPolicyDecisionDto =
    {
        Allowed: bool
        Reason: string
        RequiresDualApproval: bool
        RequiresPrivilegedRole: bool
        RequiresMfa: bool
    }

module SensitiveActionPolicy =

    let private decision allowed reason dual privileged mfa =
        {
            Allowed = allowed
            Reason = reason
            RequiresDualApproval = dual
            RequiresPrivilegedRole = privileged
            RequiresMfa = mfa
        }

    let private requiredPermission action =
        match action with
        | "RuleEdit" -> Some UserPermission.ModifyConfig
        | "BreakClosure" -> Some UserPermission.ManageDirectLending
        | "PaymentRelease" -> Some UserPermission.ManageDirectLending
        | "OverrideApproval" -> Some UserPermission.AdminMaintenance
        | _ -> None

    let private isPrivilegedRole role =
        role = UserRole.Admin || role = UserRole.Developer || role = UserRole.Executive

    let evaluate (input: SensitiveActionPolicyInput) =
        let isAccountingOverride =
            input.Action = "OverrideApproval"
            && input.Role = UserRole.Accounting
            && String.Equals(input.Team, "accounting", StringComparison.OrdinalIgnoreCase)

        match requiredPermission input.Action with
        | None -> decision false "Unknown action." false false false
        | Some _ when isAccountingOverride ->
            decision false "Segregation-of-duties blocked override approval for accounting actor." true true true
        | Some required when not (RolePermissions.HasPermission(input.Role, required)) ->
            decision false (sprintf "Role %O lacks %O permission." input.Role required) false false false
        | Some _ ->
                let dualApproval = input.Action = "PaymentRelease" || input.Action = "OverrideApproval"
                let privilegedRole = input.Action = "OverrideApproval"
                let requiresMfa = input.Action = "PaymentRelease" || input.Action = "OverrideApproval"

                if privilegedRole && not (isPrivilegedRole input.Role) then
                    decision false "Privileged role is required." dualApproval true requiresMfa
                elif dualApproval then
                    let secondary = defaultArg (Option.ofObj input.SecondaryApprovers) [||]
                    let hasDistinct =
                        secondary
                        |> Array.exists (fun approver -> not (String.Equals(approver, input.Actor, StringComparison.OrdinalIgnoreCase)))

                    if not hasDistinct then
                        decision false "Dual approval requires a distinct secondary approver." true privilegedRole requiresMfa
                    elif requiresMfa && not input.MfaSatisfied then
                        decision false "MFA requirement not satisfied." dualApproval privilegedRole true
                    else
                        decision true "Approved." dualApproval privilegedRole requiresMfa
                elif requiresMfa && not input.MfaSatisfied then
                    decision false "MFA requirement not satisfied." dualApproval privilegedRole true
                else
                    decision true "Approved." dualApproval privilegedRole requiresMfa

[<Sealed>]
type SensitiveActionPolicyInterop private () =

    static member Evaluate(input: SensitiveActionPolicyInput) =
        SensitiveActionPolicy.evaluate input
