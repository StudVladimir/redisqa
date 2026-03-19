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
    private string _queryErrorMessage = string.Empty;
    private string _emptyStateMessage = "Run a query to see results";

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

    public ObservableCollection<string> QueryColumns { get; } = new();

    public ObservableCollection<QueryResultRow> QueryResults { get; } = new();

    public bool HasResults => QueryResults.Count > 0;

    public QueryViewModel()
    {
        QueryResults.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasResults));
        };
    }

    public async Task RunQueryAsync()
    {
        if (!CanRunQuery)
        {
            return;
        }

        IsBusy = true;
        QueryErrorMessage = string.Empty;

        try
        {
            var executionResult = await _queryExecutor.ExecuteAsync(QueryText, SelectedDb);

            if (!executionResult.IsSuccess)
            {
                QueryColumns.Clear();
                QueryResults.Clear();
                QueryErrorMessage = executionResult.ErrorMessage ?? "Query execution failed.";
                EmptyStateMessage = "Run a valid query to see results.";
                return;
            }

            ApplyExecutionResult(executionResult);
            EmptyStateMessage = QueryResults.Count == 0
                ? "No rows found."
                : "Run a query to see results";
        }
        catch (System.Exception ex)
        {
            QueryColumns.Clear();
            QueryResults.Clear();
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
    public ObservableCollection<string> Cells { get; }

    public QueryResultRow(IEnumerable<string> cells)
    {
        Cells = new ObservableCollection<string>(cells);
    }
}
