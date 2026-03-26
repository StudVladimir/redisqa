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
                               && !string.IsNullOrWhiteSpace(BenchmarkQueryName);

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
        if (IsBusy || string.IsNullOrWhiteSpace(QueryText) || string.IsNullOrWhiteSpace(BenchmarkQueryName))
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

public class QueryResultRow
{
    public IReadOnlyList<string> Cells { get; }

    public QueryResultRow(IEnumerable<string> cells)
    {
        Cells = cells.ToList();
    }
}
