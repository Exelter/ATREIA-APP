using System.Runtime.CompilerServices;
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
///
/// <para>
/// The betas (the "test" channel) for the Contributors (10 November 2026): an app whose Discord sign-in has the Contributor role in the Atreia
/// Discord (<see cref="BetaAccess"/>) looks at the test channel as well and takes whichever version is newer, so the beta comes like any update.
/// A beta looks at the full releases as well, so it moves on to the full version once that is out (2.0.0 comes after 2.0.0-beta.3).
/// Moving from one channel to the other is always a full download: the other channel's small update packages are made from its own versions.
/// </para>
/// </summary>
public static class UpdateService
{
    // From the website (Brand.UpdatesUrl) since 8 November 2026, no longer from the GitHub releases: the update index releases.<channel>.json and the
    // packages it names are put there with the release (the site's tools/big-file.mjs).
    private static readonly UpdateManager Manager =
        new(new SimpleWebSource(Brand.UpdatesUrl));

    // The other channel, the same place: the betas for a Contributor's full release, the full releases for a beta.
    private static readonly UpdateManager Betas =
        new(new SimpleWebSource(Brand.UpdatesUrl), new UpdateOptions { ExplicitChannel = "test", MaximumDeltasBeforeFallback = -1 });

    private static readonly UpdateManager FullReleases =
        new(new SimpleWebSource(Brand.UpdatesUrl), new UpdateOptions { ExplicitChannel = "stable", MaximumDeltasBeforeFallback = -1 });

    // which of them found each update: its download comes from the same channel
    private static readonly ConditionalWeakTable<UpdateInfo, UpdateManager> FoundBy = new();

    /// <summary>
    /// False for a build that Velopack did not install -- a "dotnet run" during development, or a
    /// copy someone unzipped by hand. There is nothing to update in place there, and asking would
    /// only produce errors, so every entry point checks this first.
    /// </summary>
    public static bool CanUpdate => Manager.IsInstalled;

    /// <summary>True when the running version is a beta (a version with a suffix, 2.0.0-beta).</summary>
    public static bool OnBeta => Manager.CurrentVersion?.IsPrerelease == true;

    /// <summary>
    /// Newest release if it is newer than what is running, null if up to date. The installed channel first (as before; an error there is the
    /// check's error), then for a beta the full releases, and for a Contributor's full release the betas: the newer one wins.
    /// </summary>
    public static async Task<UpdateInfo?> CheckAsync()
    {
        UpdateInfo? best = await Manager.CheckForUpdatesAsync();
        UpdateManager from = Manager;
        UpdateManager? other = OnBeta ? FullReleases : await BetaAccess.ActiveAsync() ? Betas : null;
        if (other is not null)
        {
            try
            {
                UpdateInfo? found = await other.CheckForUpdatesAsync();
                if (found is not null && (best is null || found.TargetFullRelease.Version.CompareTo(best.TargetFullRelease.Version) > 0))
                {
                    best = found;
                    from = other;
                }
            }
            catch (Exception)
            {
                // the other channel can't be read right now (no beta out, or the site is busy): the installed channel's answer stands
            }
        }

        if (best is not null)
        {
            FoundBy.AddOrUpdate(best, from);
        }

        return best;
    }

    /// <summary>
    /// Fetches the update into Velopack's staging area. Delta packages mean this is usually a
    /// fraction of the full app, and nothing is swapped until <see cref="ApplyAndRestart"/> runs,
    /// so an interrupted download leaves the installed version untouched.
    /// </summary>
    public static Task DownloadAsync(UpdateInfo update, Action<int>? progress = null) =>
        (FoundBy.TryGetValue(update, out UpdateManager? from) ? from : Manager).DownloadUpdatesAsync(update, progress);

    /// <summary>
    /// Swaps in the downloaded version and relaunches. This exits the process, so callers must
    /// treat it as the last thing they do -- anything that still needs saving has to be saved
    /// before the call, not after it.
    /// </summary>
    public static void ApplyAndRestart(UpdateInfo update) => (FoundBy.TryGetValue(update, out UpdateManager? from) ? from : Manager).ApplyUpdatesAndRestart(update);
}
