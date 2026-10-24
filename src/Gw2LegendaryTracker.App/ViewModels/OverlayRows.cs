using System.Globalization;
using Gw2LegendaryTracker.Core.Goals;

namespace Gw2LegendaryTracker.App.ViewModels;

/// <summary>A material in the expanded overlay: what the goal needs, what is owned, what is still missing.</summary>
public sealed record MaterialRowVm(string Name, string? IconUrl, long Needed, long Owned, long Missing)
{
    public string Text => Missing > 0
        ? string.Create(CultureInfo.CurrentCulture, $"heb {Owned:N0} · nog {Missing:N0}")
        : string.Create(CultureInfo.CurrentCulture, $"heb {Owned:N0} · genoeg");

    public bool IsMissing => Missing > 0;

    /// <summary>Overlay: what is covered out of what is needed, e.g. <c>120 / 250</c>.</summary>
    public string CoverText => string.Create(CultureInfo.CurrentCulture, $"{Math.Max(0, Needed - Missing):N0} / {Needed:N0}");

    public double Ratio => Coverage.Ratio(Math.Max(0, Needed - Missing), Needed);
}

public sealed record ReservationRowVm(string Text);

public sealed record CurrencyRowVm(string Name, long Needed, long Owned, long Missing)
{
    public string Text => Missing > 0
        ? string.Create(CultureInfo.CurrentCulture, $"heb {Owned:N0} van {Needed:N0}")
        : string.Create(CultureInfo.CurrentCulture, $"heb {Owned:N0} · genoeg");

    public bool IsMissing => Missing > 0;

    public double Ratio => Coverage.Ratio(Math.Min(Owned, Needed), Needed);
}

/// <summary>One position of the crafting tree. Remembers whether the user expanded it, across recalculations.</summary>
public sealed class TreeNodeRow : ObservableObject
{
    private readonly Action<string, bool> _onExpandedChanged;
    private bool _isExpanded;

    public TreeNodeRow(
        string path,
        string name,
        string? iconUrl,
        long required,
        string statusText,
        TreeState state,
        IReadOnlyList<TreeNodeRow> children,
        bool isExpanded,
        Action<string, bool> onExpandedChanged)
    {
        Path = path;
        Label = required > 1 ? string.Create(CultureInfo.CurrentCulture, $"{required:N0}× {name}") : name;
        IconUrl = iconUrl;
        StatusText = statusText;
        State = state;
        Children = children;
        _isExpanded = isExpanded;
        _onExpandedChanged = onExpandedChanged;
    }

    public string Path { get; }

    public string Label { get; }

    public string? IconUrl { get; }

    public string StatusText { get; }

    public TreeState State { get; }

    public IReadOnlyList<TreeNodeRow> Children { get; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded != value)
            {
                _isExpanded = value;
                RaisePropertyChanged(nameof(IsExpanded));
                _onExpandedChanged(Path, value);
            }
        }
    }
}
