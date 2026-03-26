using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace redisqa.Services;

public sealed record BenchmarkLatencyRecord(
    string Phase,
    int RequestIndex,
    string QueryName,
    double LatencyMs,
    int RowsReturned,
    bool Ok,
    string Error);

public sealed record DockerStatsSample(
    DateTimeOffset TimestampUtc,
    string Phase,
    double CpuPercent,
    double MemPercent,
    double MemUsedBytes,
    double MemLimitBytes,
    double NetInBytes,
    double NetOutBytes,
    double BlockInBytes,
    double BlockOutBytes,
    string CpuRaw,
    string MemRaw,
    string NetRaw,
    string BlockRaw);

public sealed record PhaseSummaryRecord(
    string Db,
    string Phase,
    string QueryName,
    double DurationSec,
    int TotalRequests,
    int SuccessRequests,
    int ErrorRequests,
    double OpsPerSec,
    double P50Ms,
    double P95Ms,
    double P99Ms,
    double AvgRowsReturned,
    int TotalRowsReturned,
    double AvgCpuPercent,
    double MaxCpuPercent,
    double AvgMemPercent,
    double MaxMemPercent,
    double MemLimitBytes,
    double NetInDeltaBytes,
    double NetOutDeltaBytes,
    double BlockInDeltaBytes,
    double BlockOutDeltaBytes);

public static class BenchmarkArtifacts
{
    public static async Task WriteLatenciesAsync(string filePath, IReadOnlyList<BenchmarkLatencyRecord> records)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var writer = new StreamWriter(filePath, false, Encoding.UTF8);
        await writer.WriteLineAsync("phase,request_index,query_name,latency_ms,rows_returned,ok,error");

        foreach (var record in records)
        {
            var line = string.Join(",",
                EscapeCsv(record.Phase),
                record.RequestIndex.ToString(CultureInfo.InvariantCulture),
                EscapeCsv(record.QueryName),
                record.LatencyMs.ToString("G17", CultureInfo.InvariantCulture),
                record.RowsReturned.ToString(CultureInfo.InvariantCulture),
                record.Ok ? "True" : "False",
                EscapeCsv(record.Error));

            await writer.WriteLineAsync(line);
        }
    }

    public static async Task WriteDockerStatsAsync(string filePath, IReadOnlyList<DockerStatsSample> samples)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var writer = new StreamWriter(filePath, false, Encoding.UTF8);
        await writer.WriteLineAsync("timestamp_utc,phase,cpu_percent,mem_percent,mem_used_bytes,mem_limit_bytes,net_in_bytes,net_out_bytes,block_in_bytes,block_out_bytes,cpu_raw,mem_raw,net_raw,block_raw");

        foreach (var sample in samples)
        {
            var line = string.Join(",",
                sample.TimestampUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
                EscapeCsv(sample.Phase),
                sample.CpuPercent.ToString("G17", CultureInfo.InvariantCulture),
                sample.MemPercent.ToString("G17", CultureInfo.InvariantCulture),
                sample.MemUsedBytes.ToString("G17", CultureInfo.InvariantCulture),
                sample.MemLimitBytes.ToString("G17", CultureInfo.InvariantCulture),
                sample.NetInBytes.ToString("G17", CultureInfo.InvariantCulture),
                sample.NetOutBytes.ToString("G17", CultureInfo.InvariantCulture),
                sample.BlockInBytes.ToString("G17", CultureInfo.InvariantCulture),
                sample.BlockOutBytes.ToString("G17", CultureInfo.InvariantCulture),
                EscapeCsv(sample.CpuRaw),
                EscapeCsv(sample.MemRaw),
                EscapeCsv(sample.NetRaw),
                EscapeCsv(sample.BlockRaw));

            await writer.WriteLineAsync(line);
        }
    }

    public static async Task WritePhaseSummaryAsync(string filePath, IReadOnlyList<PhaseSummaryRecord> records)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var writer = new StreamWriter(filePath, false, Encoding.UTF8);
        await writer.WriteLineAsync("db,phase,query_name,duration_sec,total_requests,success_requests,error_requests,ops_per_sec,p50_ms,p95_ms,p99_ms,avg_rows_returned,total_rows_returned,avg_cpu_percent,max_cpu_percent,avg_mem_percent,max_mem_percent,mem_limit_bytes,net_in_delta_bytes,net_out_delta_bytes,block_in_delta_bytes,block_out_delta_bytes");

        foreach (var record in records)
        {
            var line = string.Join(",",
                EscapeCsv(record.Db),
                EscapeCsv(record.Phase),
                EscapeCsv(record.QueryName),
                FormatNumber(record.DurationSec),
                record.TotalRequests.ToString(CultureInfo.InvariantCulture),
                record.SuccessRequests.ToString(CultureInfo.InvariantCulture),
                record.ErrorRequests.ToString(CultureInfo.InvariantCulture),
                FormatNumber(record.OpsPerSec),
                FormatNumber(record.P50Ms),
                FormatNumber(record.P95Ms),
                FormatNumber(record.P99Ms),
                FormatNumber(record.AvgRowsReturned),
                record.TotalRowsReturned.ToString(CultureInfo.InvariantCulture),
                FormatNumber(record.AvgCpuPercent),
                FormatNumber(record.MaxCpuPercent),
                FormatNumber(record.AvgMemPercent),
                FormatNumber(record.MaxMemPercent),
                FormatNumber(record.MemLimitBytes),
                FormatNumber(record.NetInDeltaBytes),
                FormatNumber(record.NetOutDeltaBytes),
                FormatNumber(record.BlockInDeltaBytes),
                FormatNumber(record.BlockOutDeltaBytes));

            await writer.WriteLineAsync(line);
        }
    }

    private static string FormatNumber(double value)
    {
        if (double.IsNaN(value))
        {
            return "nan";
        }

        return value.ToString("G17", CultureInfo.InvariantCulture);
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

public static class DockerStatsSampler
{
    public static DockerStatsSample? TrySample(string phase)
    {
        try
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = "stats --no-stream --format {{json .}}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(processStartInfo);
            if (process == null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);

            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
            {
                return null;
            }

            var line = output
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(line))
            {
                return null;
            }

            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;

            var cpuRaw = ReadString(root, "CPUPerc");
            var memRaw = ReadString(root, "MemUsage");
            var netRaw = ReadString(root, "NetIO");
            var blockRaw = ReadString(root, "BlockIO");

            var cpuPercent = ParsePercent(cpuRaw);
            var memPercent = ParsePercent(ReadString(root, "MemPerc"));
            var (memUsedBytes, memLimitBytes) = ParsePair(memRaw);
            var (netInBytes, netOutBytes) = ParsePair(netRaw);
            var (blockInBytes, blockOutBytes) = ParsePair(blockRaw);

            return new DockerStatsSample(
                DateTimeOffset.UtcNow,
                phase,
                cpuPercent,
                memPercent,
                memUsedBytes,
                memLimitBytes,
                netInBytes,
                netOutBytes,
                blockInBytes,
                blockOutBytes,
                cpuRaw,
                memRaw,
                netRaw,
                blockRaw);
        }
        catch
        {
            return null;
        }
    }

    private static string ReadString(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var value))
        {
            return string.Empty;
        }

        return value.GetString() ?? string.Empty;
    }

    private static double ParsePercent(string value)
    {
        var normalized = value.Trim().TrimEnd('%');
        if (double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return double.NaN;
    }

    private static (double Left, double Right) ParsePair(string value)
    {
        var parts = value.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
        {
            return (0d, 0d);
        }

        return (ParseSize(parts[0]), ParseSize(parts[1]));
    }

    private static double ParseSize(string value)
    {
        var trimmed = value.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return 0d;
        }

        var numberPart = new string(trimmed.TakeWhile(ch => char.IsDigit(ch) || ch == '.' || ch == '-').ToArray());
        var unitPart = trimmed[numberPart.Length..].Trim().ToLowerInvariant();

        if (!double.TryParse(numberPart, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return 0d;
        }

        var multiplier = unitPart switch
        {
            "b" or "" => 1d,
            "kb" => 1_000d,
            "mb" => 1_000_000d,
            "gb" => 1_000_000_000d,
            "tb" => 1_000_000_000_000d,
            "kib" => 1024d,
            "mib" => 1024d * 1024d,
            "gib" => 1024d * 1024d * 1024d,
            "tib" => 1024d * 1024d * 1024d * 1024d,
            _ => 1d
        };

        return number * multiplier;
    }
}
