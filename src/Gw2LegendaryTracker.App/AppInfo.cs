namespace Gw2LegendaryTracker.App;

internal static class AppInfo
{
    /// <summary>The version from the project file, e.g. <c>0.9.0</c>.</summary>
    public static string Version { get; } = typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? "onbekend";
}
