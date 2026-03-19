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

		var btnPrevPage = this.FindControl<Button>("BtnPrevPage");
		if (btnPrevPage != null)
		{
			btnPrevPage.Click += BtnPrevPage_Click;
		}

		var btnNextPage = this.FindControl<Button>("BtnNextPage");
		if (btnNextPage != null)
		{
			btnNextPage.Click += BtnNextPage_Click;
		}

		var pageSizeCombo = this.FindControl<ComboBox>("PageSizeCombo");
		if (pageSizeCombo != null)
		{
			pageSizeCombo.SelectionChanged += PageSizeCombo_SelectionChanged;
		}
	}

	private async void BtnRunQuery_Click(object? sender, RoutedEventArgs e)
	{
		await _viewModel.RunQueryAsync(resetPage: true);
	}

	private async void BtnPrevPage_Click(object? sender, RoutedEventArgs e)
	{
		await _viewModel.LoadPreviousPageAsync();
	}

	private async void BtnNextPage_Click(object? sender, RoutedEventArgs e)
	{
		await _viewModel.LoadNextPageAsync();
	}

	private async void PageSizeCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
	{
		await _viewModel.ReloadWithCurrentPageSizeAsync();
	}
}
