using System.Diagnostics;

namespace VirtualPlc;

public static class DashboardLauncher
{
    public static void TryOpen(DashboardOptions options, ILogger logger)
    {
        if (!options.OpenBrowserOnStart)
        {
            return;
        }

        if (!Uri.TryCreate(options.Url, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            logger.LogWarning("Dashboard URL is invalid and will not be opened: {Url}", options.Url);
            return;
        }

        if (OperatingSystem.IsLinux() &&
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY")) &&
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
        {
            logger.LogInformation(
                "No graphical desktop detected. Open the Virtual PLC dashboard manually: {Url}",
                uri.AbsoluteUri);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = uri.AbsoluteUri,
                UseShellExecute = true
            });
            logger.LogInformation("Virtual PLC dashboard opened: {Url}", uri.AbsoluteUri);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Could not open the dashboard automatically. Open it manually: {Url}",
                uri.AbsoluteUri);
        }
    }
}
