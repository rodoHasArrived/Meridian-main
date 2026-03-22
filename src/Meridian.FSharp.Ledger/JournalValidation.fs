namespace Meridian.FSharp.Ledger

open System

[<RequireQualifiedAccess>]
module JournalValidation =

    let private balanceTolerance = 0.000001m

    let validate (journalEntryId: Guid) (timestamp: DateTimeOffset) (description: string) (lines: LedgerLineInput list) (existingJournalIds: Set<Guid>) (existingEntryIds: Set<Guid>) =
        let errors = ResizeArray<string>()

        if String.IsNullOrWhiteSpace description then
            errors.Add "Journal entry description must not be null or whitespace."

        if List.isEmpty lines then
            errors.Add "Journal entry must have at least one line."

        if existingJournalIds.Contains journalEntryId then
            errors.Add (sprintf "Journal entry '%O' has already been posted." journalEntryId)

        let totalDebit = lines |> List.sumBy (fun line -> line.Debit)
        let totalCredit = lines |> List.sumBy (fun line -> line.Credit)

        if abs (totalDebit - totalCredit) > balanceTolerance then
            errors.Add (sprintf "Journal entry '%O' is not balanced (debits=%.4f, credits=%.4f)." journalEntryId (float totalDebit) (float totalCredit))

        for line in lines do
            if line.JournalEntryId <> journalEntryId then
                errors.Add (sprintf "Ledger entry '%O' references journal entry '%O' but was posted under '%O'." line.EntryId line.JournalEntryId journalEntryId)

            if line.Timestamp <> timestamp then
                errors.Add (sprintf "Ledger entry '%O' timestamp '%O' does not match journal timestamp '%O'." line.EntryId line.Timestamp timestamp)

            if not (String.Equals(line.Description, description, StringComparison.Ordinal)) then
                errors.Add (sprintf "Ledger entry '%O' description must match journal entry description." line.EntryId)

            if existingEntryIds.Contains line.EntryId then
                errors.Add (sprintf "Ledger entry '%O' has already been posted." line.EntryId)

        totalDebit, totalCredit, (errors |> Seq.toList)
