using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace redisqa.Services;

public sealed record BenchmarkCsvRecord(
    int RequestIndex,
    string QueryName,
    double LatencyMs,
    int RowsReturned,
    bool Ok,
    string Error);

public static class BenchmarkCsvWriter
{
    public static async Task WriteAsync(string filePath, IReadOnlyList<BenchmarkCsvRecord> records)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var writer = new StreamWriter(filePath, false, Encoding.UTF8);
        await writer.WriteLineAsync("request_index,query_name,latency_ms,rows_returned,ok,error");

        foreach (var record in records)
        {
            var line = string.Join(",",
                record.RequestIndex.ToString(CultureInfo.InvariantCulture),
                EscapeCsv(record.QueryName),
                record.LatencyMs.ToString("F3", CultureInfo.InvariantCulture),
                record.RowsReturned.ToString(CultureInfo.InvariantCulture),
                record.Ok ? "true" : "false",
                EscapeCsv(record.Error));

            await writer.WriteLineAsync(line);
        }
    }

    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var escaped = value.Replace("\"", "\"\"");
        if (escaped.Contains(',') || escaped.Contains('"') || escaped.Contains('\n') || escaped.Contains('\r'))
        {
            return $"\"{escaped}\"";
        }

        return escaped;
    }
}
