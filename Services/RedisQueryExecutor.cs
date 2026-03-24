using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace redisqa.Services;

public class RedisQueryExecutor
{
    private const int RedisHashBatchSize = 200;
    private const int RedisIndexBatchSize = 200;

    private static readonly Regex SelectWhereConditionFromTableRegex = new(
        @"^\s*select\s+\*\s+from\s+([A-Za-z_][A-Za-z0-9_]*)\s+where\s+([A-Za-z_][A-Za-z0-9_]*)\s*(>=|<=|=|>|<)\s*(.+?)\s*;?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SelectAllFromTableRegex = new(
        @"^\s*select\s+\*\s+from\s+([A-Za-z_][A-Za-z0-9_]*)\s*;?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public async Task<QueryExecutionResult> ExecuteAsync(
        string queryText,
        int selectedDb,
        int pageNumber,
        int pageSize)
    {
        if (!RedisConnectionService.Instance.IsConnected)
        {
            return QueryExecutionResult.Fail("Redis is not connected.");
        }

        if (string.IsNullOrWhiteSpace(queryText))
        {
            return QueryExecutionResult.Fail("Query is empty.");
        }

        if (TryParseSelectWhereConditionFromTable(
                queryText,
                out var whereTableName,
                out var whereAttributeName,
                out var whereOperator,
                out var whereValue))
        {
            if (pageNumber < 1)
            {
                pageNumber = 1;
            }

            if (pageSize < 1)
            {
                pageSize = 1;
            }

            return await ExecuteSelectWhereFromTableAsync(
                whereTableName,
                whereAttributeName,
                whereOperator,
                whereValue,
                selectedDb,
                pageNumber,
                pageSize);
        }

        if (!TryParseSelectAllFromTable(queryText, out var tableName))
        {
            return QueryExecutionResult.Fail(
                "Invalid query format. Expected: SELECT * FROM {table_name} or SELECT * FROM {table_name} WHERE {attribute_name} {=|>|<|>=|<=} {value}");
        }

        if (pageNumber < 1)
        {
            pageNumber = 1;
        }

        if (pageSize < 1)
        {
            pageSize = 1;
        }

        return await ExecuteSelectAllFromTableAsync(tableName, selectedDb, pageNumber, pageSize);
    }

    private static bool TryParseSelectWhereConditionFromTable(
        string queryText,
        out string tableName,
        out string attributeName,
        out string conditionOperator,
        out string value)
    {
        tableName = string.Empty;
        attributeName = string.Empty;
        conditionOperator = string.Empty;
        value = string.Empty;

        var match = SelectWhereConditionFromTableRegex.Match(queryText);
        if (!match.Success)
        {
            return false;
        }

        tableName = match.Groups[1].Value;
        attributeName = match.Groups[2].Value;
        conditionOperator = match.Groups[3].Value;
        value = NormalizeWhereValue(match.Groups[4].Value);

        return !string.IsNullOrWhiteSpace(tableName)
               && !string.IsNullOrWhiteSpace(attributeName)
               && !string.IsNullOrWhiteSpace(conditionOperator)
               && !string.IsNullOrWhiteSpace(value);
    }

    private static string NormalizeWhereValue(string rawValue)
    {
        var value = rawValue.Trim();

        if (value.Length >= 2)
        {
            var hasSingleQuotes = value[0] == '\'' && value[^1] == '\'';
            var hasDoubleQuotes = value[0] == '"' && value[^1] == '"';

            if (hasSingleQuotes || hasDoubleQuotes)
            {
                value = value[1..^1].Trim();
            }
        }

        return value;
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

    private async Task<QueryExecutionResult> ExecuteSelectAllFromTableAsync(
        string tableName,
        int selectedDb,
        int pageNumber,
        int pageSize)
    {
        var db = RedisConnectionService.Instance.GetDatabase(selectedDb);
        if (db == null)
        {
            return QueryExecutionResult.Fail($"Failed to get Redis database {selectedDb}.");
        }

        var (resolvedTableName, ids) = await LoadIdsAsync(db, tableName, selectedDb);
        return await BuildPagedResultAsync(db, resolvedTableName, ids, pageNumber, pageSize);
    }

    private async Task<QueryExecutionResult> ExecuteSelectWhereFromTableAsync(
        string tableName,
        string attributeName,
        string conditionOperator,
        string value,
        int selectedDb,
        int pageNumber,
        int pageSize)
    {
        var db = RedisConnectionService.Instance.GetDatabase(selectedDb);
        if (db == null)
        {
            return QueryExecutionResult.Fail($"Failed to get Redis database {selectedDb}.");
        }

        List<string> ids;
        string resolvedTableName;

        if (conditionOperator == "=")
        {
            (resolvedTableName, ids) = await LoadIdsByIndexedAttributeAsync(
                db,
                tableName,
                attributeName,
                value,
                selectedDb);
        }
        else
        {
            (resolvedTableName, ids) = await LoadIdsByIndexedComparisonAsync(
                db,
                tableName,
                attributeName,
                conditionOperator,
                value,
                selectedDb);
        }

        return await BuildPagedResultAsync(db, resolvedTableName, ids, pageNumber, pageSize);
    }

    private async Task<QueryExecutionResult> BuildPagedResultAsync(
        IDatabase db,
        string resolvedTableName,
        List<string> ids,
        int pageNumber,
        int pageSize)
    {
        var totalRows = ids.Count;

        if (totalRows == 0)
        {
            return QueryExecutionResult.Success(
                new List<string> { "id" },
                new List<Dictionary<string, string>>(),
                totalRows,
                1,
                pageSize);
        }

        var totalPages = (int)Math.Ceiling(totalRows / (double)pageSize);
        var normalizedPageNumber = Math.Clamp(pageNumber, 1, totalPages);

        var pagedIds = ids
            .Skip((normalizedPageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        var hashes = await LoadHashesByIdsAsync(db, resolvedTableName, pagedIds, RedisHashBatchSize);

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

        var rows = new List<Dictionary<string, string>>(pagedIds.Count);
        for (var index = 0; index < pagedIds.Count; index++)
        {
            var row = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["id"] = pagedIds[index]
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

        return QueryExecutionResult.Success(
            columns,
            rows,
            totalRows,
            normalizedPageNumber,
            pageSize);
    }

    private static async Task<List<HashEntry[]>> LoadHashesByIdsAsync(
        IDatabase db,
        string tableName,
        List<string> ids,
        int batchSize)
    {
        var hashes = new List<HashEntry[]>(ids.Count);

        for (var offset = 0; offset < ids.Count; offset += batchSize)
        {
            var chunk = ids
                .Skip(offset)
                .Take(batchSize)
                .ToArray();

            var batch = db.CreateBatch();
            var batchTasks = new Task<HashEntry[]>[chunk.Length];

            for (var index = 0; index < chunk.Length; index++)
            {
                var id = chunk[index];
                batchTasks[index] = batch.HashGetAllAsync($"{tableName}:{id}");
            }

            batch.Execute();

            var chunkHashes = await Task.WhenAll(batchTasks);
            hashes.AddRange(chunkHashes);
        }

        return hashes;
    }

    private async Task<(string resolvedTableName, List<string> ids)> LoadIdsAsync(
        IDatabase db,
        string tableName,
        int selectedDb)
    {
        var schemaJson = await GetSchemaJsonAsync(selectedDb);

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

    private async Task<(string resolvedTableName, List<string> ids)> LoadIdsByIndexedAttributeAsync(
        IDatabase db,
        string tableName,
        string attributeName,
        string value,
        int selectedDb)
    {
        var resolvedTableName = tableName;
        var resolvedAttributeName = attributeName;

        var schemaJson = await GetSchemaJsonAsync(selectedDb);
        if (!string.IsNullOrWhiteSpace(schemaJson))
        {
            var resolvedNames = TryResolveTableAndAttribute(schemaJson, tableName, attributeName);
            if (!string.IsNullOrWhiteSpace(resolvedNames.resolvedTableName))
            {
                resolvedTableName = resolvedNames.resolvedTableName;
            }

            if (!string.IsNullOrWhiteSpace(resolvedNames.resolvedAttributeName))
            {
                resolvedAttributeName = resolvedNames.resolvedAttributeName;
            }
        }

        var indexSetKey = $"idx:{resolvedTableName}:{resolvedAttributeName}:{value}";
        var ids = await db.SetMembersAsync(indexSetKey);

        return (resolvedTableName, NormalizeIds(ids));
    }

    private async Task<(string resolvedTableName, List<string> ids)> LoadIdsByIndexedComparisonAsync(
        IDatabase db,
        string tableName,
        string attributeName,
        string conditionOperator,
        string value,
        int selectedDb)
    {
        var resolvedTableName = tableName;
        var resolvedAttributeName = attributeName;

        var schemaJson = await GetSchemaJsonAsync(selectedDb);
        if (!string.IsNullOrWhiteSpace(schemaJson))
        {
            var resolvedNames = TryResolveTableAndAttribute(schemaJson, tableName, attributeName);
            if (!string.IsNullOrWhiteSpace(resolvedNames.resolvedTableName))
            {
                resolvedTableName = resolvedNames.resolvedTableName;
            }

            if (!string.IsNullOrWhiteSpace(resolvedNames.resolvedAttributeName))
            {
                resolvedAttributeName = resolvedNames.resolvedAttributeName;
            }
        }

        var indexPrefix = $"idx:{resolvedTableName}:{resolvedAttributeName}:";
        var indexKeyPattern = $"{indexPrefix}*";
        var indexKeys = FindIndexKeys(indexKeyPattern, selectedDb);

        if (indexKeys.Count == 0)
        {
            return (resolvedTableName, new List<string>());
        }

        var matchedKeys = indexKeys
            .Where(key =>
            {
                if (!key.StartsWith(indexPrefix, StringComparison.Ordinal))
                {
                    return false;
                }

                var indexedValue = key[indexPrefix.Length..];
                return MatchesComparison(indexedValue, value, conditionOperator);
            })
            .ToList();

        if (matchedKeys.Count == 0)
        {
            return (resolvedTableName, new List<string>());
        }

        var ids = await LoadIdsFromIndexSetsAsync(db, matchedKeys, RedisIndexBatchSize);

        return (resolvedTableName, ids);
    }

    private static List<string> FindIndexKeys(string indexPattern, int selectedDb)
    {
        var connection = RedisConnectionService.Instance.GetConnection();
        if (connection == null)
        {
            return new List<string>();
        }

        var endpoints = connection.GetEndPoints();
        var keys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var endpoint in endpoints)
        {
            var server = connection.GetServer(endpoint);
            if (!server.IsConnected)
            {
                continue;
            }

            foreach (var key in server.Keys(selectedDb, indexPattern, pageSize: 1000))
            {
                keys.Add(key.ToString());
            }
        }

        return keys.OrderBy(key => key, StringComparer.Ordinal).ToList();
    }

    private static bool MatchesComparison(string indexedValue, string targetValue, string conditionOperator)
    {
        var comparison = CompareIndexValues(indexedValue, targetValue);

        return conditionOperator switch
        {
            ">" => comparison > 0,
            "<" => comparison < 0,
            ">=" => comparison >= 0,
            "<=" => comparison <= 0,
            "=" => comparison == 0,
            _ => false
        };
    }

    private static int CompareIndexValues(string left, string right)
    {
        if (decimal.TryParse(left, NumberStyles.Any, CultureInfo.InvariantCulture, out var leftNumber) &&
            decimal.TryParse(right, NumberStyles.Any, CultureInfo.InvariantCulture, out var rightNumber))
        {
            return leftNumber.CompareTo(rightNumber);
        }

        return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<List<string>> LoadIdsFromIndexSetsAsync(
        IDatabase db,
        List<string> indexKeys,
        int batchSize)
    {
        var allIds = new List<string>();

        for (var offset = 0; offset < indexKeys.Count; offset += batchSize)
        {
            var chunk = indexKeys
                .Skip(offset)
                .Take(batchSize)
                .ToArray();

            var batch = db.CreateBatch();
            var batchTasks = new Task<RedisValue[]>[chunk.Length];

            for (var index = 0; index < chunk.Length; index++)
            {
                batchTasks[index] = batch.SetMembersAsync(chunk[index]);
            }

            batch.Execute();

            var chunkResults = await Task.WhenAll(batchTasks);
            foreach (var members in chunkResults)
            {
                allIds.AddRange(NormalizeIds(members));
            }
        }

        return allIds
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
    }

    private static async Task<string?> GetSchemaJsonAsync(int selectedDb)
    {
        var schemaService = GetSchemaFromRedis.Instance;
        var schemaJson = schemaService.GetCachedSchema(selectedDb);

        if (!string.IsNullOrWhiteSpace(schemaJson))
        {
            return schemaJson;
        }

        return await schemaService.GetSchemaAsync(selectedDb);
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

    private static (string? resolvedTableName, string? resolvedAttributeName) TryResolveTableAndAttribute(
        string schemaJson,
        string tableName,
        string attributeName)
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

                    var currentAttributeName = attributeNameProperty.GetString();
                    if (!string.Equals(currentAttributeName, attributeName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    return (currentTableName, currentAttributeName);
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
    public int TotalRows { get; }
    public int PageNumber { get; }
    public int PageSize { get; }

    private QueryExecutionResult(
        bool isSuccess,
        string? errorMessage,
        IReadOnlyList<string> columns,
        IReadOnlyList<Dictionary<string, string>> rows,
        int totalRows,
        int pageNumber,
        int pageSize)
    {
        IsSuccess = isSuccess;
        ErrorMessage = errorMessage;
        Columns = columns;
        Rows = rows;
        TotalRows = totalRows;
        PageNumber = pageNumber;
        PageSize = pageSize;
    }

    public static QueryExecutionResult Success(
        IReadOnlyList<string> columns,
        IReadOnlyList<Dictionary<string, string>> rows,
        int totalRows,
        int pageNumber,
        int pageSize)
    {
        return new QueryExecutionResult(
            true,
            null,
            columns,
            rows,
            totalRows,
            pageNumber,
            pageSize);
    }

    public static QueryExecutionResult Fail(string errorMessage)
    {
        return new QueryExecutionResult(
            false,
            errorMessage,
            Array.Empty<string>(),
            Array.Empty<Dictionary<string, string>>(),
            0,
            1,
            0);
    }
}