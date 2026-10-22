using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Navigation;
using Gw2LegendaryTracker.App.ViewModels;

namespace Gw2LegendaryTracker.App.Views;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private AppViewModel ViewModel => (AppViewModel)DataContext;

    // PasswordBox cannot be data-bound on purpose: the key is read once, passed on, and the box is cleared.
    private async void OnConnectClick(object sender, RoutedEventArgs e) => await ConnectAsync();

    private async void OnApiKeyBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await ConnectAsync();
        }
    }

    private async Task ConnectAsync()
    {
        var input = ApiKeyBox.Password;
        await ViewModel.ConnectAsync(input);
        if (ViewModel.HasApiKey)
        {
            ApiKeyBox.Clear();
        }
    }

    private async void OnDisconnectClick(object sender, RoutedEventArgs e) => await ViewModel.DisconnectAsync();

    private void OnLinkNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
