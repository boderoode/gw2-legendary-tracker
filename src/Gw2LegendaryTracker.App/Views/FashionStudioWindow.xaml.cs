using System.Windows;
using Gw2LegendaryTracker.App.ViewModels;

namespace Gw2LegendaryTracker.App.Views;

public partial class FashionStudioWindow : Window
{
    public FashionStudioWindow(FashionStudioViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private FashionStudioViewModel ViewModel => (FashionStudioViewModel)DataContext;

    private async void OnLoaded(object sender, RoutedEventArgs e) => await ViewModel.InitializeAsync();
}
