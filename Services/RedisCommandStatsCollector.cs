using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace redisqa.Services;

public sealed record RedisCommandCounters(long Calls, long Usec, long RejectedCalls, long FailedCalls)
{
    public static readonly RedisCommandCounters Zero = new(0, 0, 0, 0);

    public RedisCommandCounters Add(RedisCommandCounters other)
    {
        return new RedisCommandCounters(
            Calls + other.Calls,
            Usec + other.Usec,
            RejectedCalls + other.RejectedCalls,
            FailedCalls + other.FailedCalls);
    }
}

public sealed class RedisCommandStatsSnapshot
{
    public IReadOnlyDictionary<string, RedisCommandCounters> ByCommand { get; }

    public RedisCommandStatsSnapshot(IReadOnlyDictionary<string, RedisCommandCounters> byCommand)
    {
        ByCommand = byCommand;
    }
}

public sealed class RedisCommandStatsCollector
{
    public async Task<RedisCommandStatsSnapshot?> TryTakeSnapshotAsync()
    {
        try
        {
            var connection = RedisConnectionService.Instance.GetConnection();
            if (connection == null || !connection.IsConnected)
            {
                return null;
            }

            var endpoint = connection.GetEndPoints().FirstOrDefault();
            if (endpoint == null)
            {
                return null;
            }

            var server = connection.GetServer(endpoint);
            if (server == null || !server.IsConnected)
            {
                return null;
            }

            var raw = await server.ExecuteAsync("INFO", "commandstats");
            var text = raw.ToString();
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            return new RedisCommandStatsSnapshot(ParseInfoCommandStats(text));
        }
        catch
        {
            return null;
        }
    }

    public static Dictionary<string, RedisCommandCounters> BuildDelta(
        RedisCommandStatsSnapshot? before,
        RedisCommandStatsSnapshot? after)
    {
        var result = new Dictionary<string, RedisCommandCounters>(StringComparer.OrdinalIgnoreCase);
        if (before == null || after == null)
        {
            return result;
        }

        var commandNames = new HashSet<string>(before.ByCommand.Keys, StringComparer.OrdinalIgnoreCase);
        commandNames.UnionWith(after.ByCommand.Keys);

        foreach (var command in commandNames)
        {
            var beforeValue = before.ByCommand.TryGetValue(command, out var beforeCounters)
                ? beforeCounters
                : RedisCommandCounters.Zero;
            var afterValue = after.ByCommand.TryGetValue(command, out var afterCounters)
                ? afterCounters
                : RedisCommandCounters.Zero;

            var calls = Math.Max(0, afterValue.Calls - beforeValue.Calls);
            var usec = Math.Max(0, afterValue.Usec - beforeValue.Usec);
            var rejected = Math.Max(0, afterValue.RejectedCalls - beforeValue.RejectedCalls);
            var failed = Math.Max(0, afterValue.FailedCalls - beforeValue.FailedCalls);

            if (calls == 0 && usec == 0 && rejected == 0 && failed == 0)
            {
                continue;
            }

            result[command] = new RedisCommandCounters(calls, usec, rejected, failed);
        }

        return result;
    }

    private static Dictionary<string, RedisCommandCounters> ParseInfoCommandStats(string text)
    {
        var result = new Dictionary<string, RedisCommandCounters>(StringComparer.OrdinalIgnoreCase);

        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            if (!line.StartsWith("cmdstat_", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var colonIndex = line.IndexOf(':');
            if (colonIndex <= 0 || colonIndex >= line.Length - 1)
            {
                continue;
            }

            var command = line["cmdstat_".Length..colonIndex].Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(command))
            {
                continue;
            }

            var statsPayload = line[(colonIndex + 1)..];
            var values = ParseStatsPayload(statsPayload);

            var counters = new RedisCommandCounters(
                GetLong(values, "calls"),
                GetLong(values, "usec"),
                GetLong(values, "rejected_calls"),
                GetLong(values, "failed_calls"));

            result[command] = counters;
        }

        return result;
    }

    private static Dictionary<string, string> ParseStatsPayload(string payload)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var parts = payload.Split(',', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var separatorIndex = part.IndexOf('=');
            if (separatorIndex <= 0 || separatorIndex >= part.Length - 1)
            {
                continue;
            }

            var key = part[..separatorIndex].Trim();
            var value = part[(separatorIndex + 1)..].Trim();

            if (!string.IsNullOrWhiteSpace(key))
            {
                result[key] = value;
            }
        }

        return result;
    }

    private static long GetLong(IReadOnlyDictionary<string, string> values, string key)
    {
        if (!values.TryGetValue(key, out var value))
        {
            return 0;
        }

        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;
    }
}
