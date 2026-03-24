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

    private static readonly Regex SelectProjectedJoinWithLeftWhereRegex = new(
        @"^\s*select\s+([A-Za-z_][A-Za-z0-9_]*)\.([A-Za-z_][A-Za-z0-9_]*)\s*,\s*([A-Za-z_][A-Za-z0-9_]*)\.([A-Za-z_][A-Za-z0-9_]*)\s*,\s*([A-Za-z_][A-Za-z0-9_]*)\.([A-Za-z_][A-Za-z0-9_]*)\s*,\s*([A-Za-z_][A-Za-z0-9_]*)\.([A-Za-z_][A-Za-z0-9_]*)\s+from\s+([A-Za-z_][A-Za-z0-9_]*)\s+([A-Za-z_][A-Za-z0-9_]*)\s+join\s+([A-Za-z_][A-Za-z0-9_]*)\s+([A-Za-z_][A-Za-z0-9_]*)\s+on\s+([A-Za-z_][A-Za-z0-9_]*)\.([A-Za-z_][A-Za-z0-9_]*)\s*=\s*([A-Za-z_][A-Za-z0-9_]*)\.([A-Za-z_][A-Za-z0-9_]*)\s+where\s+([A-Za-z_][A-Za-z0-9_]*)\.([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.+?)\s*;?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SelectJoinWithWhereRegex = new(
        @"^\s*select\s+([A-Za-z_][A-Za-z0-9_]*)\.\*\s*,\s*([A-Za-z_][A-Za-z0-9_]*)\.([A-Za-z_][A-Za-z0-9_]*)\s+from\s+([A-Za-z_][A-Za-z0-9_]*)\s+([A-Za-z_][A-Za-z0-9_]*)\s+join\s+([A-Za-z_][A-Za-z0-9_]*)\s+([A-Za-z_][A-Za-z0-9_]*)\s+on\s+([A-Za-z_][A-Za-z0-9_]*)\.([A-Za-z_][A-Za-z0-9_]*)\s*=\s*([A-Za-z_][A-Za-z0-9_]*)\.([A-Za-z_][A-Za-z0-9_]*)\s+where\s+([A-Za-z_][A-Za-z0-9_]*)\.([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.+?)\s*;?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

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

        if (TryParseSelectProjectedJoinWithLeftWhere(queryText, out var projectedJoinQuery))
        {
            if (pageNumber < 1)
            {
                pageNumber = 1;
            }

            if (pageSize < 1)
            {
                pageSize = 1;
            }

            return await ExecuteSelectProjectedJoinWithLeftWhereAsync(projectedJoinQuery, selectedDb, pageNumber, pageSize);
        }

        if (TryParseSelectJoinWithWhere(queryText, out var joinQuery))
        {
            if (pageNumber < 1)
            {
                pageNumber = 1;
            }

            if (pageSize < 1)
            {
                pageSize = 1;
            }

            return await ExecuteSelectJoinWithWhereAsync(joinQuery, selectedDb, pageNumber, pageSize);
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
                "Invalid query format. Supported: SELECT * FROM {table} | SELECT * FROM {table} WHERE {attr} {op} {val} | SELECT * FROM {table} ORDER BY {attr} LIMIT {n} [OFFSET {m}] | SELECT p.*, s.Name FROM Products p JOIN Sellers s ON s.idSeller = p.Seller_id WHERE s.idSeller = 1 | SELECT oi.Product_id, oi.Quantity, p.Title, p.Price FROM Order_Items oi JOIN Products p ON p.idProduct = oi.Product_id WHERE oi.Order_id = 1. Operators: =, >, <, >=, <=");
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

    private static bool TryParseSelectProjectedJoinWithLeftWhere(string queryText, out ProjectedJoinQueryModel query)
    {
        query = new ProjectedJoinQueryModel();

        var match = SelectProjectedJoinWithLeftWhereRegex.Match(queryText);
        if (!match.Success)
        {
            return false;
        }

        var leftSelectAlias1 = match.Groups[1].Value;
        var leftSelectAttribute1 = match.Groups[2].Value;
        var leftSelectAlias2 = match.Groups[3].Value;
        var leftSelectAttribute2 = match.Groups[4].Value;

        var rightSelectAlias1 = match.Groups[5].Value;
        var rightSelectAttribute1 = match.Groups[6].Value;
        var rightSelectAlias2 = match.Groups[7].Value;
        var rightSelectAttribute2 = match.Groups[8].Value;

        var leftTable = match.Groups[9].Value;
        var leftAlias = match.Groups[10].Value;
        var rightTable = match.Groups[11].Value;
        var rightAlias = match.Groups[12].Value;

        var onLeftAlias = match.Groups[13].Value;
        var onLeftAttribute = match.Groups[14].Value;
        var onRightAlias = match.Groups[15].Value;
        var onRightAttribute = match.Groups[16].Value;

        var whereAlias = match.Groups[17].Value;
        var whereAttribute = match.Groups[18].Value;
        var whereValue = NormalizeWhereValue(match.Groups[19].Value);

        if (!string.Equals(leftSelectAlias1, leftAlias, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(leftSelectAlias2, leftAlias, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(rightSelectAlias1, rightAlias, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(rightSelectAlias2, rightAlias, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(whereAlias, leftAlias, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string leftJoinAttribute;
        string rightJoinAttribute;

        if (string.Equals(onLeftAlias, rightAlias, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(onRightAlias, leftAlias, StringComparison.OrdinalIgnoreCase))
        {
            rightJoinAttribute = onLeftAttribute;
            leftJoinAttribute = onRightAttribute;
        }
        else if (string.Equals(onLeftAlias, leftAlias, StringComparison.OrdinalIgnoreCase) &&
                 string.Equals(onRightAlias, rightAlias, StringComparison.OrdinalIgnoreCase))
        {
            leftJoinAttribute = onLeftAttribute;
            rightJoinAttribute = onRightAttribute;
        }
        else
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(whereValue))
        {
            return false;
        }

        query = new ProjectedJoinQueryModel
        {
            LeftTable = leftTable,
            LeftAlias = leftAlias,
            LeftJoinAttribute = leftJoinAttribute,
            LeftSelectedAttribute1 = leftSelectAttribute1,
            LeftSelectedAttribute2 = leftSelectAttribute2,
            RightTable = rightTable,
            RightAlias = rightAlias,
            RightJoinAttribute = rightJoinAttribute,
            RightSelectedAttribute1 = rightSelectAttribute1,
            RightSelectedAttribute2 = rightSelectAttribute2,
            WhereLeftAttribute = whereAttribute,
            WhereValue = whereValue
        };

        return true;
    }

    private static bool TryParseSelectJoinWithWhere(string queryText, out JoinQueryModel query)
    {
        query = new JoinQueryModel();

        var match = SelectJoinWithWhereRegex.Match(queryText);
        if (!match.Success)
        {
            return false;
        }

        var selectLeftAlias = match.Groups[1].Value;
        var selectRightAlias = match.Groups[2].Value;
        var selectRightAttribute = match.Groups[3].Value;

        var leftTable = match.Groups[4].Value;
        var leftAlias = match.Groups[5].Value;
        var rightTable = match.Groups[6].Value;
        var rightAlias = match.Groups[7].Value;

        var onLeftAlias = match.Groups[8].Value;
        var onLeftAttribute = match.Groups[9].Value;
        var onRightAlias = match.Groups[10].Value;
        var onRightAttribute = match.Groups[11].Value;

        var whereAlias = match.Groups[12].Value;
        var whereAttribute = match.Groups[13].Value;
        var whereValue = NormalizeWhereValue(match.Groups[14].Value);

        if (!string.Equals(selectLeftAlias, leftAlias, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(selectRightAlias, rightAlias, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(whereAlias, rightAlias, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string leftJoinAttribute;
        string rightJoinAttribute;

        if (string.Equals(onLeftAlias, rightAlias, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(onRightAlias, leftAlias, StringComparison.OrdinalIgnoreCase))
        {
            rightJoinAttribute = onLeftAttribute;
            leftJoinAttribute = onRightAttribute;
        }
        else if (string.Equals(onLeftAlias, leftAlias, StringComparison.OrdinalIgnoreCase) &&
                 string.Equals(onRightAlias, rightAlias, StringComparison.OrdinalIgnoreCase))
        {
            leftJoinAttribute = onLeftAttribute;
            rightJoinAttribute = onRightAttribute;
        }
        else
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(whereValue))
        {
            return false;
        }

        query = new JoinQueryModel
        {
            LeftTable = leftTable,
            LeftAlias = leftAlias,
            LeftJoinAttribute = leftJoinAttribute,
            RightTable = rightTable,
            RightAlias = rightAlias,
            RightJoinAttribute = rightJoinAttribute,
            RightSelectedAttribute = selectRightAttribute,
            WhereRightAttribute = whereAttribute,
            WhereValue = whereValue
        };

        return true;
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

    private async Task<QueryExecutionResult> ExecuteSelectProjectedJoinWithLeftWhereAsync(
        ProjectedJoinQueryModel query,
        int selectedDb,
        int pageNumber,
        int pageSize)
    {
        var db = RedisConnectionService.Instance.GetDatabase(selectedDb);
        if (db == null)
        {
            return QueryExecutionResult.Fail($"Failed to get Redis database {selectedDb}.");
        }

        var schemaJson = await GetSchemaJsonAsync(selectedDb);

        if (query.WhereValue == "?")
        {
            return QueryExecutionResult.Fail("Replace '?' in WHERE with a concrete value, for example: WHERE oi.Order_id = 1.");
        }

        var resolvedLeftJoin = ResolveNames(schemaJson, query.LeftTable, query.LeftJoinAttribute);
        var resolvedLeftWhere = ResolveNames(schemaJson, query.LeftTable, query.WhereLeftAttribute);
        var resolvedLeftSelect1 = ResolveNames(schemaJson, query.LeftTable, query.LeftSelectedAttribute1);
        var resolvedLeftSelect2 = ResolveNames(schemaJson, query.LeftTable, query.LeftSelectedAttribute2);
        var resolvedRightJoin = ResolveNames(schemaJson, query.RightTable, query.RightJoinAttribute);
        var resolvedRightSelect1 = ResolveNames(schemaJson, query.RightTable, query.RightSelectedAttribute1);
        var resolvedRightSelect2 = ResolveNames(schemaJson, query.RightTable, query.RightSelectedAttribute2);

        var leftTableName = resolvedLeftJoin.resolvedTableName;
        var leftJoinAttribute = resolvedLeftJoin.resolvedAttributeName;
        var leftWhereAttribute = resolvedLeftWhere.resolvedAttributeName;
        var leftSelectAttribute1 = resolvedLeftSelect1.resolvedAttributeName;
        var leftSelectAttribute2 = resolvedLeftSelect2.resolvedAttributeName;
        var rightTableName = resolvedRightJoin.resolvedTableName;
        var rightJoinAttribute = resolvedRightJoin.resolvedAttributeName;
        var rightSelectAttribute1 = resolvedRightSelect1.resolvedAttributeName;
        var rightSelectAttribute2 = resolvedRightSelect2.resolvedAttributeName;

        if (string.IsNullOrWhiteSpace(leftTableName) || string.IsNullOrWhiteSpace(rightTableName))
        {
            return QueryExecutionResult.Fail("Failed to resolve JOIN table names.");
        }

        var indexSetKey = $"idx:{leftTableName}:{leftWhereAttribute}:{query.WhereValue}";
        var matchedIds = NormalizeIds(await db.SetMembersAsync(indexSetKey));

        var totalRows = matchedIds.Count;
        if (totalRows == 0)
        {
            var emptyColumns = new List<string>
            {
                $"{query.LeftAlias}.{leftSelectAttribute1}",
                $"{query.LeftAlias}.{leftSelectAttribute2}",
                $"{query.RightAlias}.{rightSelectAttribute1}",
                $"{query.RightAlias}.{rightSelectAttribute2}"
            };

            return QueryExecutionResult.Success(
                emptyColumns,
                new List<Dictionary<string, string>>(),
                0,
                1,
                pageSize);
        }

        var totalPages = (int)Math.Ceiling(totalRows / (double)pageSize);
        var normalizedPage = Math.Clamp(pageNumber, 1, totalPages);

        var pagedIds = matchedIds
            .Skip((normalizedPage - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        var leftHashes = await LoadHashesByIdsAsync(db, leftTableName, pagedIds, RedisHashBatchSize);

        var productIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var leftHash in leftHashes)
        {
            foreach (var entry in leftHash)
            {
                if (!string.Equals(entry.Name.ToString(), leftJoinAttribute, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var joinValue = entry.Value.ToString();
                if (!string.IsNullOrWhiteSpace(joinValue))
                {
                    productIds.Add(joinValue);
                }

                break;
            }
        }

        var rightRowsById = await LoadHashesByKeyValuesAsync(db, rightTableName, productIds.ToList(), RedisHashBatchSize);

        var columns = new List<string>
        {
            $"{query.LeftAlias}.{leftSelectAttribute1}",
            $"{query.LeftAlias}.{leftSelectAttribute2}",
            $"{query.RightAlias}.{rightSelectAttribute1}",
            $"{query.RightAlias}.{rightSelectAttribute2}"
        };

        var rows = new List<Dictionary<string, string>>(pagedIds.Count);
        foreach (var leftHash in leftHashes)
        {
            var leftRow = ToDictionary(leftHash);

            leftRow.TryGetValue(leftJoinAttribute, out var joinValue);

            Dictionary<string, string>? rightRow = null;
            if (!string.IsNullOrWhiteSpace(joinValue) && rightRowsById.TryGetValue(joinValue, out var resolvedRightRow))
            {
                rightRow = resolvedRightRow;
            }

            var row = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [columns[0]] = GetColumnValue(leftRow, leftSelectAttribute1),
                [columns[1]] = GetColumnValue(leftRow, leftSelectAttribute2),
                [columns[2]] = rightRow == null ? string.Empty : GetColumnValue(rightRow, rightSelectAttribute1),
                [columns[3]] = rightRow == null ? string.Empty : GetColumnValue(rightRow, rightSelectAttribute2)
            };

            rows.Add(row);
        }

        return QueryExecutionResult.Success(
            columns,
            rows,
            totalRows,
            normalizedPage,
            pageSize);
    }

    private async Task<QueryExecutionResult> ExecuteSelectJoinWithWhereAsync(
        JoinQueryModel query,
        int selectedDb,
        int pageNumber,
        int pageSize)
    {
        var db = RedisConnectionService.Instance.GetDatabase(selectedDb);
        if (db == null)
        {
            return QueryExecutionResult.Fail($"Failed to get Redis database {selectedDb}.");
        }

        var schemaJson = await GetSchemaJsonAsync(selectedDb);

        var resolvedLeft = string.IsNullOrWhiteSpace(schemaJson)
            ? (resolvedTableName: query.LeftTable, resolvedAttributeName: query.LeftJoinAttribute)
            : TryResolveTableAndAttribute(schemaJson, query.LeftTable, query.LeftJoinAttribute);

        var resolvedRightJoin = string.IsNullOrWhiteSpace(schemaJson)
            ? (resolvedTableName: query.RightTable, resolvedAttributeName: query.RightJoinAttribute)
            : TryResolveTableAndAttribute(schemaJson, query.RightTable, query.RightJoinAttribute);

        var resolvedRightSelect = string.IsNullOrWhiteSpace(schemaJson)
            ? (resolvedTableName: query.RightTable, resolvedAttributeName: query.RightSelectedAttribute)
            : TryResolveTableAndAttribute(schemaJson, query.RightTable, query.RightSelectedAttribute);

        var resolvedRightWhere = string.IsNullOrWhiteSpace(schemaJson)
            ? (resolvedTableName: query.RightTable, resolvedAttributeName: query.WhereRightAttribute)
            : TryResolveTableAndAttribute(schemaJson, query.RightTable, query.WhereRightAttribute);

        var resolvedLeftTableName = resolvedLeft.resolvedTableName ?? query.LeftTable;
        var resolvedLeftJoinAttribute = resolvedLeft.resolvedAttributeName ?? query.LeftJoinAttribute;
        var resolvedRightTableName = resolvedRightJoin.resolvedTableName ?? query.RightTable;
        var resolvedRightJoinAttribute = resolvedRightJoin.resolvedAttributeName ?? query.RightJoinAttribute;
        var resolvedRightSelectAttribute = resolvedRightSelect.resolvedAttributeName ?? query.RightSelectedAttribute;
        var resolvedRightWhereAttribute = resolvedRightWhere.resolvedAttributeName ?? query.WhereRightAttribute;

        if (!string.Equals(resolvedRightJoinAttribute, resolvedRightWhereAttribute, StringComparison.OrdinalIgnoreCase))
        {
            return QueryExecutionResult.Fail(
                $"JOIN query expects WHERE on join attribute '{resolvedRightJoinAttribute}'.");
        }

        if (!string.IsNullOrWhiteSpace(schemaJson))
        {
            var rightPkInfo = TryGetTablePrimaryKeyInfo(schemaJson, resolvedRightTableName);
            if (string.IsNullOrWhiteSpace(rightPkInfo.pkColumn))
            {
                return QueryExecutionResult.Fail($"Primary key for table '{resolvedRightTableName}' was not found in schema.");
            }

            if (!string.Equals(rightPkInfo.pkColumn, resolvedRightJoinAttribute, StringComparison.OrdinalIgnoreCase))
            {
                return QueryExecutionResult.Fail(
                    $"JOIN query expects right join attribute to be PK '{rightPkInfo.pkColumn}' for table '{resolvedRightTableName}'.");
            }
        }

        var indexSetKey = $"idx:{resolvedLeftTableName}:{resolvedLeftJoinAttribute}:{query.WhereValue}";
        var matchedIds = NormalizeIds(await db.SetMembersAsync(indexSetKey));

        var totalRows = matchedIds.Count;
        if (totalRows == 0)
        {
            return QueryExecutionResult.Success(
                new List<string> { "id", $"{query.RightAlias}.{resolvedRightSelectAttribute}" },
                new List<Dictionary<string, string>>(),
                0,
                1,
                pageSize);
        }

        var totalPages = (int)Math.Ceiling(totalRows / (double)pageSize);
        var normalizedPage = Math.Clamp(pageNumber, 1, totalPages);

        var pagedIds = matchedIds
            .Skip((normalizedPage - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        var rightKey = $"{resolvedRightTableName}:{query.WhereValue}";
        var rightHash = await db.HashGetAllAsync(rightKey);

        var rightSelectedValue = string.Empty;
        foreach (var entry in rightHash)
        {
            if (string.Equals(entry.Name.ToString(), resolvedRightSelectAttribute, StringComparison.OrdinalIgnoreCase))
            {
                rightSelectedValue = entry.Value.ToString();
                break;
            }
        }

        var joinColumnName = $"{query.RightAlias}.{resolvedRightSelectAttribute}";
        return await BuildResultByIdsWithExtraColumnAsync(
            db,
            resolvedLeftTableName,
            pagedIds,
            totalRows,
            normalizedPage,
            pageSize,
            joinColumnName,
            rightSelectedValue);
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

    private async Task<QueryExecutionResult> BuildResultByIdsWithExtraColumnAsync(
        IDatabase db,
        string resolvedTableName,
        List<string> ids,
        int totalRows,
        int pageNumber,
        int pageSize,
        string extraColumnName,
        string extraColumnValue)
    {
        if (ids.Count == 0)
        {
            return QueryExecutionResult.Success(
                new List<string> { "id", extraColumnName },
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
        columns.Add(extraColumnName);

        var rows = new List<Dictionary<string, string>>(ids.Count);
        for (var index = 0; index < ids.Count; index++)
        {
            var row = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["id"] = ids[index],
                [extraColumnName] = extraColumnValue
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

    private static async Task<Dictionary<string, Dictionary<string, string>>> LoadHashesByKeyValuesAsync(
        IDatabase db,
        string tableName,
        List<string> ids,
        int batchSize)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

        for (var offset = 0; offset < ids.Count; offset += batchSize)
        {
            var chunk = ids
                .Skip(offset)
                .Take(batchSize)
                .ToArray();

            var batch = db.CreateBatch();
            var tasks = new Task<HashEntry[]>[chunk.Length];

            for (var index = 0; index < chunk.Length; index++)
            {
                tasks[index] = batch.HashGetAllAsync($"{tableName}:{chunk[index]}");
            }

            batch.Execute();

            var hashes = await Task.WhenAll(tasks);
            for (var index = 0; index < chunk.Length; index++)
            {
                result[chunk[index]] = ToDictionary(hashes[index]);
            }
        }

        return result;
    }

    private static Dictionary<string, string> ToDictionary(HashEntry[] entries)
    {
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            row[entry.Name.ToString()] = entry.Value.ToString();
        }

        return row;
    }

    private static string GetColumnValue(Dictionary<string, string> row, string columnName)
    {
        return row.TryGetValue(columnName, out var value)
            ? value
            : string.Empty;
    }

    private static (string resolvedTableName, string resolvedAttributeName) ResolveNames(
        string? schemaJson,
        string tableName,
        string attributeName)
    {
        if (string.IsNullOrWhiteSpace(schemaJson))
        {
            return (tableName, attributeName);
        }

        var resolved = TryResolveTableAndAttribute(schemaJson, tableName, attributeName);

        return (
            resolved.resolvedTableName ?? tableName,
            resolved.resolvedAttributeName ?? attributeName);
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

    private sealed class JoinQueryModel
    {
        public string LeftTable { get; init; } = string.Empty;
        public string LeftAlias { get; init; } = string.Empty;
        public string LeftJoinAttribute { get; init; } = string.Empty;
        public string RightTable { get; init; } = string.Empty;
        public string RightAlias { get; init; } = string.Empty;
        public string RightJoinAttribute { get; init; } = string.Empty;
        public string RightSelectedAttribute { get; init; } = string.Empty;
        public string WhereRightAttribute { get; init; } = string.Empty;
        public string WhereValue { get; init; } = string.Empty;
    }

    private sealed class ProjectedJoinQueryModel
    {
        public string LeftTable { get; init; } = string.Empty;
        public string LeftAlias { get; init; } = string.Empty;
        public string LeftJoinAttribute { get; init; } = string.Empty;
        public string LeftSelectedAttribute1 { get; init; } = string.Empty;
        public string LeftSelectedAttribute2 { get; init; } = string.Empty;
        public string RightTable { get; init; } = string.Empty;
        public string RightAlias { get; init; } = string.Empty;
        public string RightJoinAttribute { get; init; } = string.Empty;
        public string RightSelectedAttribute1 { get; init; } = string.Empty;
        public string RightSelectedAttribute2 { get; init; } = string.Empty;
        public string WhereLeftAttribute { get; init; } = string.Empty;
        public string WhereValue { get; init; } = string.Empty;
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