using Microsoft.Win32;

namespace AionDPS.Ui;

/// <summary>
/// "Start automatically with Windows" (App menu) - a per-user HKCU Run-key entry, no admin rights
/// needed (matches this app's own asInvoker philosophy - see Program.cs's own remarks: the
/// installer handles elevation, this entry needs none). Points at whatever exe is CURRENTLY
/// running, which under Velopack is always the same stable "current" launcher path regardless of
/// which version is installed, so this never goes stale across an update.
/// </summary>
internal static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "AtreiaApp";

    /// <summary>The entry's name until 9 November 2026 (the owner: the app's files carry its name).</summary>
    private const string OldValueName = "A2HMeter";

    /// <summary>An entry under the old name goes on under the app's name, pointing where it pointed (an old install's entry is pointed at this one by
    /// <see cref="Repoint"/>).</summary>
    public static void RenameOldEntry()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key?.GetValue(OldValueName) is string value)
            {
                if (value.Length > 0 && key.GetValue(ValueName) is null)
                {
                    key.SetValue(ValueName, value);
                }

                key.DeleteValue(OldValueName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            // left as it is: it still starts the meter
        }
    }

    /// <summary>Whether SOME value is registered under this app's name - not an exact path match,
    /// so a Run-key entry written by an older/different install of this same app still reads as
    /// "on" rather than silently flipping the menu's checkmark off.</summary>
    public static bool IsEnabled()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is string value && value.Length > 0;
    }

    /// <summary>An entry written by the install in <paramref name="oldFolder"/> (the move to %LOCALAPPDATA%\AtreiaApp, Update/InstallMove) starts this copy
    /// from now on; an entry pointing anywhere else is left alone.</summary>
    public static void Repoint(string oldFolder)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        if (key?.GetValue(ValueName) is string value && value.Contains(oldFolder, StringComparison.OrdinalIgnoreCase))
        {
            SetEnabled(true);
        }
    }

    public static void SetEnabled(bool enabled)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        if (enabled)
        {
            string exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName
                ?? System.Reflection.Assembly.GetExecutingAssembly().Location;
            key.SetValue(ValueName, $"\"{exePath}\"");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }

        key.DeleteValue(OldValueName, throwOnMissingValue: false);
    }
}
