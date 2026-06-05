module Meridian.FSharp.Tests.SensitiveActionPolicyTests

open Xunit
open FsUnit.Xunit
open Meridian.Identity.Auth
open Meridian.FSharp.Operations

let private input action role mfa secondary =
    {
        Action = action
        Actor = "alice"
        Role = role
        Team = "fund-ops"
        MfaSatisfied = mfa
        SecondaryApprovers = secondary
    }

[<Fact>]
let ``Sensitive action policy requires MFA for payment release`` () =
    let denied = SensitiveActionPolicyInterop.Evaluate(input "PaymentRelease" UserRole.Accounting false [| "bob" |])
    let allowed = SensitiveActionPolicyInterop.Evaluate(input "PaymentRelease" UserRole.Accounting true [| "bob" |])

    denied.Allowed |> should equal false
    denied.RequiresMfa |> should equal true
    allowed.Allowed |> should equal true

[<Fact>]
let ``Sensitive action policy blocks override self approval for accounting team`` () =
    let decision =
        SensitiveActionPolicyInterop.Evaluate(
            { input "OverrideApproval" UserRole.Accounting true [| "bob" |] with
                Team = "accounting" })

    decision.Allowed |> should equal false
    decision.Reason.Contains("Segregation-of-duties") |> should equal true

[<Fact>]
let ``Sensitive action policy rejects unknown actions`` () =
    let decision = SensitiveActionPolicyInterop.Evaluate(input "Unknown" UserRole.Admin true [| "bob" |])

    decision.Allowed |> should equal false
    decision.Reason |> should equal "Unknown action."
