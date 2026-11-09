using Velopack;
using Velopack.Sources;

namespace AionDPS.Update;

/// <summary>
/// Self-updating, via Velopack. The meter installs per-user under %LOCALAPPDATA%, so it can
/// replace its own files without elevation: an update downloads in the background and is swapped
/// in on the next start, with no installer to run and no UAC prompt. That is the entire reason
/// the WiX/MSI packaging was dropped -- a per-machine install under Program Files is only
/// writable by an elevated process, and this app deliberately stopped asking for that when the
/// packet-capture path was removed.
///
/// <para>
/// The updates come from the website (Brand.UpdatesUrl): the update index releases.&lt;channel&gt;.json and the packages it names. A test release
/// has a channel of its own, so an installed full release never sees it as an update.
/// </para>
/// </summary>
public static class UpdateService
{
    // From the website (Brand.UpdatesUrl) since 8 November 2026, no longer from the GitHub releases: the update index releases.<channel>.json and the
    // packages it names are put there with the release (the site's tools/big-file.mjs).
    private static readonly UpdateManager Manager =
        new(new SimpleWebSource(Brand.UpdatesUrl));

    /// <summary>
    /// False for a build that Velopack did not install -- a "dotnet run" during development, or a
    /// copy someone unzipped by hand. There is nothing to update in place there, and asking would
    /// only produce errors, so every entry point checks this first.
    /// </summary>
    public static bool CanUpdate => Manager.IsInstalled;

    /// <summary>Newest release if it is newer than what is running, null if up to date.</summary>
    public static Task<UpdateInfo?> CheckAsync() => Manager.CheckForUpdatesAsync();

    /// <summary>
    /// Fetches the update into Velopack's staging area. Delta packages mean this is usually a
    /// fraction of the full app, and nothing is swapped until <see cref="ApplyAndRestart"/> runs,
    /// so an interrupted download leaves the installed version untouched.
    /// </summary>
    public static Task DownloadAsync(UpdateInfo update, Action<int>? progress = null) =>
        Manager.DownloadUpdatesAsync(update, progress);

    /// <summary>
    /// Swaps in the downloaded version and relaunches. This exits the process, so callers must
    /// treat it as the last thing they do -- anything that still needs saving has to be saved
    /// before the call, not after it.
    /// </summary>
    public static void ApplyAndRestart(UpdateInfo update) => Manager.ApplyUpdatesAndRestart(update);
}
