using System.Collections.ObjectModel;

namespace redisqa.ViewModels;

public class QueryViewModel : BaseViewModel
{
    private int _selectedDb;
    private string _queryText = "";
    private bool _isBusy;
    private ObservableCollection<QueryResultRow> _queryResults = new();

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

    public bool CanRunQuery => !IsBusy && !string.IsNullOrWhiteSpace(QueryText);

    public ObservableCollection<QueryResultRow> QueryResults
    {
        get => _queryResults;
        set
        {
            _queryResults = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasResults));
        }
    }

    public bool HasResults => QueryResults.Count > 0;

    public QueryViewModel()
    {
        QueryResults.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasResults));
        };
    }
}

public class QueryResultRow
{
    public string Column1 { get; set; } = "";
    public string Column2 { get; set; } = "";
    public string Column3 { get; set; } = "";
    public string Column4 { get; set; } = "";
}
