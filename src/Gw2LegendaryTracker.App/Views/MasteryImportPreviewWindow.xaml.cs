using System.Globalization;
using System.Windows;
using Gw2LegendaryTracker.Core.Masteries;

namespace Gw2LegendaryTracker.App.Views;

/// <summary>Shows what an import would do before the active plan is replaced. Returns <c>true</c> from ShowDialog to go ahead.</summary>
public partial class MasteryImportPreviewWindow : Window
{
    public MasteryImportPreviewWindow(MasteryImportPreview preview, bool replacesPlan)
    {
        ArgumentNullException.ThrowIfNull(preview);
        InitializeComponent();
        DataContext = new PreviewModel(preview, replacesPlan);
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e) => DialogResult = true;

    private sealed class PreviewModel
    {
        public PreviewModel(MasteryImportPreview preview, bool replacesPlan)
        {
            var plan = preview.Result.Plan!;
            var culture = CultureInfo.CurrentCulture;
            Title = plan.Title;
            Summary = plan.Summary;
            CountsText = string.Create(culture, $"{preview.PhaseCount} {(preview.PhaseCount == 1 ? "fase" : "fases")} met {preview.StepCount} {(preview.StepCount == 1 ? "stap" : "stappen")}");
            ChoicesText = (preview.KeptChoices, preview.DroppedChoices) switch
            {
                (0, 0) => "Er zijn nog geen afvinkingen die mee moeten.",
                (var kept, 0) => string.Create(culture, $"{kept} afvinking(en) blijven staan: die stappen staan ook in het nieuwe plan."),
                (var kept, var dropped) => string.Create(culture, $"{kept} afvinking(en) blijven staan; {dropped} vervallen, omdat die stappen niet meer in het plan staan."),
            };
            ReplaceText = replacesPlan
                ? "Dit vervangt je huidige plan. Er wordt eerst automatisch een back-up van al je gegevens gemaakt."
                : "Er wordt eerst automatisch een back-up van al je gegevens gemaakt.";
            ConfirmText = replacesPlan ? "Plan vervangen" : "Plan importeren";
            Warnings = preview.Result.Warnings;
            WarningsHeading = string.Create(culture, $"Gecorrigeerd of weggelaten ({Warnings.Count})");
        }

        public string Title { get; }

        public string Summary { get; }

        public bool HasSummary => Summary.Length > 0;

        public string CountsText { get; }

        public string ChoicesText { get; }

        public string ReplaceText { get; }

        public string ConfirmText { get; }

        public IReadOnlyList<string> Warnings { get; }

        public bool HasWarnings => Warnings.Count > 0;

        public string WarningsHeading { get; }
    }
}
