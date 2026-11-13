using System.Text;
using Meridian.Contracts.PrivateCapital;

namespace Meridian.PortfolioRecords.PrivateCapital;

/// <summary>Bounded canonical CSV, including quoted commas/newlines and escaped quotes.</summary>
public static class PrivateCapitalCsv
{
    public const string Header = "commitment_id,as_of_date,nav_valuation_date,commitment_amount,called_amount,unfunded_amount,cumulative_distributions,nav_amount,source_reference,supersedes_snapshot_id";

    public static IReadOnlyList<PrivateCapitalSnapshotInput> Parse(string csv)
    {
        if (csv.Length > 1_048_576)
            throw new ArgumentException("Private-capital CSV exceeds 1 MiB.");
        var records = ParseCells(csv.TrimStart('\uFEFF'));
        if (records.Count == 0 || !records[0].SequenceEqual(Header.Split(','), StringComparer.Ordinal))
            throw new ArgumentException($"Use the canonical CSV header: {Header}");
        if (records.Count > 501)
            throw new ArgumentException("Private-capital CSV exceeds 500 observations.");
        return records.Skip(1).Select((cells, index) =>
        {
            if (cells.Length != 10 || !Guid.TryParse(cells[0], out var commitment))
                throw new ArgumentException($"CSV row {index + 2} has invalid columns or commitment identity.");
            Guid? supersedes = string.IsNullOrWhiteSpace(cells[9]) ? null : Guid.TryParse(cells[9], out var value) ? value : throw new ArgumentException($"CSV row {index + 2} has an invalid correction identity.");
            return new PrivateCapitalSnapshotInput(commitment, cells[1], Empty(cells[2]), Empty(cells[3]), Empty(cells[4]), Empty(cells[5]), Empty(cells[6]), Empty(cells[7]), cells[8], supersedes);
        }).ToArray();
    }

    private static string? Empty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static List<string[]> ParseCells(string csv)
    {
        var result = new List<string[]>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        var closed = false;
        for (var index = 0; index < csv.Length; index++)
        {
            var c = csv[index];
            if (quoted)
            {
                if (c == '"' && index + 1 < csv.Length && csv[index + 1] == '"')
                { field.Append('"'); index++; }
                else if (c == '"')
                { quoted = false; closed = true; }
                else
                    field.Append(c);
                continue;
            }
            if (c == '"')
            {
                if (field.Length != 0 || closed)
                    throw new ArgumentException("CSV quote is outside a quoted field.");
                quoted = true;
            }
            else if (c == ',' || c is '\r' or '\n')
            {
                row.Add(field.ToString());
                field.Clear();
                closed = false;
                if (c != ',')
                {
                    result.Add(row.ToArray());
                    row.Clear();
                    if (c == '\r' && index + 1 < csv.Length && csv[index + 1] == '\n')
                        index++;
                }
            }
            else
            {
                if (closed)
                    throw new ArgumentException("Unexpected text after a CSV closing quote.");
                field.Append(c);
            }
        }
        if (quoted)
            throw new ArgumentException("CSV contains an unterminated quoted field.");
        if (field.Length > 0 || row.Count > 0 || closed)
        { row.Add(field.ToString()); result.Add(row.ToArray()); }
        return result;
    }
}
