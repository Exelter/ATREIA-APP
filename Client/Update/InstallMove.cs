using System.Diagnostics;
using System.IO;
using AionDPS.Ui;
using Microsoft.Win32;

namespace AionDPS.Update;

/// <summary>
/// The install moved from %LOCALAPPDATA%\A2HMeter (the meter's first name) to %LOCALAPPDATA%\AtreiaApp (8 November 2026, the owner: "it installs under
/// folder a2hmeter, it should install in the app name"). The updater names the folder after the package id, and an installed copy only ever updates within
/// its own id, so an install in the old folder cannot update itself into the new one. It was meant to get one last update that moved it, but the old
/// installs read their updates from the old GitHub repository, which is gone (9 November 2026), so nothing can reach them any more: their players install
/// once from atreia.app (Brand.Site/download/Atreia-App-Setup.exe).
/// The new install then tidies up after the old one on its first start (<see cref="CleanUpOldInstall"/>): shortcuts and the "start with Windows" entry
/// that still point into the old folder are pointed at the new one, the old entry in Windows' installed apps goes, and the old folder is deleted.
/// Settings, characters and history are in %APPDATA%\A2HMeter (<see cref="Brand.DataFolder"/>) and stay where they are: both installs read them there.
/// </summary>
public static class InstallMove
{
    public const string OldId = "A2HMeter";
    public const string NewId = "AtreiaApp";
    public const string NewExe = "AtreiaApp.exe";

    /// <summary>The folder the updater installed this copy into (the one with Update.exe, above "current"); null for a copy it did not install.</summary>
    private static string? InstallRoot()
    {
        try
        {
            string current = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!string.Equals(Path.GetFileName(current), "current", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            string? root = Path.GetDirectoryName(current);
            return root is not null && File.Exists(Path.Combine(root, "Update.exe")) ? root : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool IsNamed(string root, string id) => string.Equals(Path.GetFileName(root), id, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// In the new install, on every start until it is done (in the background): when the old folder is still there next to this one, the shortcuts and
    /// the "start with Windows" entry that point into it are pointed here, its entry in Windows' installed apps is removed and the folder is deleted. Waits
    /// for an old copy that is still closing. Whatever fails is tried again at the next start.
    /// </summary>
    public static void CleanUpOldInstall()
    {
        if (InstallRoot() is not { } root || !IsNamed(root, NewId) || Path.GetDirectoryName(root) is not { } parent)
        {
            return;
        }

        string old = Path.Combine(parent, OldId);
        if (!File.Exists(Path.Combine(old, "Update.exe")))
        {
            return;
        }

        try
        {
            WaitForOldCopies(old, TimeSpan.FromSeconds(60));
            string newStub = File.Exists(Path.Combine(root, NewExe)) ? Path.Combine(root, NewExe) : Environment.ProcessPath ?? Path.Combine(root, "current", NewExe);
            PointShortcuts(old, newStub, Path.Combine(root, "current"));
            StartupRegistration.Repoint(old);
            RemoveUninstallEntry(old);
            Directory.Delete(old, recursive: true);
            Log("removed the old install at " + old);
        }
        catch (Exception ex)
        {
            Log("the old install could not be removed yet: " + ex.Message);
        }
    }

    private static bool Under(string? path, string folder) =>
        path is { Length: > 0 } && Path.GetFullPath(path).StartsWith(folder.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);

    private static void WaitForOldCopies(string old, TimeSpan limit)
    {
        var until = DateTime.UtcNow + limit;
        while (DateTime.UtcNow < until)
        {
            bool running = false;
            foreach (Process p in Process.GetProcesses())
            {
                try
                {
                    if (p.ProcessName is OldId or "AtreiaApp" or "Update" && Under(p.MainModule?.FileName, old))
                    {
                        running = true;
                    }
                }
                catch (Exception)
                {
                    // a process of another user or one that just ended
                }
                finally
                {
                    p.Dispose();
                }
            }

            if (!running)
            {
                return;
            }

            Thread.Sleep(1000);
        }
    }

    /// <summary>Desktop, Start menu and pinned task bar shortcuts into the old folder point into the new one.</summary>
    private static void PointShortcuts(string old, string newStub, string newCurrent)
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string[] folders =
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            Environment.GetFolderPath(Environment.SpecialFolder.Startup),
            Path.Combine(appData, @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar"),
            Path.Combine(appData, @"Microsoft\Internet Explorer\Quick Launch"),
        };

        Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null)
        {
            return;
        }

        dynamic shell = Activator.CreateInstance(shellType)!;
        foreach (string folder in folders.Where(f => f.Length > 0 && Directory.Exists(f)))
        {
            IEnumerable<string> links;
            try
            {
                links = Directory.EnumerateFiles(folder, "*.lnk", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 2, IgnoreInaccessible = true }).ToList();
            }
            catch (Exception)
            {
                continue;
            }

            foreach (string link in links)
            {
                try
                {
                    dynamic shortcut = shell.CreateShortcut(link);
                    string target = (string)shortcut.TargetPath;
                    if (!Under(target, old))
                    {
                        continue;
                    }

                    shortcut.TargetPath = newStub;
                    shortcut.WorkingDirectory = newCurrent;
                    shortcut.IconLocation = newStub + ",0";
                    shortcut.Save();
                    Log("pointed " + link + " at " + newStub);
                }
                catch (Exception)
                {
                    // a shortcut that cannot be read or written: left as it is
                }
            }
        }
    }

    /// <summary>The old install's entry in Windows' installed apps (the updater writes it under the package id), only when it is the one of the old folder.</summary>
    private static void RemoveUninstallEntry(string old)
    {
        const string Apps = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";
        using RegistryKey? apps = Registry.CurrentUser.OpenSubKey(Apps, writable: true);
        using RegistryKey? entry = apps?.OpenSubKey(OldId);
        if (apps is null || entry is null)
        {
            return;
        }

        string where = (entry.GetValue("InstallLocation") as string) ?? (entry.GetValue("UninstallString") as string) ?? "";
        if (where.Contains(old, StringComparison.OrdinalIgnoreCase))
        {
            entry.Dispose();
            apps.DeleteSubKeyTree(OldId, throwOnMissingSubKey: false);
        }
    }

    private static void Log(string line)
    {
        try
        {
            File.AppendAllText(Path.Combine(MeterSettings.DataDirectory, "install-move.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // nowhere to write: nothing else to do
        }
    }
}
