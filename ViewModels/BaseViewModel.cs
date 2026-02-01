using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace redisqa.ViewModels;

public class BaseViewModel : INotifyPropertyChanged
{
    // Событие которое срабатывает когда свойство меняется
    public event PropertyChangedEventHandler? PropertyChanged;

    // Метод для уведомления UI
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}