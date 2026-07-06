using Meridian.FinancialOperations.LedgerTextJournal;
using Meridian.Platform.Results;
using Meridian.Ledger;

namespace Meridian.Application.Commands;

/// <summary>
/// Ledger-compatible text journal reports backed by the Meridian double-entry ledger engine.
/// </summary>
internal sealed class LedgerCliCommand : ICliCommand
{
    private readonly LedgerTextJournalReportService _reportService = new();

    private static readonly HashSet<string> s_supportedReports = new(StringComparer.OrdinalIgnoreCase)
    {
        "accounts",
        "balance",
        "print",
        "register",
    };

    public IReadOnlyList<string> Triggers { get; } = [ "ledger" ];

    public bool CanHandle(string[] args)
        => args.Length > 0 && Triggers.Any(trigger => args[0].Equals(trigger, StringComparison.OrdinalIgnoreCase));

    public async Task<CliResult> ExecuteAsync(string[] args, CancellationToken ct = default)
    {
        var options = LedgerCommandOptions.Parse(args);
        if (!options.IsValid)
        {
            Console.Error.WriteLine(options.Error);
            return CliResult.Fail(ErrorCode.RequiredFieldMissing);
        }

        if (!File.Exists(options.JournalPath))
        {
            Console.Error.WriteLine($"Ledger journal file not found: {options.JournalPath}");
            return CliResult.Fail(ErrorCode.FileNotFound);
        }

        try
        {
            var lines = await File.ReadAllLinesAsync(options.JournalPath, ct).ConfigureAwait(false);
            var report = _reportService.RenderReport(lines, new LedgerTextReportOptions(options.Report, options.AccountFilter));
            Console.Write(report);
            return CliResult.Ok();
        }
        catch (OperationCanceledException)
        {
            return CliResult.Fail(ErrorCode.Cancelled);
        }
        catch (LedgerTextJournalException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return CliResult.Fail(ErrorCode.ValidationFailed);
        }
        catch (LedgerValidationException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return CliResult.Fail(ErrorCode.ValidationFailed);
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return CliResult.Fail(ErrorCode.ReadFailed);
        }
    }

    private sealed record LedgerCommandOptions(
        string? JournalPath,
        string Report,
        string? AccountFilter,
        bool IsValid,
        string? Error)
    {
        public static LedgerCommandOptions Parse(string[] args)
        {
            var report = string.Empty;
            string? filter = null;

            for (var i = 1; i < args.Length; i++)
            {
                var arg = args[i];
                if (arg.Equals("-f", StringComparison.OrdinalIgnoreCase) ||
                    arg.Equals("--file", StringComparison.OrdinalIgnoreCase))
                {
                    i++;
                    continue;
                }

                if (arg.StartsWith("-", StringComparison.Ordinal))
                    continue;

                report = arg;
                if (i + 1 < args.Length)
                    filter = args[i + 1];
                break;
            }

            if (string.IsNullOrWhiteSpace(report) || !s_supportedReports.Contains(report))
            {
                return new LedgerCommandOptions(
                    null,
                    string.Empty,
                    null,
                    false,
                    "Usage: Meridian ledger -f <journal-file> <balance|register|print|accounts> [account-filter]");
            }

            var journalPath = CliArguments.GetValue(args, "-f") ?? CliArguments.GetValue(args, "--file");
            if (string.IsNullOrWhiteSpace(journalPath))
            {
                return new LedgerCommandOptions(
                    null,
                    report,
                    filter,
                    false,
                    "Error: ledger requires -f <journal-file>");
            }

            return new LedgerCommandOptions(journalPath, report, filter, true, null);
        }
    }
}
