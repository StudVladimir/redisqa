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

    private static readonly Regex SelectOrderByLimitOffsetRegex = new(
        @"^\s*select\s+\*\s+from\s+([A-Za-z_][A-Za-z0-9_]*)\s+order\s+by\s+([A-Za-z_][A-Za-z0-9_]*)\s+limit\s+(\d+)(?:\s+offset\s+(\d+))?\s*;?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SelectWhereWithComparisonRegex = new(
        @"^\s*select\s+\*\s+from\s+([A-Za-z_][A-Za-z0-9_]*)\s+where\s+([A-Za-z_][A-Za-z0-9_]*)\s*(>=|<=|>|<|=)\s*(.+?)\s*;?\s*$",
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

        if (TryParseSelectOrderByLimitOffset(queryText, out var orderByTableName, out var orderByAttribute, out var limit, out var offset))
        {
            if (limit < 1)
            {
                limit = 1;
            }

            if (offset < 0)
            {
                offset = 0;
            }

            return await ExecuteSelectWithOrderByAndLimitAsync(
                orderByTableName,
                orderByAttribute,
                limit,
                offset,
                selectedDb);
        }

        if (TryParseSelectWhereWithComparison(queryText, out var whereTableName, out var whereAttributeName, out var whereOperator, out var whereValue))
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
                "Invalid query format. Supported: SELECT * FROM {table} | SELECT * FROM {table} WHERE {attr} {op} {val} | SELECT * FROM {table} ORDER BY {attr} LIMIT {n} [OFFSET {m}]. Operators: =, >, <, >=, <=");
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

    private static bool TryParseSelectOrderByLimitOffset(
        string queryText,
        out string tableName,
        out string orderByAttribute,
        out int limit,
        out int offset)
    {
        tableName = string.Empty;
        orderByAttribute = string.Empty;
        limit = 0;
        offset = 0;

        var match = SelectOrderByLimitOffsetRegex.Match(queryText);
        if (!match.Success)
        {
            return false;
        }

        tableName = match.Groups[1].Value;
        orderByAttribute = match.Groups[2].Value;
        
        if (!int.TryParse(match.Groups[3].Value, out limit) || limit < 1)
        {
            return false;
        }

        if (match.Groups[4].Success && !int.TryParse(match.Groups[4].Value, out offset))
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(tableName) && !string.IsNullOrWhiteSpace(orderByAttribute);
    }

    private static bool TryParseSelectWhereWithComparison(
        string queryText,
        out string tableName,
        out string attributeName,
        out string op,
        out string value)
    {
        tableName = string.Empty;
        attributeName = string.Empty;
        op = string.Empty;
        value = string.Empty;

        var match = SelectWhereWithComparisonRegex.Match(queryText);
        if (!match.Success)
        {
            return false;
        }

        tableName = match.Groups[1].Value;
        attributeName = match.Groups[2].Value;
        op = match.Groups[3].Value;
        value = NormalizeWhereValue(match.Groups[4].Value);

        return !string.IsNullOrWhiteSpace(tableName)
               && !string.IsNullOrWhiteSpace(attributeName)
               && !string.IsNullOrWhiteSpace(op)
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
        string op,
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

        var (resolvedTableName, ids) = await LoadIdsByIndexedAttributeAsync(
            db,
            tableName,
            attributeName,
            op,
            value,
            selectedDb);

        return await BuildPagedResultAsync(db, resolvedTableName, ids, pageNumber, pageSize);
    }

    private async Task<QueryExecutionResult> ExecuteSelectWithOrderByAndLimitAsync(
        string tableName,
        string orderByAttributeName,
        int limit,
        int offset,
        int selectedDb)
    {
        var db = RedisConnectionService.Instance.GetDatabase(selectedDb);
        if (db == null)
        {
            return QueryExecutionResult.Fail($"Failed to get Redis database {selectedDb}.");
        }

        var schemaJson = await GetSchemaJsonAsync(selectedDb);
        if (string.IsNullOrWhiteSpace(schemaJson))
        {
            return QueryExecutionResult.Fail("Schema is not available for ORDER BY query.");
        }

        var tablePrimaryKeyInfo = TryGetTablePrimaryKeyInfo(schemaJson, tableName);
        if (string.IsNullOrWhiteSpace(tablePrimaryKeyInfo.resolvedTableName) ||
            string.IsNullOrWhiteSpace(tablePrimaryKeyInfo.pkColumn))
        {
            return QueryExecutionResult.Fail($"Primary key for table '{tableName}' was not found in schema.");
        }

        if (!string.Equals(orderByAttributeName, tablePrimaryKeyInfo.pkColumn, StringComparison.OrdinalIgnoreCase))
        {
            return QueryExecutionResult.Fail(
                $"ORDER BY supports only primary key '{tablePrimaryKeyInfo.pkColumn}' for table '{tablePrimaryKeyInfo.resolvedTableName}'.");
        }

        var pkSetKey = $"idx:pk:{tablePrimaryKeyInfo.resolvedTableName}:{tablePrimaryKeyInfo.pkColumn}";
        var ids = await db.SetMembersAsync(pkSetKey);

        var orderedIds = SortValues(NormalizeIds(ids));
        var slicedIds = orderedIds
            .Skip(offset)
            .Take(limit)
            .ToList();

        return await BuildResultByIdsAsync(
            db,
            tablePrimaryKeyInfo.resolvedTableName,
            slicedIds,
            slicedIds.Count,
            1,
            limit);
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

    private async Task<QueryExecutionResult> BuildResultByIdsAsync(
        IDatabase db,
        string resolvedTableName,
        List<string> ids,
        int totalRows,
        int pageNumber,
        int pageSize)
    {
        if (ids.Count == 0)
        {
            return QueryExecutionResult.Success(
                new List<string> { "id" },
                new List<Dictionary<string, string>>(),
                totalRows,
                pageNumber,
                pageSize);
        }

        var hashes = await LoadHashesByIdsAsync(db, resolvedTableName, ids, RedisHashBatchSize);

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

        return QueryExecutionResult.Success(
            columns,
            rows,
            totalRows,
            pageNumber,
            pageSize);
    }

    private async Task<(List<string> ids, List<Dictionary<string, string>> sortedRows)> LoadAndSortRecordsAsync(
        IDatabase db,
        string tableName,
        string pkAttribute,
        string orderByAttribute,
        int selectedDb)
    {
        // Get all PK IDs
        var pkSetKey = $"idx:pk:{tableName}:{pkAttribute}";
        var allPkIds = await db.SetMembersAsync(pkSetKey);
        var ids = NormalizeIds(allPkIds);

        if (ids.Count == 0)
        {
            return (new List<string>(), new List<Dictionary<string, string>>());
        }

        // Load all hashes with batching
        var hashes = await LoadHashesByIdsAsync(db, tableName, ids, RedisHashBatchSize);

        // Create rows with sorting information
        var rowsWithSortKey = new List<(Dictionary<string, string> row, IComparable sortKey)>();

        for (var index = 0; index < ids.Count; index++)
        {
            var row = new Dictionary<string, string>(StringComparer.Ordinal) { ["id"] = ids[index] };
            var sortKeyValue = string.Empty;

            foreach (var entry in hashes[index])
            {
                var fieldName = entry.Name.ToString();
                var fieldValue = entry.Value.ToString();
                row[fieldName] = fieldValue;

                if (fieldName == orderByAttribute)
                {
                    sortKeyValue = fieldValue;
                }
            }

            // Try to parse sort key as number for numeric sorting
            if (decimal.TryParse(sortKeyValue, out var numValue))
            {
                rowsWithSortKey.Add((row, numValue));
            }
            else
            {
                rowsWithSortKey.Add((row, sortKeyValue));
            }
        }

        // Sort by the extracted key
        var sortedRows = rowsWithSortKey
            .OrderBy(x => x.sortKey)
            .Select(x => x.row)
            .ToList();

        return (ids, sortedRows);
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
        string op,
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

        // For exact match (=), use the single key lookup
        if (op == "=")
        {
            var indexSetKey = $"idx:{resolvedTableName}:{resolvedAttributeName}:{value}";
            var ids = await db.SetMembersAsync(indexSetKey);
            return (resolvedTableName, NormalizeIds(ids));
        }

        // For comparison operators (>=, <=, >, <), scan all matching keys
        var matchingIds = await LoadIdsByComparisonAsync(db, resolvedTableName, resolvedAttributeName, op, value);
        return (resolvedTableName, matchingIds);
    }

    private async Task<List<string>> LoadIdsByComparisonAsync(
        IDatabase db,
        string tableName,
        string attributeName,
        string op,
        string compareValue)
    {
        var indexKeyPrefix = $"idx:{tableName}:{attributeName}:";
        var connection = RedisConnectionService.Instance.GetConnection();
        
        if (connection == null)
        {
            return new List<string>();
        }

        var server = connection.GetServer(connection.GetEndPoints().FirstOrDefault());
        if (server == null)
        {
            return new List<string>();
        }

        var allIds = new HashSet<string>(StringComparer.Ordinal);

        try
        {
            // Scan all keys matching the pattern idx:table:attribute:*
            var keys = server.Keys(database: db.Database, pattern: $"{indexKeyPrefix}*", pageSize: 1000);

            foreach (var key in keys)
            {
                var keyString = key.ToString();
                
                // Extract value from key: idx:table:attribute:VALUE
                var valuePart = keyString.Substring(indexKeyPrefix.Length);
                
                // Check if value matches the comparison condition
                if (MatchesComparison(valuePart, op, compareValue))
                {
                    // Get all IDs from this set
                    var setMembers = await db.SetMembersAsync(key);
                    foreach (var member in setMembers)
                    {
                        var id = member.ToString();
                        if (!string.IsNullOrWhiteSpace(id))
                        {
                            allIds.Add(id);
                        }
                    }
                }
            }
        }
        catch
        {
            // If scanning fails, return empty list
            return new List<string>();
        }

        return allIds
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
    }

    private static bool MatchesComparison(string actualValue, string op, string compareValue)
    {
        // Try to parse as comparable values (support both numeric and string comparisons)
        if (decimal.TryParse(actualValue, out var actualDecimal) && 
            decimal.TryParse(compareValue, out var compareDecimal))
        {
            return op switch
            {
                ">" => actualDecimal > compareDecimal,
                "<" => actualDecimal < compareDecimal,
                ">=" => actualDecimal >= compareDecimal,
                "<=" => actualDecimal <= compareDecimal,
                "=" => actualDecimal == compareDecimal,
                _ => false
            };
        }

        // Fall back to string comparison
        var comparison = string.Compare(actualValue, compareValue, StringComparison.Ordinal);
        return op switch
        {
            ">" => comparison > 0,
            "<" => comparison < 0,
            ">=" => comparison >= 0,
            "<=" => comparison <= 0,
            "=" => comparison == 0,
            _ => false
        };
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

    private static List<string> SortValues(List<string> values)
    {
        return values
            .OrderBy(value => value, Comparer<string>.Create(CompareIndexValues))
            .ToList();
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