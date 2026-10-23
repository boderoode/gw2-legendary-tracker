using System.ComponentModel;
using System.Diagnostics;
using Gw2LegendaryTracker.Core.Links;

namespace Gw2LegendaryTracker.App.Services;

internal static class LinkOpener
{
    /// <summary>Opens a link in the default browser, but only https links to the sites in <see cref="ItemLinks"/>.</summary>
    public static void Open(string? url)
    {
        if (!ItemLinks.IsSafe(url))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(url!) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            // No default browser is set up: there is nothing sensible to do, and the app must not stop for it.
        }
    }
}
