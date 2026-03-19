using Avalonia.Controls;
using Avalonia.Interactivity;
using redisqa.ViewModels;

namespace redisqa.Views.QueryView;

public partial class QueryView : UserControl
{
	private readonly QueryViewModel _viewModel;

	public QueryView()
	{
		InitializeComponent();

		_viewModel = new QueryViewModel();
		DataContext = _viewModel;

		var btnRunQuery = this.FindControl<Button>("BtnRunQuery");
		if (btnRunQuery != null)
		{
			btnRunQuery.Click += BtnRunQuery_Click;
		}
	}

	private async void BtnRunQuery_Click(object? sender, RoutedEventArgs e)
	{
		await _viewModel.RunQueryAsync();
	}
}
