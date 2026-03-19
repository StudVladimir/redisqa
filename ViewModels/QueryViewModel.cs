using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using redisqa.Services;

namespace redisqa.ViewModels;

public class QueryViewModel : BaseViewModel
{
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

    public bool CanRunQuery => !IsBusy && !string.IsNullOrWhiteSpace(QueryText);

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
        if (IsBusy || string.IsNullOrWhiteSpace(QueryText))
        {
            return;
        }

        _lastExecutedQuery = QueryText;
        _hasExecutedQuery = true;

        if (resetPage)
        {
            CurrentPage = 1;
        }

        await ExecuteCurrentQueryAsync();
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

    private async Task ExecuteCurrentQueryAsync()
    {
        if (string.IsNullOrWhiteSpace(_lastExecutedQuery))
        {
            return;
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
                return;
            }

            TotalRows = executionResult.TotalRows;
            CurrentPage = executionResult.PageNumber;

            ApplyExecutionResult(executionResult);
            EmptyStateMessage = TotalRows == 0
                ? "No rows found."
                : "Run a query to see results";
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
        }
        finally
        {
            IsBusy = false;
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
