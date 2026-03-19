using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace redisqa.Services;

public class RedisQueryExecutor
{
    private static readonly Regex SelectAllFromTableRegex = new(
        @"^\s*select\s+\*\s+from\s+([A-Za-z_][A-Za-z0-9_]*)\s*;?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public async Task<QueryExecutionResult> ExecuteAsync(string queryText, int selectedDb)
    {
        if (!RedisConnectionService.Instance.IsConnected)
        {
            return QueryExecutionResult.Fail("Redis is not connected.");
        }

        if (string.IsNullOrWhiteSpace(queryText))
        {
            return QueryExecutionResult.Fail("Query is empty.");
        }

        if (!TryParseSelectAllFromTable(queryText, out var tableName))
        {
            return QueryExecutionResult.Fail("Invalid query format. Expected: SELECT * FROM {table_name}");
        }

        return await ExecuteSelectAllFromTableAsync(tableName, selectedDb);
    }

    private static bool TryParseSelectAllFromTable(string queryText, out string tableName)
    {
        tableName = string.Empty;

        var match = SelectAllFromTableRegex.Match(queryText);
        if (!match.Success)
        {
            return false;
        }

        tableName = match.Groups[1].Value;
        return !string.IsNullOrWhiteSpace(tableName);
    }

    private async Task<QueryExecutionResult> ExecuteSelectAllFromTableAsync(string tableName, int selectedDb)
    {
        var db = RedisConnectionService.Instance.GetDatabase(selectedDb);
        if (db == null)
        {
            return QueryExecutionResult.Fail($"Failed to get Redis database {selectedDb}.");
        }

        var (resolvedTableName, ids) = await LoadIdsAsync(db, tableName, selectedDb);
        if (ids.Count == 0)
        {
            return QueryExecutionResult.Success(
                new List<string> { "id" },
                new List<Dictionary<string, string>>());
        }

        var hashTasks = ids
            .Select(id => db.HashGetAllAsync($"{resolvedTableName}:{id}"))
            .ToArray();

        var hashes = await Task.WhenAll(hashTasks);

        var fieldNames = new List<string>();
        var seenFields = new HashSet<string>(StringComparer.Ordinal);

        foreach (var hash in hashes)
        {
            foreach (var entry in hash)
            {
                var fieldName = entry.Name.ToString();
                if (seenFields.Add(fieldName))
                {
                    fieldNames.Add(fieldName);
                }
            }
        }

        var columns = new List<string> { "id" };
        columns.AddRange(fieldNames);

        var rows = new List<Dictionary<string, string>>(ids.Count);
        for (var index = 0; index < ids.Count; index++)
        {
            var row = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["id"] = ids[index]
            };

            foreach (var fieldName in fieldNames)
            {
                row[fieldName] = string.Empty;
            }

            foreach (var entry in hashes[index])
            {
                row[entry.Name.ToString()] = entry.Value.ToString();
            }

            rows.Add(row);
        }

        return QueryExecutionResult.Success(columns, rows);
    }

    private async Task<(string resolvedTableName, List<string> ids)> LoadIdsAsync(
        IDatabase db,
        string tableName,
        int selectedDb)
    {
        var schemaService = GetSchemaFromRedis.Instance;
        var schemaJson = schemaService.GetCachedSchema(selectedDb);

        if (string.IsNullOrWhiteSpace(schemaJson))
        {
            schemaJson = await schemaService.GetSchemaAsync(selectedDb);
        }

        if (string.IsNullOrWhiteSpace(schemaJson))
        {
            return (tableName, new List<string>());
        }

        var tablePrimaryKeyInfo = TryGetTablePrimaryKeyInfo(schemaJson, tableName);
        if (string.IsNullOrWhiteSpace(tablePrimaryKeyInfo.resolvedTableName) ||
            string.IsNullOrWhiteSpace(tablePrimaryKeyInfo.pkColumn))
        {
            return (tableName, new List<string>());
        }

        var legacySetKey = $"idx:pk:{tablePrimaryKeyInfo.resolvedTableName}:{tablePrimaryKeyInfo.pkColumn}";
        var ids = await db.SetMembersAsync(legacySetKey);

        return (tablePrimaryKeyInfo.resolvedTableName, NormalizeIds(ids));
    }

    private static List<string> NormalizeIds(IEnumerable<RedisValue> values)
    {
        return values
            .Select(value => value.ToString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToList();
    }

    private static (string? resolvedTableName, string? pkColumn) TryGetTablePrimaryKeyInfo(
        string schemaJson,
        string tableName)
    {
        try
        {
            using var jsonDocument = JsonDocument.Parse(schemaJson);
            var root = jsonDocument.RootElement;

            if (!root.TryGetProperty("tables", out var tables))
            {
                return (null, null);
            }

            foreach (var table in tables.EnumerateArray())
            {
                if (!table.TryGetProperty("name", out var nameProperty))
                {
                    continue;
                }

                var currentTableName = nameProperty.GetString();
                if (!string.Equals(currentTableName, tableName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!table.TryGetProperty("attributes", out var attributes))
                {
                    return (currentTableName, null);
                }

                foreach (var attribute in attributes.EnumerateArray())
                {
                    if (!attribute.TryGetProperty("name", out var attributeNameProperty))
                    {
                        continue;
                    }

                    var attributeName = attributeNameProperty.GetString();
                    if (string.IsNullOrWhiteSpace(attributeName))
                    {
                        continue;
                    }

                    if (IsPrimaryKeyAttribute(attribute))
                    {
                        return (currentTableName, attributeName);
                    }
                }

                return (currentTableName, null);
            }

            return (null, null);
        }
        catch
        {
            return (null, null);
        }
    }

    private static bool IsPrimaryKeyAttribute(JsonElement attribute)
    {
        var hasPkLower = TryGetBooleanProperty(attribute, "pk", out var isPkLower) && isPkLower;
        if (hasPkLower)
        {
            return true;
        }

        return TryGetBooleanProperty(attribute, "PK", out var isPkUpper) && isPkUpper;
    }

    private static bool TryGetBooleanProperty(JsonElement element, string propertyName, out bool value)
    {
        value = false;

        if (!element.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.True || property.ValueKind == JsonValueKind.False)
        {
            value = property.GetBoolean();
            return true;
        }

        if (property.ValueKind == JsonValueKind.String && bool.TryParse(property.GetString(), out var parsed))
        {
            value = parsed;
            return true;
        }

        return false;
    }
}

public sealed class QueryExecutionResult
{
    public bool IsSuccess { get; }
    public string? ErrorMessage { get; }
    public IReadOnlyList<string> Columns { get; }
    public IReadOnlyList<Dictionary<string, string>> Rows { get; }

    private QueryExecutionResult(
        bool isSuccess,
        string? errorMessage,
        IReadOnlyList<string> columns,
        IReadOnlyList<Dictionary<string, string>> rows)
    {
        IsSuccess = isSuccess;
        ErrorMessage = errorMessage;
        Columns = columns;
        Rows = rows;
    }

    public static QueryExecutionResult Success(
        IReadOnlyList<string> columns,
        IReadOnlyList<Dictionary<string, string>> rows)
    {
        return new QueryExecutionResult(true, null, columns, rows);
    }

    public static QueryExecutionResult Fail(string errorMessage)
    {
        return new QueryExecutionResult(
            false,
            errorMessage,
            Array.Empty<string>(),
            Array.Empty<Dictionary<string, string>>());
    }
}