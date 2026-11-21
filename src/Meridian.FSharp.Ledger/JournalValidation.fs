namespace Meridian.FSharp.Ledger

open System

[<RequireQualifiedAccess>]
module JournalValidation =

    let private balanceTolerance = 0.000001m

    /// Validates a journal entry against already-posted ids supplied as membership tests, so the
    /// caller's own id sets are queried directly instead of being copied for every post.
    let validateWith (journalEntryId: Guid) (timestamp: DateTimeOffset) (description: string) (lines: LedgerLineInput list) (isJournalPosted: Guid -> bool) (isEntryPosted: Guid -> bool) =
        let errors = ResizeArray<string>()

        if String.IsNullOrWhiteSpace description then
            errors.Add "Journal entry description must not be null or whitespace."

        if List.isEmpty lines then
            errors.Add "Journal entry must have at least one line."

        if isJournalPosted journalEntryId then
            errors.Add (sprintf "Journal entry '%O' has already been posted." journalEntryId)

        // Legacy lines without currency detail remain compatible. Explicit functional
        // amounts in different currencies can never be summed into a balanced journal.
        let currencies =
            lines
            |> List.choose (fun line ->
                if String.IsNullOrWhiteSpace line.FunctionalCurrency then None
                else Some (line.FunctionalCurrency.Trim().ToUpperInvariant()))
            |> List.distinct
        if List.length currencies > 1 then
            errors.Add "Journal entry lines must use one functional currency."

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

            if isEntryPosted line.EntryId then
                errors.Add (sprintf "Ledger entry '%O' has already been posted." line.EntryId)

        totalDebit, totalCredit, (errors |> Seq.toList)

    let validate (journalEntryId: Guid) (timestamp: DateTimeOffset) (description: string) (lines: LedgerLineInput list) (existingJournalIds: Set<Guid>) (existingEntryIds: Set<Guid>) =
        validateWith journalEntryId timestamp description lines existingJournalIds.Contains existingEntryIds.Contains
