using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using redisqa.Services;

namespace redisqa.ViewModels;

public class QueryViewModel : BaseViewModel
{
    private const int BenchmarkRepeatCount = 1000;
    private const int SequentialWarmupRequestCount = 200;
    private const int SequentialRequestsPerQuery = 1000;
    private const string BenchmarkModeSingleLegacy = "Single query (legacy)";
    private const string BenchmarkModeSequential = "Sequential (full)";
    private static readonly Regex BenchmarkIncrementIdRegex = new(
        @"(\bwhere\s+(?:[A-Za-z_][A-Za-z0-9_]*\.)?idUser\s*=\s*)(['""]?)(\d+)\2",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly RedisQueryExecutor _queryExecutor = new();

    private int _selectedDb;
    private string _queryText = string.Empty;
    private bool _isBusy;
    private int _pageSize = 200;
    private int _currentPage = 1;
    private int _totalRows;
    private string _queryErrorMessage = string.Empty;
    private string _emptyStateMessage = "Run a query to see results";
    private bool _hasExecutedQuery;
    private string _lastExecutedQuery = string.Empty;
    private string _benchmarkQueryName = string.Empty;
    private string _benchmarkStatusMessage = string.Empty;
    private string _selectedBenchmarkMode = BenchmarkModeSequential;

    public int SelectedDb
    {
        get => _selectedDb;
        set
        {
            _selectedDb = value;
            OnPropertyChanged();
        }
    }

    public string QueryText
    {
        get => _queryText;
        set
        {
            _queryText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanRunQuery));
        }
    }

    public string BenchmarkQueryName
    {
        get => _benchmarkQueryName;
        set
        {
            _benchmarkQueryName = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanRunQuery));
        }
    }

    public string BenchmarkStatusMessage
    {
        get => _benchmarkStatusMessage;
        set
        {
            _benchmarkStatusMessage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasBenchmarkStatus));
        }
    }

    public bool HasBenchmarkStatus => !string.IsNullOrWhiteSpace(BenchmarkStatusMessage);

    public ObservableCollection<string> BenchmarkModeOptions { get; } =
    [
        BenchmarkModeSequential,
        BenchmarkModeSingleLegacy
    ];

    public string SelectedBenchmarkMode
    {
        get => _selectedBenchmarkMode;
        set
        {
            _selectedBenchmarkMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSingleBenchmarkMode));
            OnPropertyChanged(nameof(CanRunQuery));
        }
    }

    public bool IsSingleBenchmarkMode =>
        string.Equals(SelectedBenchmarkMode, BenchmarkModeSingleLegacy, StringComparison.Ordinal);

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            _isBusy = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanRunQuery));
            OnPropertyChanged(nameof(CanGoPrevious));
            OnPropertyChanged(nameof(CanGoNext));
        }
    }

    public ObservableCollection<int> PageSizeOptions { get; } = new() { 50, 100, 200, 500 };

    public int PageSize
    {
        get => _pageSize;
        set
        {
            if (value < 1 || _pageSize == value)
            {
                return;
            }

            _pageSize = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TotalPages));
            OnPropertyChanged(nameof(PageStatusText));
            OnPropertyChanged(nameof(RowsInfoText));
            OnPropertyChanged(nameof(CanGoNext));
        }
    }

    public int CurrentPage
    {
        get => _currentPage;
        private set
        {
            if (_currentPage == value)
            {
                return;
            }

            _currentPage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PageStatusText));
            OnPropertyChanged(nameof(RowsInfoText));
            OnPropertyChanged(nameof(CanGoPrevious));
            OnPropertyChanged(nameof(CanGoNext));
        }
    }

    public int TotalRows
    {
        get => _totalRows;
        private set
        {
            if (_totalRows == value)
            {
                return;
            }

            _totalRows = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TotalPages));
            OnPropertyChanged(nameof(PageStatusText));
            OnPropertyChanged(nameof(RowsInfoText));
            OnPropertyChanged(nameof(CanGoPrevious));
            OnPropertyChanged(nameof(CanGoNext));
        }
    }

    public string QueryErrorMessage
    {
        get => _queryErrorMessage;
        set
        {
            _queryErrorMessage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasQueryError));
        }
    }

    public bool HasQueryError => !string.IsNullOrWhiteSpace(QueryErrorMessage);

    public string EmptyStateMessage
    {
        get => _emptyStateMessage;
        set
        {
            _emptyStateMessage = value;
            OnPropertyChanged();
        }
    }

    public bool CanRunQuery => !IsBusy
                               && !string.IsNullOrWhiteSpace(QueryText)
                               && (!IsSingleBenchmarkMode || !string.IsNullOrWhiteSpace(BenchmarkQueryName));

    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalRows / (double)PageSize));

    public string PageStatusText => $"Page: {CurrentPage}/{TotalPages}";

    public string RowsInfoText
    {
        get
        {
            if (TotalRows <= 0 || QueryResults.Count == 0)
            {
                return "Rows: 0";
            }

            var from = ((CurrentPage - 1) * PageSize) + 1;
            var to = from + QueryResults.Count - 1;

            return $"Rows: {from}-{to} / {TotalRows}";
        }
    }

    public bool CanGoPrevious => !IsBusy && _hasExecutedQuery && CurrentPage > 1;

    public bool CanGoNext => !IsBusy && _hasExecutedQuery && CurrentPage < TotalPages;

    public ObservableCollection<string> QueryColumns { get; } = new();

    public ObservableCollection<QueryResultRow> QueryResults { get; } = new();

    public bool HasResults => QueryResults.Count > 0;

    public QueryViewModel()
    {
        QueryResults.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasResults));
            OnPropertyChanged(nameof(RowsInfoText));
            OnPropertyChanged(nameof(CanGoPrevious));
            OnPropertyChanged(nameof(CanGoNext));
        };
    }

    public async Task RunQueryAsync(bool resetPage = true)
    {
        if (IsBusy
            || string.IsNullOrWhiteSpace(QueryText)
            || (IsSingleBenchmarkMode && string.IsNullOrWhiteSpace(BenchmarkQueryName)))
        {
            return;
        }

        _lastExecutedQuery = QueryText;
        _hasExecutedQuery = true;

        if (resetPage)
        {
            CurrentPage = 1;
        }

        var executionResult = await ExecuteCurrentQueryAsync();
        if (executionResult is null || !executionResult.IsSuccess)
        {
            return;
        }

        if (!resetPage)
        {
            return;
        }

        await RunBenchmarkAsync(_lastExecutedQuery, BenchmarkQueryName.Trim(), CurrentPage, PageSize);
    }

    public async Task LoadPreviousPageAsync()
    {
        if (!CanGoPrevious)
        {
            return;
        }

        CurrentPage--;
        await ExecuteCurrentQueryAsync();
    }

    public async Task LoadNextPageAsync()
    {
        if (!CanGoNext)
        {
            return;
        }

        CurrentPage++;
        await ExecuteCurrentQueryAsync();
    }

    public async Task ReloadWithCurrentPageSizeAsync()
    {
        if (!_hasExecutedQuery || IsBusy)
        {
            return;
        }

        CurrentPage = 1;
        await ExecuteCurrentQueryAsync();
    }

    private async Task<QueryExecutionResult?> ExecuteCurrentQueryAsync()
    {
        if (string.IsNullOrWhiteSpace(_lastExecutedQuery))
        {
            return null;
        }

        IsBusy = true;
        QueryErrorMessage = string.Empty;

        try
        {
            var executionResult = await _queryExecutor.ExecuteAsync(
                _lastExecutedQuery,
                SelectedDb,
                CurrentPage,
                PageSize);

            if (!executionResult.IsSuccess)
            {
                QueryColumns.Clear();
                QueryResults.Clear();
                TotalRows = 0;
                CurrentPage = 1;
                _hasExecutedQuery = false;
                QueryErrorMessage = executionResult.ErrorMessage ?? "Query execution failed.";
                EmptyStateMessage = "Run a valid query to see results.";
                return executionResult;
            }

            TotalRows = executionResult.TotalRows;
            CurrentPage = executionResult.PageNumber;

            ApplyExecutionResult(executionResult);
            EmptyStateMessage = TotalRows == 0
                ? "No rows found."
                : "Run a query to see results";

            return executionResult;
        }
        catch (Exception ex)
        {
            QueryColumns.Clear();
            QueryResults.Clear();
            TotalRows = 0;
            CurrentPage = 1;
            _hasExecutedQuery = false;
            QueryErrorMessage = $"Query execution failed: {ex.Message}";
            EmptyStateMessage = "Run a valid query to see results.";
            return QueryExecutionResult.Fail(QueryErrorMessage);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RunBenchmarkAsync(string queryText, string queryName, int pageNumber, int pageSize)
    {
        if (IsSingleBenchmarkMode)
        {
            await RunSingleQueryBenchmarkAsync(queryText, queryName, pageNumber, pageSize);
            return;
        }

        await RunSequentialBenchmarkAsync(pageNumber, pageSize);
    }

    private async Task RunSingleQueryBenchmarkAsync(string queryText, string queryName, int pageNumber, int pageSize)
    {
        IsBusy = true;
        BenchmarkStatusMessage = $"Benchmark started: 0/{BenchmarkRepeatCount}";

        var serverRecords = new List<BenchmarkCsvRecord>(BenchmarkRepeatCount);
        var clientRecords = new List<BenchmarkCsvRecord>(BenchmarkRepeatCount);

        try
        {
            for (var index = 1; index <= BenchmarkRepeatCount; index++)
            {
                QueryExecutionResult iterationResult;
                var clientStopwatch = Stopwatch.StartNew();
                var queryForIteration = BuildBenchmarkQueryForIteration(queryText, index);

                try
                {
                    iterationResult = await _queryExecutor.ExecuteAsync(queryForIteration, SelectedDb, pageNumber, pageSize);
                }
                catch (Exception ex)
                {
                    iterationResult = QueryExecutionResult.Fail($"Query execution failed: {ex.Message}");
                }

                if (iterationResult.IsSuccess)
                {
                    SimulateClientResultMaterialization(iterationResult);
                }

                clientStopwatch.Stop();

                var rowsReturned = iterationResult.IsSuccess ? iterationResult.Rows.Count : 0;
                var error = iterationResult.IsSuccess
                    ? string.Empty
                    : iterationResult.ErrorMessage ?? "Query execution failed.";

                serverRecords.Add(new BenchmarkCsvRecord(
                    index,
                    queryName,
                    iterationResult.ServerLatencyMs,
                    rowsReturned,
                    iterationResult.IsSuccess,
                    error));

                clientRecords.Add(new BenchmarkCsvRecord(
                    index,
                    queryName,
                    clientStopwatch.Elapsed.TotalMilliseconds,
                    rowsReturned,
                    iterationResult.IsSuccess,
                    error));

                if (index % 50 == 0 || index == BenchmarkRepeatCount)
                {
                    BenchmarkStatusMessage = $"Benchmark progress: {index}/{BenchmarkRepeatCount}";
                }
            }

            var outputDir = Path.Combine(Directory.GetCurrentDirectory(), "benchmark-results");
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var serverCsvPath = Path.Combine(outputDir, $"server_metrics_{timestamp}.csv");
            var clientCsvPath = Path.Combine(outputDir, $"client_metrics_{timestamp}.csv");

            await BenchmarkCsvWriter.WriteAsync(serverCsvPath, serverRecords);
            await BenchmarkCsvWriter.WriteAsync(clientCsvPath, clientRecords);

            BenchmarkStatusMessage = $"Benchmark complete. Server CSV: {serverCsvPath} | Client CSV: {clientCsvPath}";
            Debug.WriteLine(BenchmarkStatusMessage);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RunSequentialBenchmarkAsync(int pageNumber, int pageSize)
    {
        IsBusy = true;

        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var outputDir = Path.Combine(Directory.GetCurrentDirectory(), "benchmark-results", timestamp + "_sequential");
        Directory.CreateDirectory(outputDir);

        var queryDefinitions = await BuildSequentialQueryDefinitionsAsync();
        var latencyRecords = new List<BenchmarkLatencyRecord>(
            SequentialWarmupRequestCount + (SequentialRequestsPerQuery * Math.Max(1, queryDefinitions.Count)));
        var dockerSamples = new List<DockerStatsSample>();

        try
        {
            if (queryDefinitions.Count == 0)
            {
                BenchmarkStatusMessage = "Sequential benchmark skipped: no query definitions are available.";
                return;
            }

            BenchmarkStatusMessage = $"Sequential benchmark warmup: 0/{SequentialWarmupRequestCount}";
            TrySampleDocker("warmup", dockerSamples);

            for (var index = 0; index < SequentialWarmupRequestCount; index++)
            {
                var definition = queryDefinitions[index % queryDefinitions.Count];
                var query = definition.BuildQuery(index + 1);
                var iteration = await ExecuteBenchmarkIterationAsync(query, pageNumber, pageSize);

                latencyRecords.Add(new BenchmarkLatencyRecord(
                    "warmup",
                    index,
                    definition.QueryName,
                    iteration.ClientLatencyMs,
                    iteration.RowsReturned,
                    iteration.IsSuccess,
                    iteration.Error));

                if ((index + 1) % 25 == 0 || index + 1 == SequentialWarmupRequestCount)
                {
                    BenchmarkStatusMessage = $"Sequential warmup progress: {index + 1}/{SequentialWarmupRequestCount}";
                }

                if ((index + 1) % 50 == 0)
                {
                    TrySampleDocker("warmup", dockerSamples);
                }
            }

            TrySampleDocker("warmup", dockerSamples);

            var totalBenchmarkRequests = SequentialRequestsPerQuery * queryDefinitions.Count;
            var benchmarkProgress = 0;
            BenchmarkStatusMessage = $"Sequential benchmark: 0/{totalBenchmarkRequests}";
            TrySampleDocker("benchmark", dockerSamples);

            foreach (var definition in queryDefinitions)
            {
                for (var iterationIndex = 1; iterationIndex <= SequentialRequestsPerQuery; iterationIndex++)
                {
                    var query = definition.BuildQuery(iterationIndex);
                    var iteration = await ExecuteBenchmarkIterationAsync(query, pageNumber, pageSize);

                    latencyRecords.Add(new BenchmarkLatencyRecord(
                        "benchmark",
                        iterationIndex,
                        definition.QueryName,
                        iteration.ClientLatencyMs,
                        iteration.RowsReturned,
                        iteration.IsSuccess,
                        iteration.Error));

                    benchmarkProgress++;

                    if (benchmarkProgress % 50 == 0 || benchmarkProgress == totalBenchmarkRequests)
                    {
                        BenchmarkStatusMessage = $"Sequential benchmark progress: {benchmarkProgress}/{totalBenchmarkRequests}";
                    }

                    if (benchmarkProgress % 100 == 0)
                    {
                        TrySampleDocker("benchmark", dockerSamples);
                    }
                }
            }

            TrySampleDocker("benchmark", dockerSamples);

            var phaseSummary = BuildPhaseSummary(
                latencyRecords,
                dockerSamples,
                queryDefinitions.Select(x => x.QueryName).ToList());

            var latenciesPath = Path.Combine(outputDir, "latencies.csv");
            var dockerStatsPath = Path.Combine(outputDir, "docker_stats_samples.csv");
            var phaseSummaryPath = Path.Combine(outputDir, "phase_summary.csv");

            await BenchmarkArtifacts.WriteLatenciesAsync(latenciesPath, latencyRecords);
            await BenchmarkArtifacts.WriteDockerStatsAsync(dockerStatsPath, dockerSamples);
            await BenchmarkArtifacts.WritePhaseSummaryAsync(phaseSummaryPath, phaseSummary);

            BenchmarkStatusMessage =
                $"Sequential benchmark complete. Latencies: {latenciesPath} | Docker: {dockerStatsPath} | Summary: {phaseSummaryPath}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<List<SequentialQueryDefinition>> BuildSequentialQueryDefinitionsAsync()
    {
        var loginValues = await LoadUserLoginsAsync();

        const int userMaxId = 10000;
        const int sellerMaxId = 100;
        const int orderMaxId = 100000;

        return
        [
            new SequentialQueryDefinition(
                "q1_user_by_id",
                iterationIndex => $"SELECT * FROM Users WHERE idUser = {BuildPositiveId(iterationIndex, userMaxId)};"),

            new SequentialQueryDefinition(
                "q2_all_users",
                _ => "SELECT * FROM Users;"),

            new SequentialQueryDefinition(
                "q3_user_by_login",
                iterationIndex =>
                {
                    var login = loginValues.Count == 0
                        ? $"user{BuildPositiveId(iterationIndex, userMaxId)}"
                        : loginValues[(iterationIndex - 1) % loginValues.Count];

                    return $"SELECT * FROM Users WHERE Login = '{EscapeSqlLiteral(login)}';";
                }),

            new SequentialQueryDefinition(
                "q4_sellers_rating",
                _ => "SELECT * FROM Sellers WHERE Rating >= 4 LIMIT 100;"),

            new SequentialQueryDefinition(
                "q5_orders_offset",
                _ => "SELECT * FROM Orders ORDER BY idOrder LIMIT 50 OFFSET 5000;"),

            new SequentialQueryDefinition(
                "q6_products_by_seller",
                iterationIndex => $"SELECT * FROM Products WHERE Seller_id = {BuildPositiveId(iterationIndex, sellerMaxId)};"),

            new SequentialQueryDefinition(
                "q7_products_join_sellers",
                iterationIndex =>
                    $"SELECT p.*, s.Name FROM Products p JOIN Sellers s ON s.idSeller = p.Seller_id WHERE s.idSeller = {BuildPositiveId(iterationIndex, sellerMaxId)};"),

            new SequentialQueryDefinition(
                "q8_order_items_join_products",
                iterationIndex =>
                    $"SELECT oi.Product_id, oi.Quantity, p.Title, p.Price FROM Order_Items oi JOIN Products p ON p.idProduct = oi.Product_id WHERE oi.Order_id = {BuildPositiveId(iterationIndex, orderMaxId)};"),

            new SequentialQueryDefinition(
                "q9_orders_items_products_by_user",
                iterationIndex =>
                    $"SELECT o.idOrder, o.Create_Date, oi.Product_id, oi.Quantity, p.Title, p.Price FROM Orders o JOIN Order_Items oi ON oi.Order_id = o.idOrder JOIN Products p ON p.idProduct = oi.Product_id WHERE o.Users_id = {BuildPositiveId(iterationIndex, userMaxId)};"),

            new SequentialQueryDefinition(
                "q10_top_sellers_qty",
                _ =>
                    "SELECT p.Seller_id, COUNT(*) AS items, SUM(oi.Quantity) AS qty FROM Order_Items oi JOIN Products p ON p.idProduct = oi.Product_id GROUP BY p.Seller_id ORDER BY qty DESC LIMIT 20;"),

            new SequentialQueryDefinition(
                "q11_top_categories",
                _ =>
                    "SELECT pc.Category_id, COUNT(*) cnt FROM Product_Categories pc GROUP BY pc.Category_id ORDER BY cnt DESC LIMIT 10;")
        ];
    }

    private static int BuildPositiveId(int iterationIndex, int maxValue)
    {
        if (maxValue < 1)
        {
            return 1;
        }

        return 1 + ((iterationIndex - 1) % maxValue);
    }

    private async Task<List<string>> LoadUserLoginsAsync()
    {
        var result = await _queryExecutor.ExecuteAsync("SELECT * FROM Users", SelectedDb, 1, 500);
        if (!result.IsSuccess || result.Rows.Count == 0)
        {
            return new List<string>();
        }

        var loginColumn = result.Columns.FirstOrDefault(column =>
            string.Equals(column, "Login", StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(loginColumn))
        {
            return new List<string>();
        }

        return result.Rows
            .Select(row => row.TryGetValue(loginColumn, out var login) ? login : string.Empty)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private async Task<BenchmarkIterationResult> ExecuteBenchmarkIterationAsync(string query, int pageNumber, int pageSize)
    {
        QueryExecutionResult iterationResult;
        var clientStopwatch = Stopwatch.StartNew();

        try
        {
            iterationResult = await _queryExecutor.ExecuteAsync(query, SelectedDb, pageNumber, pageSize);
        }
        catch (Exception ex)
        {
            iterationResult = QueryExecutionResult.Fail($"Query execution failed: {ex.Message}");
        }

        if (iterationResult.IsSuccess)
        {
            SimulateClientResultMaterialization(iterationResult);
        }

        clientStopwatch.Stop();

        return new BenchmarkIterationResult(
            iterationResult.IsSuccess,
            clientStopwatch.Elapsed.TotalMilliseconds,
            iterationResult.IsSuccess ? iterationResult.Rows.Count : 0,
            iterationResult.IsSuccess
                ? string.Empty
                : iterationResult.ErrorMessage ?? "Query execution failed.");
    }

    private static string EscapeSqlLiteral(string value)
    {
        return value.Replace("'", "''", StringComparison.Ordinal);
    }

    private static void TrySampleDocker(string phase, ICollection<DockerStatsSample> sink)
    {
        var sample = DockerStatsSampler.TrySample(phase);
        if (sample != null)
        {
            sink.Add(sample);
        }
    }

    private static List<PhaseSummaryRecord> BuildPhaseSummary(
        IReadOnlyList<BenchmarkLatencyRecord> latencyRecords,
        IReadOnlyList<DockerStatsSample> dockerSamples,
        IReadOnlyList<string> queryNames)
    {
        var summary = new List<PhaseSummaryRecord>();

        summary.AddRange(BuildPhaseSummaryRows(
            "warmup",
            latencyRecords,
            dockerSamples,
            queryNames));

        summary.AddRange(BuildPhaseSummaryRows(
            "benchmark",
            latencyRecords,
            dockerSamples,
            queryNames));

        return summary;
    }

    private static IEnumerable<PhaseSummaryRecord> BuildPhaseSummaryRows(
        string phase,
        IReadOnlyList<BenchmarkLatencyRecord> allLatencies,
        IReadOnlyList<DockerStatsSample> allDockerSamples,
        IReadOnlyList<string> queryNames)
    {
        var latenciesInPhase = allLatencies.Where(row => string.Equals(row.Phase, phase, StringComparison.Ordinal)).ToList();
        var dockerInPhase = allDockerSamples.Where(row => string.Equals(row.Phase, phase, StringComparison.Ordinal)).ToList();

        var avgCpu = dockerInPhase.Count == 0 ? double.NaN : dockerInPhase.Average(sample => sample.CpuPercent);
        var maxCpu = dockerInPhase.Count == 0 ? double.NaN : dockerInPhase.Max(sample => sample.CpuPercent);
        var avgMem = dockerInPhase.Count == 0 ? double.NaN : dockerInPhase.Average(sample => sample.MemPercent);
        var maxMem = dockerInPhase.Count == 0 ? double.NaN : dockerInPhase.Max(sample => sample.MemPercent);
        var memLimit = dockerInPhase.Count == 0 ? double.NaN : dockerInPhase.Average(sample => sample.MemLimitBytes);

        var netInDelta = ComputeDelta(dockerInPhase.Select(sample => sample.NetInBytes));
        var netOutDelta = ComputeDelta(dockerInPhase.Select(sample => sample.NetOutBytes));
        var blockInDelta = ComputeDelta(dockerInPhase.Select(sample => sample.BlockInBytes));
        var blockOutDelta = ComputeDelta(dockerInPhase.Select(sample => sample.BlockOutBytes));

        foreach (var queryName in queryNames)
        {
            var queryRows = latenciesInPhase
                .Where(row => string.Equals(row.QueryName, queryName, StringComparison.Ordinal))
                .ToList();

            var queryDurationSec = queryRows.Sum(row => row.LatencyMs) / 1000d;

            var totalRequests = queryRows.Count;
            var successRequests = queryRows.Count(row => row.Ok);
            var errorRequests = totalRequests - successRequests;
            var successfulLatencies = queryRows.Where(row => row.Ok).Select(row => row.LatencyMs).ToList();
            var successfulRows = queryRows.Where(row => row.Ok).Select(row => row.RowsReturned).ToList();

            yield return new PhaseSummaryRecord(
                "redis",
                phase,
                queryName,
                queryDurationSec,
                totalRequests,
                successRequests,
                errorRequests,
                queryDurationSec > 0 ? totalRequests / queryDurationSec : 0d,
                ComputePercentile(successfulLatencies, 0.50),
                ComputePercentile(successfulLatencies, 0.95),
                ComputePercentile(successfulLatencies, 0.99),
                successfulRows.Count == 0 ? double.NaN : successfulRows.Average(),
                successfulRows.Sum(),
                avgCpu,
                maxCpu,
                avgMem,
                maxMem,
                memLimit,
                netInDelta,
                netOutDelta,
                blockInDelta,
                blockOutDelta);
        }
    }

    private static double ComputeDelta(IEnumerable<double> values)
    {
        var ordered = values.ToList();
        if (ordered.Count < 2)
        {
            return 0d;
        }

        return ordered.Max() - ordered.Min();
    }

    private static double ComputePercentile(IReadOnlyList<double> values, double percentile)
    {
        if (values.Count == 0)
        {
            return double.NaN;
        }

        var ordered = values.OrderBy(value => value).ToList();
        var index = percentile * (ordered.Count - 1);
        var lower = (int)Math.Floor(index);
        var upper = (int)Math.Ceiling(index);
        if (lower == upper)
        {
            return ordered[lower];
        }

        var weight = index - lower;
        return ordered[lower] + ((ordered[upper] - ordered[lower]) * weight);
    }

    private static string BuildBenchmarkQueryForIteration(string queryText, int iterationIndex)
    {
        if (string.IsNullOrWhiteSpace(queryText))
        {
            return queryText;
        }

        var match = BenchmarkIncrementIdRegex.Match(queryText);
        if (!match.Success)
        {
            return queryText;
        }

        if (!int.TryParse(match.Groups[3].Value, out var startId))
        {
            return queryText;
        }

        var nextId = startId + iterationIndex - 1;
        var prefix = match.Groups[1].Value;
        var quote = match.Groups[2].Value;
        var replacement = string.Concat(prefix, quote, nextId.ToString(), quote);

        return BenchmarkIncrementIdRegex.Replace(queryText, replacement, 1);
    }

    private static void SimulateClientResultMaterialization(QueryExecutionResult executionResult)
    {
        var columns = executionResult.Columns.ToList();
        foreach (var row in executionResult.Rows)
        {
            _ = columns
                .Select(column => row.TryGetValue(column, out var value) ? value : string.Empty)
                .ToList();
        }
    }

    private void ApplyExecutionResult(QueryExecutionResult executionResult)
    {
        QueryColumns.Clear();
        foreach (var column in executionResult.Columns)
        {
            QueryColumns.Add(column);
        }

        QueryResults.Clear();
        foreach (var row in executionResult.Rows)
        {
            var cells = QueryColumns
                .Select(column => row.TryGetValue(column, out var value) ? value : string.Empty)
                .ToList();

            QueryResults.Add(new QueryResultRow(cells));
        }
    }
}

public sealed record SequentialQueryDefinition(string QueryName, Func<int, string> BuildQuery);

public sealed record BenchmarkIterationResult(
    bool IsSuccess,
    double ClientLatencyMs,
    int RowsReturned,
    string Error);

public class QueryResultRow
{
    public IReadOnlyList<string> Cells { get; }

    public QueryResultRow(IEnumerable<string> cells)
    {
        Cells = cells.ToList();
    }
}
