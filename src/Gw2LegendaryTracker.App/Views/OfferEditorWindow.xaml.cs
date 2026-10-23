using System.Windows;
using Gw2LegendaryTracker.App.ViewModels;

namespace Gw2LegendaryTracker.App.Views;

public partial class OfferEditorWindow : Window
{
    public OfferEditorWindow(OfferEditorViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private OfferEditorViewModel ViewModel => (OfferEditorViewModel)DataContext;

    private async void OnLoaded(object sender, RoutedEventArgs e) => await ViewModel.LoadAsync();

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (await ViewModel.SaveAsync())
        {
            DialogResult = true;
        }
    }
}
