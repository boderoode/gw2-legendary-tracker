using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Navigation;
using Microsoft.Win32;
using Gw2LegendaryTracker.App.ViewModels;
using Gw2LegendaryTracker.Infrastructure.Persistence;

namespace Gw2LegendaryTracker.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.NewValue is AppViewModel viewModel)
            {
                viewModel.Goals.AddSourceRequested += OnAddSourceRequested;
                viewModel.Masteries.ClipboardRequested += OnClipboardRequested;
            }
        };
    }

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

    private async void OnForgeConnectClick(object sender, RoutedEventArgs e) => await ConnectForgeAsync();

    private async void OnForgeKeyBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await ConnectForgeAsync();
        }
    }

    private async Task ConnectForgeAsync()
    {
        await ViewModel.RecipeData.ConnectAsync(ForgeKeyBox.Password);
        if (ViewModel.RecipeData.HasKey)
        {
            ForgeKeyBox.Clear();
        }
    }

    private async void OnForgeDisconnectClick(object sender, RoutedEventArgs e) => await ViewModel.RecipeData.DisconnectAsync();

    private async void OnDisconnectClick(object sender, RoutedEventArgs e) => await ViewModel.DisconnectAsync();

    private async void OnExportClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Gegevens exporteren",
            Filter = "Export van GW2 Legendary Tracker (*.json)|*.json",
            FileName = $"gw2-legendary-tracker-{DateTime.Now:yyyyMMdd}.json",
            AddExtension = true,
            DefaultExt = ".json",
        };

        if (dialog.ShowDialog(this) == true)
        {
            await ViewModel.DataTransfer.ExportAsync(dialog.FileName);
        }
    }

    private async void OnImportClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Gegevens importeren",
            Filter = "Export van GW2 Legendary Tracker (*.json)|*.json",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var answer = MessageBox.Show(
            this,
            "Dit vervangt je huidige Legendaries, afvinkingen, notities en instellingen door de inhoud van het bestand.\n\nEr wordt eerst automatisch een back-up gemaakt. Doorgaan?",
            "Importeren",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);

        if (answer == MessageBoxResult.Yes)
        {
            await ViewModel.DataTransfer.ImportAsync(dialog.FileName);
        }
    }

    private void OnClipboardRequested(object? sender, ClipboardRequest request)
    {
        try
        {
            Clipboard.SetText(request.Text);
            ViewModel.Masteries.Report(false, request.SuccessMessage);
        }
        catch (ExternalException ex)
        {
            // Another program can hold the clipboard for a moment.
            ViewModel.Masteries.Report(true, $"Kopiëren naar het klembord is niet gelukt. Probeer het nog eens. ({ex.Message})");
        }
    }

    private async void OnPastePlanClick(object sender, RoutedEventArgs e)
    {
        string text;
        try
        {
            text = Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty;
        }
        catch (ExternalException ex)
        {
            ViewModel.Masteries.Report(true, $"Het klembord kon niet worden gelezen. Probeer het nog eens. ({ex.Message})");
            return;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            ViewModel.Masteries.Report(true, "Er staat geen tekst op het klembord. Kopieer eerst het antwoord van de AI.");
            return;
        }

        await ImportPlanTextAsync(text);
    }

    private async void OnImportPlanFileClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Masteryplan importeren",
            Filter = "Masteryplan (*.json;*.txt;*.md)|*.json;*.txt;*.md|Alle bestanden (*.*)|*.*",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var read = await UserDataFiles.ReadAsync(dialog.FileName);
        if (read.Text is null)
        {
            ViewModel.Masteries.Report(true, read.Error ?? "Het bestand kon niet worden gelezen.");
            return;
        }

        await ImportPlanTextAsync(read.Text);
    }

    private async Task ImportPlanTextAsync(string text)
    {
        var masteries = ViewModel.Masteries;
        var preview = masteries.PreviewImport(text);
        if (!preview.Succeeded)
        {
            return;
        }

        var confirm = new MasteryImportPreviewWindow(preview, replacesPlan: masteries.HasPlan) { Owner = this };
        if (confirm.ShowDialog() == true)
        {
            await masteries.ApplyImportAsync(preview);
        }
    }

    private async void OnExportPlanClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Masteryplan exporteren",
            Filter = "Masteryplan (*.json)|*.json",
            FileName = $"gw2-mastery-plan-{DateTime.Now:yyyyMMdd}.json",
            AddExtension = true,
            DefaultExt = ".json",
        };

        if (dialog.ShowDialog(this) == true)
        {
            await ViewModel.Masteries.ExportAsync(dialog.FileName);
        }
    }

    private void OnAddSourceRequested(object? sender, UnbuyableRowVm row)
    {
        var editor = ViewModel.Acquisitions.CreateEditor(row.ItemId, row.Name);
        new OfferEditorWindow(editor) { Owner = this }.ShowDialog();
    }

    private void OnOpenCustomDataFolderClick(object sender, RoutedEventArgs e) => OpenFolder(ViewModel.Acquisitions.CustomDataFolder);

    private void OnOpenLogFolderClick(object sender, RoutedEventArgs e) => OpenFolder(ViewModel.DataTransfer.LogFolder);

    private void OnOpenBackupFolderClick(object sender, RoutedEventArgs e) => OpenFolder(ViewModel.DataTransfer.BackupFolder);

    private static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private void OnLinkNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
