using AionDPS.Aion2.Capture;
using AionDPS.Combat.Sources;
using AionDPS.Linux.Meter;
using AionDPS.Linux.Platform;

namespace AionDPS.Linux.Ui;

/// <summary>
/// What the meter's status line says: first what keeps it from working (the right to read the network, libpcap), then what the capture says, the image
/// pack's download and a passing note. The Windows app says the same things about Npcap; here they name what a Linux system has instead.
/// </summary>
internal static class StatusLine
{
    public const string CopyCommand = "Copy the command";

    public static (string Text, bool Warning, string? Action) For(CaptureRights.Rights rights, MeterEngine engine, string? note, string? images)
    {
        switch (rights)
        {
            case CaptureRights.Rights.NotGiven:
                return ("Atreia App may not read the network yet. Run the installer (install.sh), or give the right once in a terminal, then start the app again: "
                    + CaptureRights.SetcapCommand, true, CopyCommand);
            case CaptureRights.Rights.Ignored:
                return ("The app's folder is on a drive mounted \"nosuid\", which takes the right to read the network away. Install it elsewhere (install.sh puts it in "
                    + "~/.local/share/atreia-app) or remount the drive without nosuid.", true, null);
        }

        SourceStatus status = engine.Status;
        string text;
        bool warning = false;
        if (status.State == SourceState.Error && status.Message.Contains("Npcap is not installed", StringComparison.Ordinal))
        {
            text = "libpcap is missing (the library that reads network traffic). Install it with your package manager - Debian and Ubuntu: libpcap0.8, "
                + "Fedora, Arch and SteamOS: libpcap - and start the app again.";
            warning = true;
        }
        else if (status.State == SourceState.Error)
        {
            text = status.Message.Replace("Npcap", "libpcap", StringComparison.Ordinal);
            warning = true;
        }
        else if (engine.Source is null || (status.State == SourceState.Waiting && !engine.Capturing))
        {
            text = GameConnections.GameRunning() ? "The game is running - waiting for its traffic (it comes with the first fight or map change)." : "Waiting for the game.";
        }
        else if (engine.Paused)
        {
            text = "Paused.";
        }
        else
        {
            text = "";
        }

        if (note is not null)
        {
            text = note;
        }

        if (images is not null)
        {
            text = text.Length > 0 ? images + "  ·  " + text : images;
        }

        return (text, warning, null);
    }
}
