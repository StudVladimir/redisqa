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

    public MainWindowViewModel()
    {
        // При запуске показываем HomeView
        CurrentView = new Views.HomeView();
    }

    public void NavigateToHome()
    {
        CurrentView = new Views.HomeView();
    }

    public void NavigateToSecond()
    {
        CurrentView = new Views.SecondView();
    }
}