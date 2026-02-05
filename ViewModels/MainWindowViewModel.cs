namespace redisqa.ViewModels;

public class MainWindowViewModel: BaseViewModel
{
    private object? _currentView;
    public object? CurrentView 
    {
        get => _currentView;
        set
        {
            _currentView = value;
            OnPropertyChanged();
        }
    }

    private int? _selectedDb;
    public int? SelectedDb
    {
        get => _selectedDb;
        set
        {
            _selectedDb = value;
            OnPropertyChanged();
        }
    }

    public MainWindowViewModel()
    {
        // При запуске показываем HomeView
        CurrentView = new Views.HomeView();
    }

    public void NavigateToHome()
    {
        CurrentView = new Views.HomeView();
    }

    public void NavigateToSchema()
    {
        CurrentView = new Views.SchemaView.SchemaView();
    }
}