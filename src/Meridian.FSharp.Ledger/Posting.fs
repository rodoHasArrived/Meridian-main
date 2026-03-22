namespace Meridian.FSharp.Ledger

[<RequireQualifiedAccess>]
module Posting =

    let calculateNetBalance (accountType: int) (debits: decimal) (credits: decimal) =
        match accountType with
        | 0
        | 4 -> debits - credits
        | _ -> credits - debits
