using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace AionDPS.Linux.Platform;

/// <summary>
/// Whether this process may read network traffic. On Linux reading packets takes two of the kernel's capabilities, CAP_NET_RAW (packet sockets) and
/// CAP_NET_ADMIN (the capture buffer), which root has and an ordinary program has not; the installer gives them to the app's program file once
/// (<c>setcap cap_net_raw,cap_net_admin=eip</c>, the way Wireshark's dumpcap gets them), so the app itself runs as the user. Nothing else needs them.
/// </summary>
internal static class CaptureRights
{
    private const int CapNetAdmin = 12, CapNetRaw = 13;

    /// <summary>What the process may do now.</summary>
    internal enum Rights
    {
        /// <summary>Root, or the capabilities are in effect.</summary>
        Granted,

        /// <summary>The program file has no capabilities: the installer was not run, or the app was copied or updated since.</summary>
        NotGiven,

        /// <summary>The program file has them but they are not in effect: its folder is on a drive mounted "nosuid", which turns them off.</summary>
        Ignored,
    }

    /// <summary>Where this app's program file is (the one to give the capabilities to).</summary>
    internal static string ProgramPath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "AtreiaApp");

    /// <summary>The one line that gives them, for a terminal.</summary>
    internal static string SetcapCommand => $"sudo setcap cap_net_raw,cap_net_admin=eip '{ProgramPath.Replace("'", "'\\''", StringComparison.Ordinal)}'";

    internal static Rights Check()
    {
        if (Effective() is ulong caps && (caps & (1UL << CapNetRaw)) != 0)
        {
            return Rights.Granted;
        }

        return FileHasCapabilities(ProgramPath) ? Rights.Ignored : Rights.NotGiven;
    }

    /// <summary>The effective capabilities of this process (/proc/self/status, "CapEff: 0000003fffffffff"); null when unreadable.</summary>
    internal static ulong? Effective() => EffectiveFrom(SafeRead("/proc/self/status"));

    internal static ulong? EffectiveFrom(string? status)
    {
        if (status is null)
        {
            return null;
        }

        foreach (string line in status.Split('\n'))
        {
            if (line.StartsWith("CapEff:", StringComparison.Ordinal)
                && ulong.TryParse(line.AsSpan(7).Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong caps))
            {
                return caps;
            }
        }

        return null;
    }

    /// <summary>Whether the program file carries capabilities (its "security.capability" attribute).</summary>
    internal static bool FileHasCapabilities(string path)
    {
        try
        {
            return GetXattr(path, "security.capability", IntPtr.Zero, UIntPtr.Zero) > 0;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    private static string? SafeRead(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    [DllImport("libc", EntryPoint = "getxattr", SetLastError = true)]
    private static extern IntPtr GetXattr([MarshalAs(UnmanagedType.LPUTF8Str)] string path, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, IntPtr value, UIntPtr size);
}
